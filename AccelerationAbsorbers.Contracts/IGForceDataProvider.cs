using KSA;

namespace AccelerationAbsorbers.Contracts;

/// <summary>
/// Optional cross-mod API exposed by the AccelerationAbsorbers mod through the
/// StarMap <c>IModServiceRegistry</c>. Consumers should treat the service as
/// optional: it is only registered when AccelerationAbsorbers is loaded.
/// </summary>
public interface IGForceDataProvider
{
	/// <summary>
	/// Returns the latest committed G-force snapshot for a vehicle that has at
	/// least one acceleration absorber. Returns false for vehicles without
	/// absorbers or when no snapshot has been recorded yet.
	/// </summary>
	bool TryGetSnapshot(Vehicle vehicle, out VehicleGForceSnapshot snapshot);
}
