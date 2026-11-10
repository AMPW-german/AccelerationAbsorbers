using HarmonyLib;
using KSA;

namespace AccelerationAbsorbers;

[HarmonyPatch]
public static class HelloWorldPatch
{
	// CheckOnLaunch runs once per game launch, after mods are loaded.
	[HarmonyPatch(typeof(VersionInfo), nameof(VersionInfo.CheckOnLaunch))]
	[HarmonyPrefix]
	public static void CheckOnLaunchPrefix()
	{
		Console.WriteLine("Hello world from AccelerationAbsorbers!");
	}
}
