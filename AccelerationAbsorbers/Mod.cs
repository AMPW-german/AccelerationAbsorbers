using System;
using Brutal.ImGuiApi;
using HarmonyLib;
using StarMap.API;
using StarMap.API.Registries;
using StarMap.API.Services;

namespace AccelerationAbsorbers;

[StarMapMod]
public class AccelerationAbsorbers
{
	private readonly IDisposable _menuItem;

	private bool _showWelcome = true;

	private bool _showAbsorbers = true;

	public AccelerationAbsorbers(IModServiceRegistry services, IModMenuService menu)
	{
		services.RegisterService<Contracts.IGForceDataProvider>(new GForceDataProvider());
		_menuItem = menu.AddModMenuItem("Acceleration Absorbers", () => _showAbsorbers = !_showAbsorbers);
	}

	[StarMapBeforeMain]
	public void BeforeMain()
	{
		new Harmony("AccelerationAbsorbers").PatchAll();
		XmlRegistration.ApplyToStaticOverrides();
	}

	[StarMapUnload]
	public void Unload()
	{
		_menuItem.Dispose();
	}

	[StarMapAfterGui]
	public void AfterGui(double dt)
	{
		if (_showWelcome && ImGui.Begin("Welcome to AccelerationAbsorbers", ref _showWelcome, ImGuiWindowFlags.NoSavedSettings))
		{
			ImGui.Text("Acceleration absorbers negate experienced G-force inside your vehicles.");
			ImGui.TextDisabled("Attach the module via part XML, toggle it per part in the part info panel, and track vehicles in the 'Acceleration Absorbers' window (mod menu).");
			ImGui.End();
		}

		if (!_showAbsorbers)
		{
			return;
		}
		if (!ImGui.Begin("Acceleration Absorbers", ref _showAbsorbers))
		{
			ImGui.End();
			return;
		}
		var entries = AbsorberTelemetry.GetEntries();
		if (entries.Count == 0)
		{
			ImGui.TextDisabled("No vehicles with acceleration absorbers.");
		}
		foreach (var entry in entries)
		{
			ImGui.Separator();
			ImGui.Text($"{entry.VehicleId}");
			ImGui.Text($"  Raw G:      {entry.RawG.Length():F2} g  ({entry.RawG.X:F2}, {entry.RawG.Y:F2}, {entry.RawG.Z:F2})");
			ImGui.Text($"  Effective:  {entry.EffectiveG.Length():F2} g  ({entry.EffectiveG.X:F2}, {entry.EffectiveG.Y:F2}, {entry.EffectiveG.Z:F2})");
			ImGui.Text($"  Absorbed:   {entry.AbsorbedG:F2} g ({entry.AbsorbedFraction * 100.0:F0}%)   Capacity: {entry.TotalCapacityG:F1} g   Power: {entry.TotalPowerW:F0} W   Active: {entry.ActiveModuleCount}");
			foreach (var module in entry.Modules)
			{
				string state = module.Active ? "active" : (module.Enabled ? "no power" : "disabled");
				ImGui.Text($"    - {module.Label}: {module.ConsumedWatts:F0} W, {module.CommandedG:F1}/{module.MaxGLoad:F1} g, absorbed {module.AbsorbedG:F2} g [{state}]");
			}
		}
		ImGui.End();
	}
}
