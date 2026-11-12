using System;
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

	public double AbsorbedGThisStep;

	public double LastRawG;

	public UniverseTime LastRawGSimSeconds;
}

public struct AccelerationAbsorberGlobalState
{
	public double TotalCapacityG;

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

	private const int MaxDelayQueueSize = 1024;

	private const double RawGStaleSeconds = 2.0;

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

	public double MaxGLoad;

	public double MinDelayMs;

	public double MaxDelayMs;

	public bool Enabled = true;

	public required PowerManager PowerManager { get; set; }

	private readonly List<DelayEvent> _delayQueue = new List<DelayEvent>(256);

	private double _commandedG;

	private double _lastClampedG;

	private double _residualG;

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
					MaxGLoad = template2.MaxGLoad,
					MinDelayMs = template2.MinDelayMs,
					MaxDelayMs = template2.MaxDelayMs,
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
		globalState.TotalCommandedG = 0.0;
		globalState.TotalPowerW = 0.0;
		globalState.ActiveModuleCount = 0;
		ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater.ModuleAndNewStateEnumerator enumerator = typeUpdater.GetModulesAndNewStates().GetEnumerator();
		while (enumerator.MoveNext())
		{
			ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater.ModuleAndNewStateRef current = enumerator.Current;
			AccelerationAbsorber module = current.Module;
			double rawG = 0.0;
			if ((context.Step.NextTime - current.State.LastRawGSimSeconds).Seconds() < RawGStaleSeconds)
			{
				rawG = current.State.LastRawG;
			}
			double commanded = module.TickDelayQueue(context.Step, rawG);
			current.State.AbsorbedGThisStep = 0.0;
			current.State.CommandedAbsorptionG = 0.0;
			if (module.Enabled && module.PowerManager.ResourceAvailable(updater.GetReadOnlyStates()))
			{
				double absorptionWattsRequired = massTonnes * module.PowerPerGLoad.Value() * commanded * module.PowerPerTonne.Value();
				double overheadWatts = module.BasePower.Value();
				double requiredWatts = overheadWatts + absorptionWattsRequired;
				Joules joulesChange = new Watts((float)requiredWatts).Integrate(context.Step.DeltaTime);
				module.PowerManager.Consume(updater.GetNewStates(), joulesChange, out var amountChanged);
				double actualWatts = amountChanged.Differentiate(context.Step.DeltaTime).Value();
				current.State.ConsumedWatts = new Watts((float)actualWatts);
				double absorptionBudget = Math.Max(0.0, actualWatts - overheadWatts);
				double powerScale = absorptionWattsRequired > 1e-9 ? Math.Clamp(absorptionBudget / absorptionWattsRequired, 0.0, 1.0) : 1.0;
				current.State.CommandedAbsorptionG = commanded * powerScale;
				current.State.Active = amountChanged > Joules.Zero;
			}
			else
			{
				current.State.ConsumedWatts = Watts.Zero;
				current.State.Active = false;
			}
			globalState.TotalCapacityG += module.MaxGLoad;
			globalState.TotalCommandedG += current.State.CommandedAbsorptionG;
			globalState.TotalPowerW += current.State.ConsumedWatts.Value();
			if (current.State.Active)
			{
				globalState.ActiveModuleCount++;
			}
		}
	}

	private double TickDelayQueue(SimStep step, double rawG)
	{
		if (_needsSeed)
		{
			_needsSeed = false;
			double clampedSeed = Math.Min(rawG, MaxGLoad);
			_commandedG = clampedSeed;
			_lastClampedG = clampedSeed;
			_residualG = 0.0;
			_delayQueue.Clear();
			return _commandedG;
		}
		double clampedG = Math.Min(rawG, MaxGLoad);
		_residualG += clampedG - _lastClampedG;
		_lastClampedG = clampedG;
		if (Math.Abs(_residualG) > ResidualEpsilonG)
		{
			double delaySeconds = (MinDelayMs + Random.Shared.NextDouble() * Math.Max(0.0, MaxDelayMs - MinDelayMs)) * 0.001;
			Enqueue(step.NextTime + delaySeconds, _residualG);
			_residualG = 0.0;
		}
		FlushDue(step.NextTime);
		if (_delayQueue.Count >= MaxDelayQueueSize)
		{
			_commandedG = clampedG;
			_delayQueue.Clear();
		}
		_commandedG = Math.Clamp(_commandedG, 0.0, MaxGLoad);
		return _commandedG;
	}

	private void Enqueue(UniverseTime ready, double deltaG)
	{
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
		}
		_delayQueue.RemoveRange(0, due);
	}

	public void SetEnabled(bool enabled)
	{
		Enabled = enabled;
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
		if (states.TryGetTypeList(out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates))
		{
			ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList.ModuleAndAllMutableStatesRef moduleAndAllMutableStates = typeStates.GetModuleAndAllMutableStatesForInitialization(this);
			moduleAndAllMutableStates.State.CommandedAbsorptionG = 0.0;
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
