using System;
using System.Runtime.CompilerServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Core;
using HarmonyLib;
using KSA;

namespace AccelerationAbsorbers;

[HarmonyPatch]
public static class ModuleCreationPatch
{
	[HarmonyPatch(typeof(ModuleList), nameof(ModuleList.CreateModules))]
	[HarmonyPostfix]
	public static void CreateModulesPostfix(Part part, PartTemplate template, TransformDataReference<SubPartTemplate>? instance, Part? parent)
	{
		AccelerationAbsorber.CreateComponents(part, template, instance);
	}
}

[HarmonyPatch]
public static class VehicleModuleUpdatePatch
{
	[HarmonyPatch(typeof(VehicleUpdateState), nameof(VehicleUpdateState.UpdateModules))]
	[HarmonyPostfix]
	public static void UpdateModulesPostfix(VehicleUpdateState __instance, SimStep step)
	{
		ModuleUpdateContext context = new ModuleUpdateContext
		{
			Step = step,
			States = __instance.GetReadOnlyStates(),
			Modules = __instance.ReadOnlyVehicle.Parts.Modules,
			Updaters = __instance.UpdateData.Updaters,
			Ctrl2Body = __instance.FlightComputer.Ctrl2Asmb
		};
		AccelerationAbsorber.UpdateModules(in context);
	}
}

[HarmonyPatch]
public static class MeasurementPatch
{
	[HarmonyPatch(typeof(PhysicsBubble), "IngestStepKinematicMeasurements")]
	[HarmonyPrefix]
	public static void IngestStepKinematicMeasurementsPrefix(VehicleUpdateState vehicleState, ref KinematicMeasurements stepKinematicMeasurements)
	{
		AbsorberMeasurement.Apply(vehicleState, ref stepKinematicMeasurements, vehicleState.Origin.Time);
	}

	// ApplySingleSurfaceMotion is the only caller of the static ApplySurfaceMotion that
	// bypasses IngestStepKinematicMeasurements (the analytic Landed/Floating/Bottomed
	// path). Patching it - rather than ApplySurfaceMotion itself - avoids scaling the
	// same measurement twice on the full-physics unconstrained path, where
	// ApplySurfaceMotion output flows into ingest.
	[HarmonyPatch(typeof(PhysicsBubble), "ApplySingleSurfaceMotion")]
	[HarmonyPostfix]
	public static void ApplySingleSurfaceMotionPostfix(UniverseTime nextTime, VehicleUpdateState vehicleState, ref KinematicMeasurements kinematicMeasurements)
	{
		AbsorberMeasurement.Apply(vehicleState, ref kinematicMeasurements, nextTime);
	}
}

[HarmonyPatch]
public static class ThrottleCapPatch
{
	private const float GLoadSafetyFactor = 0.9f;

	[HarmonyPatch(typeof(FlightComputer), "SolveGLoadThrottleCap")]
	[HarmonyPrefix]
	public static void SolveGLoadThrottleCapPrefix(FlightComputer __instance, out float __state)
	{
		__state = __instance.MaxGLoad;
		if (ThrottleBonusG.TryGet(__instance, out double bonusG) && bonusG > 0.0)
		{
			__instance.MaxGLoad += (float)(bonusG / GLoadSafetyFactor);
		}
	}

	[HarmonyPatch(typeof(FlightComputer), "SolveGLoadThrottleCap")]
	[HarmonyPostfix]
	public static void SolveGLoadThrottleCapPostfix(FlightComputer __instance, float __state)
	{
		__instance.MaxGLoad = __state;
	}
}

[HarmonyPatch]
public static class PartInfoPatch
{
	[HarmonyPatch(typeof(Part), nameof(Part.DrawPartInfo))]
	[HarmonyPostfix]
	public static void DrawPartInfoPostfix(Part __instance)
	{
		PartTree? tree = __instance.Tree;
		if (tree == null || !ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.TryGetFrom(tree.States, out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates))
		{
			return;
		}
		Span<AccelerationAbsorber> absorbers = __instance.SubtreeModules.Get<AccelerationAbsorber>();
		if (absorbers.IsEmpty)
		{
			return;
		}
		ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAllColumns;
		Span<char> buffer = stackalloc char[128];
		for (int i = 0; i < absorbers.Length; i++)
		{
			AccelerationAbsorber module = absorbers[i];
			SpanBuilder header = new SpanBuilder(buffer);
			header.Append((ReadOnlySpan<char>)"Absorber '");
			header.Append((ReadOnlySpan<char>)module.TemplateId);
			header.Append((ReadOnlySpan<char>)"'###");
			header.Append(module.InstanceId);
			if (ImGui.CollapsingHeader(header, flags))
			{
				AccelerationAbsorberState state = typeStates.States[module.StatesIdx];
				module.DrawStateInfo(in state);
			}
		}
	}
}

