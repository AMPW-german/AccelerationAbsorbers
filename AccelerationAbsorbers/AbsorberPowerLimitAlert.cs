using System;
using Core;
using KSA;

namespace AccelerationAbsorbers;

internal sealed class AbsorberPowerLimitAlert : Alert
{
	public static void DrawCentered(float cursorY, double rawG, double deliverableG, bool danger, bool powerLimited)
	{
		Span<char> buffer = stackalloc char[128];
		SpanBuilder text = new SpanBuilder(buffer);
		if (deliverableG > 1e-9)
		{
			double fraction = rawG / deliverableG;
			string warning = powerLimited
				? (danger && rawG >= deliverableG
					? "ABSORBER POWER LIMIT EXCEEDED ("
					: danger
						? "ABSORBER POWER LIMIT WARNING ("
						: "ABSORBER POWER LIMIT APPROACHING (")
				: (danger && rawG >= deliverableG
					? "ABSORBER CAPACITY EXCEEDED ("
					: danger
						? "ABSORBER CAPACITY WARNING ("
						: "ABSORBER CAPACITY APPROACHING (");
			text.Append(warning);
			text.Append(fraction * 100.0, "F0");
			text.Append((ReadOnlySpan<char>)"%) [");
		}
		else
		{
			text.Append((ReadOnlySpan<char>)(powerLimited
				? "ABSORBER POWER LIMIT EXCEEDED (NO CAPACITY) ["
				: "ABSORBER CAPACITY EXCEEDED (NO AVAILABLE CAPACITY) ["));
		}
		text.Append(rawG, "F1");
		text.Append((ReadOnlySpan<char>)" / ");
		text.Append(deliverableG, "F1");
		text.Append((ReadOnlySpan<char>)" g]");

		DrawCenteredAlertText(cursorY, text.Written, danger ? ConsoleStyle.DangerU32 : ConsoleStyle.PendingU32, 1f);
	}

	public override bool Draw(float cursorY, double dt)
	{
		return false;
	}
}
