using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Serialization;
using Brutal.Numerics;
using Core;
using KSA;

namespace AccelerationAbsorbers;

public struct AccelerationAbsorberState
{
	public bool Active;

	public Watts ConsumedWatts;

	public double CommandedAbsorptionG;

	public double TargetAbsorptionG;

	public double AbsorbedGThisStep;

	public double LastRawG;

	public UniverseTime LastRawGSimSeconds;
}

public struct AccelerationAbsorberGlobalState
{
	public double TotalCapacityG;

	public double TotalDeliverableG;

	public double TotalCommandedG;

	public double TotalPowerW;

	public int ActiveModuleCount;

	public double3 RawG;

	public double AbsorbedG;

	public double AbsorbedFraction;

	public double3 EffectiveG;

	public UniverseTime LastIngestSimSeconds;
}

public class AccelerationAbsorber : ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>, IFlowManagerHost
{
	private const double ResidualEpsilonG = 1e-4;

	private const double DriftEpsilonG = 1e-3;

	private const int MaxDelayQueueSize = 1024;

	private const double RawGStaleSeconds = 2.0;

	private const double EffectiveDeliverableTauSeconds = 0.2;

	private static readonly Brutal.Localization.LString s_enabledSetting = Brutal.Localization.LStrings.Create("AccelerationAbsorberEnabled", "Absorber Enabled");

	private static readonly MethodInfo s_recordsHistoryFor = typeof(VehicleEditor).GetMethod("RecordsHistoryFor", BindingFlags.Static | BindingFlags.NonPublic)
		?? throw new InvalidOperationException("VehicleEditor.RecordsHistoryFor not found");

	[XmlType(TypeName = "AccelerationAbsorber")]
	public class Template : TemplateDataBase
	{
		[XmlElement]
		public PowerReference BasePower = new PowerReference(0.0);

		[XmlElement]
		public PowerReference PowerPerGLoad = new PowerReference(0.0);

		[XmlElement]
		public PowerReference PowerPerTonne = new PowerReference(0.0);

		[XmlElement]
		public PowerReference MaxPower = new PowerReference(0.0);

		[XmlAttribute]
		public double MaxGLoad = 20.0;

		[XmlAttribute]
		public double MinDelayMs = 50.0;

		[XmlAttribute]
		public double MaxDelayMs = 200.0;

		public override void OnDataLoad(Mod mod)
		{
			BasePower.OnDataLoad(mod);
			PowerPerGLoad.OnDataLoad(mod);
			PowerPerTonne.OnDataLoad(mod);
			MaxPower.OnDataLoad(mod);
		}
	}

	[XmlType(TypeName = "AccelerationAbsorberData")]
	public class SaveData : SaveDataBase
	{
		[XmlAttribute("Active")]
		public bool Active;

		public override void OnDataLoad(Mod mod)
		{
		}
	}

	private struct DelayEvent
	{
		public UniverseTime Ready;

		public double DeltaG;
	}

	public required Watts BasePower;

	public required Watts PowerPerGLoad;

	public required Watts PowerPerTonne;

	public required Watts MaxPower;

	public double MaxGLoad;

	public double MinDelayMs;

	public double MaxDelayMs;

	public bool Enabled = true;

	public required PowerManager PowerManager { get; set; }

	private readonly List<DelayEvent> _delayQueue = new List<DelayEvent>(256);

	private double _commandedG;

	private double _smoothedG;

	private double _lastSignalG;

	private double _residualG;

	private double _pendingDeltaSum;

	private double _boostG;

	private double _effectiveDeliverableG = -1.0;

	private bool _needsSeed = true;

	protected AccelerationAbsorber(string templateId)
		: base(templateId)
	{
	}

	public void RecreateManager(PartTree tree)
	{
		bool showFlow = PowerManager.ShowFlow;
		PowerManager.Dispose();
		PowerManager = new PowerManager(Parent);
		PowerManager.ShowFlow = showFlow;
	}

	public void OnDrawUi(IViewport inViewport, ref readonly double4x4 matrixVehicleAsmb2Ego)
	{
		PowerManager.OnDrawUi(inViewport, in matrixVehicleAsmb2Ego);
	}