internal static class ThrottleBonusG
{
	private sealed class BonusHolder
	{
		public double Value;
	}

	private static readonly ConditionalWeakTable<FlightComputer, BonusHolder> s_bonusG = new ConditionalWeakTable<FlightComputer, BonusHolder>();

	public static void Set(FlightComputer flightComputer, double bonusG)
	{
		s_bonusG.GetValue(flightComputer, static _ => new BonusHolder()).Value = bonusG;
	}

	public static bool TryGet(FlightComputer flightComputer, out double bonusG)
	{
		if (s_bonusG.TryGetValue(flightComputer, out BonusHolder? holder))
		{
			bonusG = holder.Value;
			return true;
		}
		bonusG = 0.0;
		return false;
	}
}

internal static class AbsorberMeasurement
{
	private const double StandardGravity = 9.80665;

	public static void Apply(VehicleUpdateState vehicleState, ref KinematicMeasurements measurements, UniverseTime now)
	{
		ModuleStateUpdaters updaters = vehicleState.UpdateData.Updaters;
		if (!updaters.TryGetTypeStateUpdater<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>(out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateUpdater updater))
		{
			return;
		}
		ReadOnlySpan<AccelerationAbsorber> modules = updater.Modules;
		int count = modules.Length;
		if (count == 0)
		{
			return;
		}
		Span<AccelerationAbsorberState> states = updater.GetNewStates();
		double3 rawAccel = measurements.AccelerationBody;
		double rawG = rawAccel.Length() / StandardGravity;
		double sumEffective = 0.0;
		for (int i = 0; i < count; i++)
		{
			AccelerationAbsorberState state = states[modules[i].StatesIdx];
			if (state.Active)
			{
				sumEffective += state.CommandedAbsorptionG;
			}
		}
		double absorbed = Math.Min(sumEffective, rawG);
		double fraction = rawG > 1e-9 ? absorbed / rawG : 0.0;
		if (fraction > 0.0)
		{
			measurements.AccelerationBody = rawAccel * (1.0 - fraction);
		}
		AbsorberModuleRow[] rows = new AbsorberModuleRow[count];
		double capacityG = 0.0;
		double powerW = 0.0;
		int activeCount = 0;
		for (int i = 0; i < count; i++)
		{
			ref AccelerationAbsorberState state = ref states[modules[i].StatesIdx];
			AccelerationAbsorber module = modules[i];
			double effective = state.Active ? state.CommandedAbsorptionG : 0.0;
			state.LastRawG = rawG;
			state.LastRawGSimSeconds = now;
			state.AbsorbedGThisStep = sumEffective > 0.0 ? absorbed * effective / sumEffective : 0.0;
			capacityG += module.MaxGLoad;
			powerW += state.ConsumedWatts.Value();
			if (state.Active)
			{
				activeCount++;
			}
			rows[i] = new AbsorberModuleRow
			{
				Label = module.Parent.DisplayName,
				Enabled = module.Enabled,
				Active = state.Active,
				ConsumedWatts = state.ConsumedWatts.Value(),
				CommandedG = state.CommandedAbsorptionG,
				AbsorbedG = state.AbsorbedGThisStep,
				MaxGLoad = module.MaxGLoad
			};
		}
		ref AccelerationAbsorberGlobalState globalState = ref updater.GlobalState;
		globalState.RawG = rawAccel * (1.0 / StandardGravity);
		globalState.EffectiveG = measurements.AccelerationBody * (1.0 / StandardGravity);
		globalState.AbsorbedG = absorbed;
		globalState.AbsorbedFraction = fraction;
		globalState.LastIngestSimSeconds = now;
		ThrottleBonusG.Set(vehicleState.FlightComputer, capacityG);
		AbsorberTelemetry.Publish(vehicleState.ReadOnlyVehicle, now, globalState.RawG, globalState.EffectiveG, absorbed, fraction, capacityG, powerW, activeCount, rows);
	}
}
