using System;
using System.Globalization;
using System.IO;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Extensions;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using StarMap.API;
using StarMap.API.Registries;
using StarMap.API.Services;

namespace AccelerationAbsorbers;

[StarMapMod]
public class AccelerationAbsorbers
{
	private static readonly string s_windowPositionPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
		"My Games",
		"Kitten Space Agency",
		"AccelerationAbsorbers.window-position");

	private readonly IDisposable _menuItem;

	private readonly float2? _savedWindowPosition;

	private bool _showAbsorbers = true;

	private Vehicle? _warningVehicle;

	private bool _isPowerWarning;

	private bool _isPowerDanger;

	private bool _windowPositionApplied;

	private float2? _lastObservedWindowPosition;

	private double _positionSaveCountdown;

	private bool _positionDirty;

	public AccelerationAbsorbers(IModServiceRegistry services, IModMenuService menu)
	{
		services.RegisterService<Contracts.IGForceDataProvider>(new GForceDataProvider());
		_menuItem = menu.AddModMenuItem("Acceleration Absorbers", () => _showAbsorbers = !_showAbsorbers);
		_savedWindowPosition = LoadWindowPosition();
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
		SaveWindowPosition();
		_menuItem.Dispose();
	}

	[StarMapAfterGui]
	public void AfterGui(double dt)
	{
		Vehicle? vehicle = Program.ControlledVehicle;
		if (vehicle == null || vehicle.IsDisposed || vehicle.Parts.Modules.Get<AccelerationAbsorber>().IsEmpty)
		{
			ResetPowerWarning(null);
			return;
		}

		if (!AbsorberTelemetry.TryGetEntry(vehicle, out VehicleAbsorberTelemetry? entry) || entry == null || entry.Modules.Length == 0)
		{
			ResetPowerWarning(vehicle);
			_isPowerWarning = false;
			_isPowerDanger = false;
			return;
		}

		ResetPowerWarning(vehicle);
		double rawG = entry.RawG.Length();
		UpdatePowerWarning(entry, rawG);
		if (_isPowerWarning || _isPowerDanger)
		{
			float cursorY = 150f + ImGui.GetTextLineHeightWithSpacing() * 2.5f;
			AbsorberPowerLimitAlert.DrawCentered(
				cursorY,
				rawG,
				entry.TotalDeliverableG,
				_isPowerDanger,
				HasPowerLimitedModule(entry));
		}

		if (!_showAbsorbers)
		{
			return;
		}

		if (!_windowPositionApplied && _savedWindowPosition.HasValue)
		{
			ImGui.SetNextWindowPos(_savedWindowPosition.Value, ImGuiCond.Always);
		}
		bool windowVisible = ConsoleStyle.BeginWindow(
			"AccelerationAbsorbers",
			"ACCELERATION ABSORBERS",
			"KSA-AA",
			ref _showAbsorbers,
			new float2(720f, 900f) * ImGuiHelper.InterfaceScale);
		_windowPositionApplied = true;
		if (!windowVisible)
		{
			return;
		}

		TrackWindowPosition(ImGui.GetWindowPos(), dt);
		ConsoleStyle.BeginBody();
		ConsoleStyle.PushWidgetStyle();
		DrawDashboard(vehicle, entry);
		ConsoleStyle.PopWidgetStyle();
		ConsoleStyle.EndBody();
		ConsoleStyle.EndWindow();
		if (!_showAbsorbers)
		{
			SaveWindowPosition();
		}
	}

	private void DrawDashboard(Vehicle vehicle, VehicleAbsorberTelemetry entry)
	{
		double rawG = entry.RawG.Length();
		double experiencedG = entry.EffectiveG.Length();
		double gScale = Math.Max(1.0, rawG);

		ConsoleWidgets.RegionHeader("VEHICLE LOAD");
		ConsoleWidgets.Readout("CONTROLLED VEHICLE", vehicle.Id);
		ConsoleWidgets.RegionHeader("G-FORCE");
		DrawGBar("Raw G", rawG, gScale);
		ConsoleWidgets.Readout("RAW AXES", $"X {entry.RawG.X:F2}, Y {entry.RawG.Y:F2}, Z {entry.RawG.Z:F2}");
		DrawGBar("Experienced G", experiencedG, gScale);
		DrawPowerWarning(entry, rawG);

		ConsoleWidgets.RegionHeader("ABSORPTION CAPACITY");
		ConsoleWidgets.Readout("ABSORBED / RATED", $"{entry.AbsorbedG:F2} / {entry.TotalCapacityG:F1} g");
		DrawMeter(entry.AbsorbedG, entry.TotalCapacityG, _isPowerWarning, _isPowerDanger);
		ConsoleWidgets.Readout("DELIVERABLE", $"{entry.TotalDeliverableG:F1} g");
		DrawPowerSummary(entry);

		ConsoleWidgets.RegionHeader($"ABSORBERS - {entry.Modules.Length} INSTALLED");
		ImGui.BeginChild("##AbsorberModuleList", new float2(0f, 0f), ImGuiChildFlags.Borders);
		for (int i = 0; i < entry.Modules.Length; i++)
		{
			AbsorberModuleRow row = entry.Modules[i];
			AccelerationAbsorber module = row.Module;
			ImGui.PushID(i);
			ConsoleWidgets.BeginRow(row.Label);
			bool enabled = module.Enabled;
			if (ConsoleWidgets.Checkbox("##Enabled", ref enabled, pending: false))
			{
				module.SetEnabledFromManagementUi(enabled);
			}
			ConsoleWidgets.EndRow();

			string state = !module.Enabled ? "Disabled" : (row.Active ? "Active" : "No power");
			ConsoleWidgets.Readout(row.PowerLimited && module.Enabled ? "STATUS - POWER LIMITED" : "STATUS", state);
			ConsoleWidgets.Readout("ABSORBED / RATED", $"{row.AbsorbedG:F2} / {row.MaxGLoad:F1} g");
			DrawMeter(row.AbsorbedG, row.MaxGLoad, row.PowerLimited && module.Enabled);
			if (row.MaxPowerW > 0.0)
			{
				ConsoleWidgets.Readout("ELECTRICITY", $"{row.ConsumedWatts:F0} / {row.MaxPowerW:F0} W");
				double powerFraction = Fraction(row.ConsumedWatts, row.MaxPowerW);
				DrawMeter(powerFraction, 1.0, powerFraction >= 0.85, powerFraction >= 0.95);
			}
			else
			{
				ConsoleWidgets.Readout("ELECTRICITY", $"{row.ConsumedWatts:F0} W / uncapped");
			}
			ConsoleWidgets.Rule();
			ImGui.PopID();
		}
		ImGui.EndChild();
	}

	private static void DrawGBar(string label, double value, double scale)
	{
		ConsoleWidgets.Readout(label, $"{value:F2} g");
		DrawMeter(value, scale);
	}

	private static void DrawMeter(double value, double capacity, bool pending = false, bool danger = false)
	{
		ConsoleStyle.EnsureInitialized();
		float width = ConsoleStyle.ContentAvailWidth();
		float height = MathF.Max(8f, ConsoleStyle.FrameHeightPx * 0.5f);
		float fraction = Fraction(value, capacity);
		float2 min = ImGui.GetCursorScreenPos();
		float2 max = min + new float2(width, height);
		var drawList = ImGui.GetWindowDrawList();

		drawList.AddRectFilled(in min, in max, ConsoleStyle.ApplyAlpha(ConsoleStyle.InsetU32), ConsoleStyle.FrameRoundingPx);
		float fillWidth = width * fraction;
		if (fillWidth > 0f)
		{
			uint fillColor = danger ? ConsoleStyle.DangerU32 : (pending ? ConsoleStyle.PendingU32 : ConsoleStyle.ActiveToolU32);
			float2 fillMax = new float2(min.X + fillWidth, max.Y);
			drawList.AddRectFilled(in min, in fillMax, ConsoleStyle.ApplyAlpha(fillColor), ConsoleStyle.FrameRoundingPx, ImDrawFlags.RoundCornersLeft);
		}
		drawList.AddRect(in min, in max, ConsoleStyle.ApplyAlpha(ConsoleStyle.FrameU32), ConsoleStyle.FrameRoundingPx, ImDrawFlags.None, ConsoleStyle.WindowBorderThicknessPx);
		drawList.AddLine(
			new float2(min.X + ConsoleStyle.FrameRoundingPx, min.Y + ConsoleStyle.BezelInsetPx),
			new float2(max.X - ConsoleStyle.FrameRoundingPx, min.Y + ConsoleStyle.BezelInsetPx),
			ConsoleStyle.ApplyAlpha(ConsoleStyle.TopHighlightU32),
			ConsoleStyle.WindowBorderThicknessPx);
		ImGui.Dummy(new float2(width, height));
	}

	private static void DrawPowerSummary(VehicleAbsorberTelemetry entry)
	{
		double maxPowerW = 0.0;
		bool uncapped = false;
		foreach (AbsorberModuleRow module in entry.Modules)
		{
			if (module.MaxPowerW <= 0.0)
			{
				uncapped = true;
				break;
			}
			maxPowerW += module.MaxPowerW;
		}

		if (uncapped)
		{
			ConsoleWidgets.Readout("ELECTRICITY", $"{entry.TotalPowerW:F0} W / uncapped");
		}
		else
		{
			ConsoleWidgets.Readout("ELECTRICITY", $"{entry.TotalPowerW:F0} / {maxPowerW:F0} W");
			DrawMeter(entry.TotalPowerW, maxPowerW);
		}
	}

	private void DrawPowerWarning(VehicleAbsorberTelemetry entry, double rawG)
	{
		double deliverableG = entry.TotalDeliverableG;
		bool powerLimited = HasPowerLimitedModule(entry);
		if (_isPowerDanger)
		{
			string warning = powerLimited
				? (rawG >= deliverableG ? "POWER LIMIT EXCEEDED" : "POWER LIMIT WARNING")
				: (rawG >= deliverableG ? "ABSORBER CAPACITY EXCEEDED" : "ABSORBER CAPACITY WARNING");
			ImGui.PushStyleColor(ImGuiCol.Text, in ConsoleStyle.Danger);
			ImGui.Text($"{warning} - {rawG:F2} g raw vs {deliverableG:F1} g deliverable");
			ImGui.PopStyleColor();
		}
		else if (_isPowerWarning)
		{
			ImGui.PushStyleColor(ImGuiCol.Text, in ConsoleStyle.Pending);
			string warning = powerLimited ? "Absorber power limit approaching" : "Absorber capacity approaching";
			ImGui.Text($"{warning} - {rawG:F2} / {deliverableG:F1} g");
			ImGui.PopStyleColor();
		}
	}

	private void UpdatePowerWarning(VehicleAbsorberTelemetry entry, double rawG)
	{
		if (entry.ActiveModuleCount == 0)
		{
			_isPowerWarning = false;
			_isPowerDanger = false;
			return;
		}

		double deliverableG = entry.TotalDeliverableG;
		double fraction = deliverableG > 1e-9
			? rawG / deliverableG
			: (rawG > 1e-9 ? double.PositiveInfinity : 0.0);
		_isPowerWarning = fraction >= (_isPowerWarning ? 0.65 : 0.70);
		_isPowerDanger = fraction >= (_isPowerDanger ? 0.80 : 0.85);
	}

	private static bool HasPowerLimitedModule(VehicleAbsorberTelemetry entry)
	{
		foreach (AbsorberModuleRow module in entry.Modules)
		{
			if (module.Enabled && module.PowerLimited)
			{
				return true;
			}
		}
		return false;
	}

	private void TrackWindowPosition(float2 position, double dt)
	{
		if (!_lastObservedWindowPosition.HasValue ||
			_lastObservedWindowPosition.Value.X != position.X ||
			_lastObservedWindowPosition.Value.Y != position.Y)
		{
			_lastObservedWindowPosition = position;
			_positionDirty = true;
			_positionSaveCountdown = 0.5;
		}
		else if (_positionDirty)
		{
			_positionSaveCountdown -= Math.Max(0.0, dt);
		}

		if (_positionDirty && _positionSaveCountdown <= 0.0)
		{
			SaveWindowPosition();
		}
	}

	private void SaveWindowPosition()
	{
		if (!_positionDirty || !_lastObservedWindowPosition.HasValue)
		{
			return;
		}

		try
		{
			string? directory = Path.GetDirectoryName(s_windowPositionPath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			float2 position = _lastObservedWindowPosition.Value;
			File.WriteAllText(s_windowPositionPath, string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R}", position.X, position.Y));
			_positionDirty = false;
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static float2? LoadWindowPosition()
	{
		try
		{
			string[] values = File.ReadAllText(s_windowPositionPath).Split(',');
			if (values.Length == 2 &&
				float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
				float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
				float.IsFinite(x) &&
				float.IsFinite(y))
			{
				return new float2(x, y);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		return null;
	}

	private void ResetPowerWarning(Vehicle? vehicle)
	{
		if (_warningVehicle == vehicle)
		{
			return;
		}
		_warningVehicle = vehicle;
		_isPowerWarning = false;
		_isPowerDanger = false;
	}

	private static float Fraction(double value, double capacity)
	{
		if (!(capacity > 0.0) || !double.IsFinite(value))
		{
			return 0f;
		}
		return (float)Math.Clamp(value / capacity, 0.0, 1.0);
	}
}