	public static void CreateComponents(Part part, PartTemplate template, TransformDataReference<SubPartTemplate>? _instance)
	{
		foreach (TemplateDataBase component in template.Components)
		{
			if (component is Template template2)
			{
				AccelerationAbsorber accelerationAbsorber = new AccelerationAbsorber(template2.Key)
				{
					Parent = part,
					BasePower = new Watts((float)(double)template2.BasePower),
					PowerPerGLoad = new Watts((float)(double)template2.PowerPerGLoad),
					PowerPerTonne = new Watts((float)(double)template2.PowerPerTonne),
					MaxPower = new Watts((float)(double)template2.MaxPower),
					MaxGLoad = double.IsFinite(template2.MaxGLoad) && template2.MaxGLoad > 0.0 ? template2.MaxGLoad : 0.0,
					MinDelayMs = double.IsFinite(template2.MinDelayMs) ? Math.Max(0.0, template2.MinDelayMs) : 0.0,
					MaxDelayMs = double.IsFinite(template2.MaxDelayMs) ? Math.Max(0.0, template2.MaxDelayMs) : 0.0,
					PowerManager = new PowerManager(part)
				};
				part.Modules.Add(accelerationAbsorber);
			}
		}
	}

	public override void CreateStates(out AccelerationAbsorberState state, out EmptyStruct fxState)
	{
		state = default;
		fxState = default;
	}

