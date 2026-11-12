using AccelerationAbsorbers.Contracts;
using KSA;

namespace AccelerationAbsorbers;

/// <summary>
/// <see cref="IGForceDataProvider"/> implementation reading the committed
/// per-vehicle module state. Safe to call from the main thread.
/// </summary>
public sealed class GForceDataProvider : IGForceDataProvider
{
	public bool TryGetSnapshot(Vehicle vehicle, out VehicleGForceSnapshot snapshot)
	{
		snapshot = default;
		PartTree? tree = vehicle.Parts;
		if (tree == null || !ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.TryGetFrom(tree.States, out ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>.StateList typeStates) || typeStates.NumModules == 0)
		{
			return false;
		}
		ref readonly AccelerationAbsorberGlobalState global = ref typeStates.GlobalState;
		if (global.LastIngestSimSeconds == default(UniverseTime))
		{
			return false;
		}
		snapshot = new VehicleGForceSnapshot(vehicle.Id, global.RawG, global.EffectiveG, global.AbsorbedFraction, global.TotalCapacityG, global.TotalPowerW, global.ActiveModuleCount);
		return true;
	}
}
