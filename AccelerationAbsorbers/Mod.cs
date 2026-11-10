using Brutal.ImGuiApi;
using HarmonyLib;
using StarMap.API;

namespace AccelerationAbsorbers;

[StarMapMod]
public class AccelerationAbsorbers
{
	private bool _showWelcome = true;

	[StarMapAllModsLoaded]
	public void AllModsLoaded()
	{
		new Harmony("AccelerationAbsorbers").PatchAll();
	}

	[StarMapAfterGui]
	public void AfterGui(double dt)
	{
		if (!_showWelcome)
			return;

		if (ImGui.Begin("Welcome to AccelerationAbsorbers", ref _showWelcome, ImGuiWindowFlags.NoSavedSettings))
		{
			ImGui.Text("Hello from AccelerationAbsorbers!");
			ImGui.TextDisabled("This window is drawn from [StarMapAfterGui]. Close it to dismiss.");
			ImGui.End();
		}
	}
}