	public static void UpdateModules(ref readonly ModuleUpdateContext context)
	{
		if (!context.Updaters.TryGetOrCreateTypeStateUpdater(out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater typeUpdater) || !ModuleStateful<Battery, BatteryState, EmptyStruct, EmptyStruct>.TryGetOrCreateFrom(context.Updaters, out ModuleStateful<Battery, BatteryState, EmptyStruct, EmptyStruct>.StateUpdater updater))
		{
			return;
		}
		double massTonnes = context.States.Props.TotalMass * 0.001;
		ref AccelerationAbsorberGlobalState globalState = ref typeUpdater.GlobalState;
		globalState.TotalCapacityG = 0.0;
		globalState.TotalDeliverableG = 0.0;
		globalState.TotalCommandedG = 0.0;
		globalState.TotalPowerW = 0.0;
		globalState.ActiveModuleCount = 0;
		int moduleCount = typeUpdater.Modules.Length;
		if (moduleCount == 0)
		{
			return;
		}
		double[] weightPool = ArrayPool<double>.Shared.Rent(moduleCount * 4);
		try
		{
			Span<double> deliverableG = weightPool.AsSpan(0, moduleCount);
			Span<double> effectiveG = weightPool.AsSpan(moduleCount, moduleCount);
			Span<double> delayedG = weightPool.AsSpan(moduleCount * 2, moduleCount);
			Span<double> boostG = weightPool.AsSpan(moduleCount * 3, moduleCount);
			double requiredG = 0.0;
			double sumEffectiveG = 0.0;
			int index = 0;
			ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater.ModuleAndNewStateEnumerator enumerator = typeUpdater.GetModulesAndNewStates().GetEnumerator();
			while (enumerator.MoveNext())
			{
				ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater.ModuleAndNewStateRef current = enumerator.Current;
				AccelerationAbsorber module = current.Module;
				double rawG = 0.0;
				if ((context.Step.NextTime - current.State.LastRawGSimSeconds).Seconds() < RawGStaleSeconds && double.IsFinite(current.State.LastRawG))
				{
					rawG = current.State.LastRawG;
				}
				requiredG = Math.Max(requiredG, rawG);
				delayedG[index] = module.TickDelayQueue(context.Step, rawG);
				double capWatts = module.MaxPower.Value();
				double deliverableDenom = massTonnes * module.PowerPerGLoad.Value() * module.PowerPerTonne.Value();
				double theoreticalG = module.MaxGLoad;
				if (!module.Enabled)
				{
					theoreticalG = 0.0;
				}
				else if (capWatts > 0.0 && deliverableDenom > 1e-12)
				{
					theoreticalG = Math.Min(module.MaxGLoad, Math.Max(0.0, (capWatts - module.BasePower.Value()) / deliverableDenom));
				}
				deliverableG[index] = theoreticalG;
				double moduleEffectiveG = theoreticalG;
				if (theoreticalG > 0.0)
				{
					if (!module.PowerManager.ResourceAvailable(updater.GetReadOnlyStates()))
					{
						moduleEffectiveG = 0.0;
					}
					else
					{
						if (module._effectiveDeliverableG < 0.0)
						{
							module._effectiveDeliverableG = theoreticalG;
						}
						AccelerationAbsorberState prevState = current.State;
						bool wasPowerLimited = prevState.TargetAbsorptionG > prevState.CommandedAbsorptionG + 1e-6;
						if (wasPowerLimited)
						{
							module._effectiveDeliverableG = Math.Min(theoreticalG, Math.Max(0.0, prevState.CommandedAbsorptionG));
						}
						else
						{
							double recover = 1.0 - Math.Exp(-context.Step.DeltaTime / EffectiveDeliverableTauSeconds);
							module._effectiveDeliverableG = Math.Min(theoreticalG, module._effectiveDeliverableG + (theoreticalG - module._effectiveDeliverableG) * recover);
						}
						moduleEffectiveG = Math.Max(0.0, module._effectiveDeliverableG);
					}
				}
				effectiveG[index++] = moduleEffectiveG;
				sumEffectiveG += moduleEffectiveG;
			}
			double sumDelayedSharesG = 0.0;
			for (int i = 0; i < moduleCount; i++)
			{
				double shareG = sumEffectiveG > 0.0 ? delayedG[i] * effectiveG[i] / sumEffectiveG : 0.0;
				shareG = Math.Min(shareG, effectiveG[i]);
				delayedG[i] = shareG;
				boostG[i] = 0.0;
				sumDelayedSharesG += shareG;
			}
			double floorG = Math.Min(requiredG, sumEffectiveG);
			double remainingDeficitG = Math.Max(0.0, floorG - sumDelayedSharesG);
			for (int iter = 0; iter < moduleCount && remainingDeficitG > 1e-9; iter++)
			{
				double headroomSumG = 0.0;
				for (int i = 0; i < moduleCount; i++)
				{
					headroomSumG += Math.Max(0.0, effectiveG[i] - delayedG[i] - boostG[i]);
				}
				if (headroomSumG <= 0.0)
				{
					break;
				}
				double allocatedG = 0.0;
				for (int i = 0; i < moduleCount; i++)
				{
					double headroomG = effectiveG[i] - delayedG[i] - boostG[i];
					if (headroomG <= 0.0)
					{
						continue;
					}
					double addG = Math.Min(remainingDeficitG * headroomG / headroomSumG, headroomG);
					boostG[i] += addG;
					allocatedG += addG;
				}
				if (allocatedG <= 1e-12)
				{
					break;
				}
				remainingDeficitG -= allocatedG;
			}
			enumerator = typeUpdater.GetModulesAndNewStates().GetEnumerator();
			index = 0;
			while (enumerator.MoveNext())
			{
				ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater.ModuleAndNewStateRef current = enumerator.Current;
				AccelerationAbsorber module = current.Module;
				double moduleDeliverableG = deliverableG[index];
				double shareG = delayedG[index];
				double boostTargetG = boostG[index++];
				double boostTau = Math.Clamp((module.MinDelayMs + module.MaxDelayMs) * 0.0005, 0.0, 0.25);
				if (boostTau > 0.0 && context.Step.DeltaTime > 0.0)
				{
					module._boostG += (boostTargetG - module._boostG) * (1.0 - Math.Exp(-context.Step.DeltaTime / boostTau));
				}
				else
				{
					module._boostG = boostTargetG;
				}
				double commandedG = Math.Min(shareG + module._boostG, moduleDeliverableG);
				current.State.AbsorbedGThisStep = 0.0;
				current.State.CommandedAbsorptionG = 0.0;
				current.State.TargetAbsorptionG = 0.0;
				double capWatts = module.MaxPower.Value();
				if (module.Enabled && module.PowerManager.ResourceAvailable(updater.GetReadOnlyStates()))
				{
					double absorptionWattsRequired = massTonnes * module.PowerPerGLoad.Value() * commandedG * module.PowerPerTonne.Value();
					double overheadWatts = module.BasePower.Value();
					double requiredWatts = overheadWatts + absorptionWattsRequired;
					if (capWatts > 0.0)
					{
						requiredWatts = Math.Min(requiredWatts, capWatts);
					}
					Joules joulesChange = new Watts((float)requiredWatts).Integrate(context.Step.DeltaTime);
					module.PowerManager.Consume(updater.GetNewStates(), joulesChange, out var amountChanged);
					double actualWatts = amountChanged.Differentiate(context.Step.DeltaTime).Value();
					current.State.ConsumedWatts = new Watts((float)actualWatts);
					double absorptionBudget = Math.Max(0.0, actualWatts - overheadWatts);
					double powerScale = absorptionWattsRequired > 1e-9 ? Math.Clamp(absorptionBudget / absorptionWattsRequired, 0.0, 1.0) : 1.0;
					current.State.TargetAbsorptionG = commandedG;
					current.State.CommandedAbsorptionG = commandedG * powerScale;
					current.State.Active = amountChanged > Joules.Zero;
				}
				else
				{
					current.State.ConsumedWatts = Watts.Zero;
					current.State.Active = false;
				}
				globalState.TotalCapacityG += module.MaxGLoad;
				globalState.TotalDeliverableG += moduleDeliverableG;
				globalState.TotalCommandedG += current.State.CommandedAbsorptionG;
				globalState.TotalPowerW += current.State.ConsumedWatts.Value();
				if (current.State.Active)
				{
					globalState.ActiveModuleCount++;
				}
			}
		}
		finally
		{
			ArrayPool<double>.Shared.Return(weightPool);
		}
	}

