using Brutal.Numerics;
using KSA;

namespace AccelerationAbsorbers.Contracts;

/// <summary>
/// Immutable per-vehicle snapshot of acceleration-absorber state, as seen through
/// the committed module state on the main thread.
/// </summary>
public readonly struct VehicleGForceSnapshot
{
	public VehicleGForceSnapshot(string vehicleId, double3 rawG, double3 effectiveG, double absorbedFraction, double totalCapacityG, double totalPowerW, int activeModuleCount)
	{
		VehicleId = vehicleId;
		RawG = rawG;
		EffectiveG = effectiveG;
		AbsorbedFraction = absorbedFraction;
		TotalCapacityG = totalCapacityG;
		TotalPowerW = totalPowerW;
		ActiveModuleCount = activeModuleCount;
	}

	/// <summary>Vehicle <see cref="Astronomical.Id"/> (also its display name).</summary>
	public string VehicleId { get; }

	/// <summary>Measured specific-force acceleration before absorption, in g, body frame.</summary>
	public double3 RawG { get; }

	/// <summary>Specific-force acceleration after absorption, in g, body frame.</summary>
	public double3 EffectiveG { get; }

	/// <summary>Fraction of raw G magnitude that was absorbed (0..1).</summary>
	public double AbsorbedFraction { get; }

	/// <summary>Sum of installed absorber capacity over all modules on the vehicle, in g.</summary>
	public double TotalCapacityG { get; }

	/// <summary>Total electrical draw of all active absorber modules, in watts.</summary>
	public double TotalPowerW { get; }

	/// <summary>Number of absorber modules currently active (enabled and powered).</summary>
	public int ActiveModuleCount { get; }
}
