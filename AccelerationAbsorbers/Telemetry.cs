using System.Collections.Concurrent;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;

namespace AccelerationAbsorbers;

internal struct AbsorberModuleRow
{
	public string Label;

	public bool Enabled;

	public bool Active;

	public double ConsumedWatts;

	public double CommandedG;

	public double AbsorbedG;

	public double MaxGLoad;
}

internal sealed class VehicleAbsorberTelemetry
{
	public VehicleAbsorberTelemetry(string vehicleId, double simTimeSeconds, double3 rawG, double3 effectiveG, double absorbedG, double absorbedFraction, double totalCapacityG, double totalPowerW, int activeModuleCount, AbsorberModuleRow[] modules)
	{
		VehicleId = vehicleId;
		SimTimeSeconds = simTimeSeconds;
		RawG = rawG;
		EffectiveG = effectiveG;
		AbsorbedG = absorbedG;
		AbsorbedFraction = absorbedFraction;
		TotalCapacityG = totalCapacityG;
		TotalPowerW = totalPowerW;
		ActiveModuleCount = activeModuleCount;
		Modules = modules;
	}

	public string VehicleId { get; }

	public double SimTimeSeconds { get; }

	public double3 RawG { get; }

	public double3 EffectiveG { get; }

	public double AbsorbedG { get; }

	public double AbsorbedFraction { get; }

	public double TotalCapacityG { get; }

	public double TotalPowerW { get; }

	public int ActiveModuleCount { get; }

	public AbsorberModuleRow[] Modules { get; }
}

internal static class AbsorberTelemetry
{
	private const double StaleSeconds = 30.0;

	private static readonly ConcurrentDictionary<Vehicle, VehicleAbsorberTelemetry> s_entries = new ConcurrentDictionary<Vehicle, VehicleAbsorberTelemetry>();

	public static void Publish(Vehicle vehicle, UniverseTime simTime, double3 rawG, double3 effectiveG, double absorbedG, double absorbedFraction, double totalCapacityG, double totalPowerW, int activeModuleCount, AbsorberModuleRow[] modules)
	{
		s_entries[vehicle] = new VehicleAbsorberTelemetry(vehicle.Id, simTime.Seconds(), rawG, effectiveG, absorbedG, absorbedFraction, totalCapacityG, totalPowerW, activeModuleCount, modules);
	}

	public static List<VehicleAbsorberTelemetry> GetEntries()
	{
		double maxTime = double.MinValue;
		foreach (VehicleAbsorberTelemetry entry in s_entries.Values)
		{
			if (entry.SimTimeSeconds > maxTime)
			{
				maxTime = entry.SimTimeSeconds;
			}
		}
		List<VehicleAbsorberTelemetry> result = new List<VehicleAbsorberTelemetry>(s_entries.Count);
		foreach (KeyValuePair<Vehicle, VehicleAbsorberTelemetry> pair in s_entries)
		{
			if (maxTime - pair.Value.SimTimeSeconds > StaleSeconds)
			{
				s_entries.TryRemove(pair.Key, out _);
				continue;
			}
			result.Add(pair.Value);
		}
		return result;
	}
}