	private double TickDelayQueue(SimStep step, double rawG)
	{
		if (_needsSeed)
		{
			_needsSeed = false;
			double seedG = Math.Max(0.0, rawG);
			_commandedG = seedG;
			_smoothedG = seedG;
			_lastSignalG = seedG;
			_residualG = 0.0;
			_pendingDeltaSum = 0.0;
			_delayQueue.Clear();
			return _smoothedG;
		}
		double signalG = Math.Max(0.0, rawG);
		_residualG += signalG - _lastSignalG;
		_lastSignalG = signalG;
		if (Math.Abs(_residualG) > ResidualEpsilonG)
		{
			double delaySeconds = (MinDelayMs + Random.Shared.NextDouble() * Math.Max(0.0, MaxDelayMs - MinDelayMs)) * 0.001;
			Enqueue(step.NextTime + delaySeconds, _residualG);
			_residualG = 0.0;
		}
		FlushDue(step.NextTime);
		double accountedG = _commandedG + _pendingDeltaSum + _residualG;
		if (_delayQueue.Count >= MaxDelayQueueSize || !double.IsFinite(accountedG) || Math.Abs(signalG - accountedG) > DriftEpsilonG)
		{
			_commandedG = signalG;
			_smoothedG = signalG;
			_residualG = 0.0;
			_pendingDeltaSum = 0.0;
			_delayQueue.Clear();
		}
		double smoothTau = Math.Clamp((MaxDelayMs - MinDelayMs) * 0.00025, 0.0, 0.25);
		if (smoothTau > 0.0 && step.DeltaTime > 0.0)
		{
			double alpha = 1.0 - Math.Exp(-step.DeltaTime / smoothTau);
			_smoothedG += (_commandedG - _smoothedG) * alpha;
		}
		else
		{
			_smoothedG = _commandedG;
		}
		return Math.Max(0.0, _smoothedG);
	}

	private void Enqueue(UniverseTime ready, double deltaG)
	{
		_pendingDeltaSum += deltaG;
		int lo = 0;
		int hi = _delayQueue.Count;
		while (lo < hi)
		{
			int mid = lo + hi >> 1;
			if (_delayQueue[mid].Ready < ready)
			{
				lo = mid + 1;
			}
			else
			{
				hi = mid;
			}
		}
		_delayQueue.Insert(lo, new DelayEvent
		{
			Ready = ready,
			DeltaG = deltaG
		});
	}

	private void FlushDue(UniverseTime now)
	{
		int due = 0;
		while (due < _delayQueue.Count && _delayQueue[due].Ready <= now)
		{
			due++;
		}
		if (due == 0)
		{
			return;
		}
		for (int i = 0; i < due; i++)
		{
			_commandedG += _delayQueue[i].DeltaG;
			_pendingDeltaSum -= _delayQueue[i].DeltaG;
		}
		_delayQueue.RemoveRange(0, due);
	}

