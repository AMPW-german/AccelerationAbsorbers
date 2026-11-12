using System;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using KSA;

namespace AccelerationAbsorbers;

/// <summary>
/// Registers the absorber Template/SaveData types with KSA's XML serializers.
/// KSA's XmlHelper.BuildAttributeOverrides only scans KSA.dll, so mod component
/// types must be added to every XmlAttributeOverrides instance by hand:
/// the static XmlHelper.AttributeOverrides (save-game path) and the instance
/// inside the ContentTagTable built by GameContentTags.Build (content path).
/// </summary>
[HarmonyPatch]
public static class XmlRegistration
{
	[HarmonyPatch(typeof(XmlHelper), nameof(XmlHelper.BuildAttributeOverrides))]
	[HarmonyPostfix]
	public static void BuildAttributeOverridesPostfix(XmlAttributeOverrides __result)
	{
		Apply(__result);
	}

	/// <summary>
	/// Applies the registrations to the already-built static overrides instance.
	/// Called from [StarMapBeforeMain] after PatchAll so the postfix above is
	/// active before this runs - touching the field forces the static
	/// initializer, which produces an instance that the postfix has already
	/// populated; Apply is idempotent either way.
	/// </summary>
	public static void ApplyToStaticOverrides()
	{
		Apply(XmlHelper.AttributeOverrides);
	}

	public static void Apply(XmlAttributeOverrides overrides)
	{
		ApplyTemplate(overrides);
		ApplySaveData(overrides);
	}

	private static void ApplyTemplate(XmlAttributeOverrides overrides)
	{
		const string member = "Components";
		XmlAttributes? attributes = overrides[typeof(PartTemplate), member];
		if (attributes == null)
		{
			attributes = new XmlAttributes
			{
				XmlArray = new XmlArrayAttribute("Components")
			};
			overrides.Add(typeof(PartTemplate), member, attributes);
		}
		foreach (XmlArrayItemAttribute item in attributes.XmlArrayItems)
		{
			if (item.Type == typeof(AccelerationAbsorber.Template))
			{
				return;
			}
		}
		attributes.XmlArrayItems.Add(new XmlArrayItemAttribute("AccelerationAbsorber", typeof(AccelerationAbsorber.Template)));
	}

	private static void ApplySaveData(XmlAttributeOverrides overrides)
	{
		const string member = "Components";
		XmlAttributes? attributes = overrides[typeof(PartInstance), member];
		if (attributes == null)
		{
			attributes = new XmlAttributes();
			overrides.Add(typeof(PartInstance), member, attributes);
		}
		foreach (XmlElementAttribute element in attributes.XmlElements)
		{
			if (element.Type == typeof(AccelerationAbsorber.SaveData))
			{
				return;
			}
		}
		attributes.XmlElements.Add(new XmlElementAttribute("AccelerationAbsorberData", typeof(AccelerationAbsorber.SaveData)));
	}
}

/// <summary>
/// Postfix on the internal GameContentTags.Build so the absorber types are
/// added to the ContentTagTable's overrides - the same instance
/// ContentSerializerCache uses for content deserialization. This mirrors the
/// approach used by KittenExtensions/ShaderExtensions. It must not run during
/// mod init: KittenExtensions and ShaderExtensions populate their asset
/// registries in a Program.Main prefix, so the tag table has to be built
/// after that - which is exactly when GameContentTags.Build runs.
/// </summary>
[HarmonyPatch]
public static class ContentTagTablePatch
{
	private static readonly MethodInfo s_buildMethod = typeof(ModLibrary).Assembly
		.GetType("KSA.GameContentTags", throwOnError: true)!
		.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;

	[HarmonyTargetMethod]
	private static MethodBase TargetMethod()
	{
		return s_buildMethod;
	}

	[HarmonyPostfix]
	private static void Postfix(ContentTagTable __result)
	{
		XmlRegistration.Apply(__result.Overrides);
	}
}