	public void SetEnabled(bool enabled)
	{
		Enabled = enabled;
		_boostG = 0.0;
		Part fullPart = Parent.FullPart;
		Part.SymmetryLinkData? symmetryLink = fullPart.SymmetryLink;
		if (!symmetryLink.HasValue)
		{
			return;
		}
		Part.SymmetryLinkData valueOrDefault = symmetryLink.GetValueOrDefault();
		Span<AccelerationAbsorber> span = fullPart.SubtreeModules.Get<AccelerationAbsorber>();
		int num = -1;
		for (int i = 0; i < span.Length; i++)
		{
			if (span[i] == this)
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			return;
		}
		foreach (Part.SymmetryData symmetry in valueOrDefault.Symmetries)
		{
			foreach (Part part in symmetry.Parts)
			{
				Span<AccelerationAbsorber> span2 = part.SubtreeModules.Get<AccelerationAbsorber>();
				if (num < span2.Length)
				{
					span2[num].Enabled = enabled;
				}
			}
		}
	}

	private static bool RecordsHistoryFor(PartTree? tree, Brutal.Localization.LString setting, bool changed)
	{
		return (bool)(s_recordsHistoryFor.Invoke(null, new object?[] { tree, setting, changed }) ?? false);
	}

	public void DrawStateInfo(ref readonly AccelerationAbsorberState state)
	{
		Span<char> buffer = stackalloc char[128];
		ImGuiHelper.BeginColumns(2);
		bool value = Enabled;
		if (RecordsHistoryFor(Parent.FullPart.Tree, s_enabledSetting, ImGuiHelper.DrawCheckbox("Absorber Enabled"u8, ref value, isChanged: false)))
		{
			SetEnabled(value);
		}
		ImGuiHelper.EndColumns();
		SpanBuilder sb = new SpanBuilder(buffer);
		PowerReference.ToNearest(state.ConsumedWatts.Value(), ref sb);
		ImGuiHelper.DrawTextWidget("Consuming:"u8, sb);
		if (MaxPower.Value() > 0.0)
		{
			SpanBuilder capSb = new SpanBuilder(buffer);
			PowerReference.ToNearest(MaxPower.Value(), ref capSb);
			ImGuiHelper.DrawTextWidget("Power cap:"u8, capSb);
		}
		ImGuiHelper.DrawTextWidget("Absorbing:"u8, $"{state.CommandedAbsorptionG:F1} / {MaxGLoad:F1} g");
		PartTree? tree = Parent.FullPart.Tree;
		if (tree != null && ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.TryGetFrom(tree.States, out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates))
		{
			double3 rawG = typeStates.GlobalState.RawG;
			ImGuiHelper.DrawTextWidget("Raw G:"u8, $"{rawG.Length():F2} g ({rawG.X:F1}, {rawG.Y:F1}, {rawG.Z:F1})");
		}
	}

	public override SaveDataBase? GetSaveData(ref uint localRunningId, ModuleStateList states)
	{
		if (!ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.TryGetFrom(states, out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates))
		{
			return null;
		}
		return new SaveData
		{
			LocalInstanceId = localRunningId++,
			InstanceOf = TemplateId,
			Active = Enabled
		};
	}

	public override void ApplySaveData(SaveDataBase saveDataBase, ModuleStateList states)
	{
		SaveData saveData = (SaveData)saveDataBase;
		Enabled = saveData.Active;
		_needsSeed = true;
		_delayQueue.Clear();
		_pendingDeltaSum = 0.0;
		_residualG = 0.0;
		_boostG = 0.0;
		_effectiveDeliverableG = -1.0;
		if (states.TryGetTypeList(out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates))
		{
			ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList.ModuleAndAllMutableStatesRef moduleAndAllMutableStates = typeStates.GetModuleAndAllMutableStatesForInitialization(this);
			moduleAndAllMutableStates.State.CommandedAbsorptionG = 0.0;
			moduleAndAllMutableStates.State.TargetAbsorptionG = 0.0;
			moduleAndAllMutableStates.State.AbsorbedGThisStep = 0.0;
			moduleAndAllMutableStates.State.LastRawG = 0.0;
			moduleAndAllMutableStates.State.LastRawGSimSeconds = default;
			moduleAndAllMutableStates.State.Active = false;
			moduleAndAllMutableStates.State.ConsumedWatts = Watts.Zero;
		}
	}

	public override void Dispose()
	{
		_delayQueue.Clear();
		base.Dispose();
	}

	void IFlowManagerHost.OnDrawUi(IViewport inViewport, ref readonly double4x4 matrixVehicleAsmb2Ego)
	{
		OnDrawUi(inViewport, in matrixVehicleAsmb2Ego);
	}
}
