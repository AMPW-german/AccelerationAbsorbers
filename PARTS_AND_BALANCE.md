# Acceleration absorber parts and balance

This document records a proposed first parts lineup and its starting balance values. The values are intended for in-game testing and tuning, not treated as final KSA balance.

## Design choices

- Create two radially attached parts and four inline parts.
- Use 1 m, 2 m, 3 m, and 4 m inline profiles. Confirm the attachment profiles against the current KSA content before modeling.
- Offer the same four Max G ratings on every part: **4 G, 8 G, 12 G, and 16 G**.
- Distinguish the rating variants by color: **Green 4 G**, **Cyan 8 G**, **Amber 12 G**, and **Violet 16 G**.
- This makes 24 combinations in total: 6 form factors × 4 ratings.
- All variants are available immediately. There are no technology or research gates for the initial release.
- Set `MinDelayMs="10"` and `MaxDelayMs="30"` on every variant.
- Lower-rated variants have lower base draw and a lower `PowerPerGLoad` value. Higher-rated variants trade higher draw for a higher G ceiling.
- Larger units have a mildly lower effective power coefficient. The intent is for improved equipment to offset some of the larger field, not to make size alone a strong efficiency upgrade.
- Increase `MaxPower` with rating. Each form factor's Green 4 G value is the baseline cap; Cyan, Amber, and Violet scale it to 1.33×, 1.67×, and 2× respectively.
- Use a nominal material density of **5,000 kg/m³** for both inline and radial parts. Estimate the inline units as solid cylinders; estimate each radial unit from its overall cylindrical envelope, ignoring internal voids and small concave details.
- Size the Compact Radial unit at 0.5 m diameter × 0.4 m depth and the Heavy Radial unit at 0.9 m diameter × 0.55 m depth. These use standard 0.5 and 1.0 radial attachment profiles respectively.
- Offer the Heavy Radial in caged and cage-free cosmetic variants for each rating. KSA variants inherit gameplay data, so both looks share the same rating and mass configuration.
- Keep both radial designs symmetric across X=0 and Y=0; fit the pressure cylinders within the grey mounting-plate footprint.

## Power model and rating values

The module's requested draw is calculated from vehicle mass and commanded absorption:

```text
requested watts = BasePower + mass in tonnes × commanded G × k
k = PowerPerGLoad × PowerPerTonne
```

`MaxPower` caps the draw. When the cap is reached, the module delivers less G than its rating. For the values below, set `PowerPerTonne` to `1` and put the listed `k` value in `PowerPerGLoad`.

Base draw and `k` use these multipliers relative to each form factor's 12 G reference values. `MaxPower` uses the multiplier in its own column relative to the Green 4 G cap:

| Rating | Color | BasePower multiplier | `k` multiplier | `MaxPower` multiplier |
|---:|---|---:|---:|---:|
| 4 G | Green | 0.50× | 0.65× | 1.00× |
| 8 G | Cyan | 0.75× | 0.85× | 1.33× |
| 12 G | Amber | 1.00× | 1.00× | 1.67× |
| 16 G | Violet | 1.25× | 1.15× | 2.00× |

## Variant table

All power values are per module. The `PowerPerTonne` setting is `1` for every row; `k` is therefore the corresponding `PowerPerGLoad` value.

| Part | Mount / diameter | Rating | `MaxGLoad` | `BasePower` | `PowerPerGLoad` (`k`) | `MaxPower` | Delay |
|---|---|---|---:|---:|---:|---:|---:|
| Compact Radial | Radial | Green | 4 G | 50 W | 1.30 | 2.5 kW | 10–30 ms |
| Compact Radial | Radial | Cyan | 8 G | 75 W | 1.70 | 3.325 kW | 10–30 ms |
| Compact Radial | Radial | Amber | 12 G | 100 W | 2.00 | 4.175 kW | 10–30 ms |
| Compact Radial | Radial | Violet | 16 G | 125 W | 2.30 | 5 kW | 10–30 ms |
| Heavy Radial | Radial | Green | 4 G | 200 W | 1.235 | 15 kW | 10–30 ms |
| Heavy Radial | Radial | Cyan | 8 G | 300 W | 1.615 | 19.95 kW | 10–30 ms |
| Heavy Radial | Radial | Amber | 12 G | 400 W | 1.90 | 25.05 kW | 10–30 ms |
| Heavy Radial | Radial | Violet | 16 G | 500 W | 2.185 | 30 kW | 10–30 ms |
| 1 m Inline | Inline / 1 m | Green | 4 G | 125 W | 1.30 | 5 kW | 10–30 ms |
| 1 m Inline | Inline / 1 m | Cyan | 8 G | 187.5 W | 1.70 | 6.65 kW | 10–30 ms |
| 1 m Inline | Inline / 1 m | Amber | 12 G | 250 W | 2.00 | 8.35 kW | 10–30 ms |
| 1 m Inline | Inline / 1 m | Violet | 16 G | 312.5 W | 2.30 | 10 kW | 10–30 ms |
| 2 m Inline | Inline / 2 m | Green | 4 G | 375 W | 1.235 | 25 kW | 10–30 ms |
| 2 m Inline | Inline / 2 m | Cyan | 8 G | 562.5 W | 1.615 | 33.25 kW | 10–30 ms |
| 2 m Inline | Inline / 2 m | Amber | 12 G | 750 W | 1.90 | 41.75 kW | 10–30 ms |
| 2 m Inline | Inline / 2 m | Violet | 16 G | 937.5 W | 2.185 | 50 kW | 10–30 ms |
| 3 m Inline | Inline / 3 m | Green | 4 G | 1 kW | 1.17 | 80 kW | 10–30 ms |
| 3 m Inline | Inline / 3 m | Cyan | 8 G | 1.5 kW | 1.53 | 106.4 kW | 10–30 ms |
| 3 m Inline | Inline / 3 m | Amber | 12 G | 2 kW | 1.80 | 133.6 kW | 10–30 ms |
| 3 m Inline | Inline / 3 m | Violet | 16 G | 2.5 kW | 2.07 | 160 kW | 10–30 ms |
| 4 m Inline | Inline / 4 m | Green | 4 G | 2.5 kW | 1.105 | 250 kW | 10–30 ms |
| 4 m Inline | Inline / 4 m | Cyan | 8 G | 3.75 kW | 1.445 | 332.5 kW | 10–30 ms |
| 4 m Inline | Inline / 4 m | Amber | 12 G | 5 kW | 1.70 | 417.5 kW | 10–30 ms |
| 4 m Inline | Inline / 4 m | Violet | 16 G | 6.25 kW | 1.955 | 500 kW | 10–30 ms |

## Part mass estimates and self-mass share

Use a solid cylinder with radius = diameter / 2 and length = diameter / 2 for the inline parts. For radial parts, use the overall design envelope dimensions listed here:

```text
mass = density × π × radius² × length
density = 5,000 kg/m³
```

The percentages below show the module's own mass as a share of the maximum total vehicle mass one module can absorb at its power cap. They account for the fixed `BasePower` draw. The Heavy Radial's cage-free visual variant uses the same mass as the caged head because KSA cosmetic variants inherit all gameplay data:

```text
available absorption power = MaxPower - BasePower
supported mass (tonnes) = available absorption power / (G × k)
self-mass share = module mass (tonnes) / supported mass (tonnes) × 100
```

The 1 G column uses `G = 1`; the max-G column uses that rating's `MaxGLoad`. These are single-module, power-cap estimates; other modules and resource availability are not included.

| Part size | Rating | Estimated mass | Self-mass share at 1 G | Self-mass share at max G |
|---|---|---:|---:|---:|
| Compact Radial (0.5 m × 0.4 m) | Green 4 G | 392.699 kg | 0.021% | 0.083% |
| Compact Radial (0.5 m × 0.4 m) | Cyan 8 G | 392.699 kg | 0.021% | 0.164% |
| Compact Radial (0.5 m × 0.4 m) | Amber 12 G | 392.699 kg | 0.019% | 0.231% |
| Compact Radial (0.5 m × 0.4 m) | Violet 16 G | 392.699 kg | 0.019% | 0.296% |
| Heavy Radial (0.9 m × 0.55 m) | Green 4 G | 1,749.474 kg | 0.015% | 0.058% |
| Heavy Radial (0.9 m × 0.55 m) | Cyan 8 G | 1,749.474 kg | 0.014% | 0.115% |
| Heavy Radial (0.9 m × 0.55 m) | Amber 12 G | 1,749.474 kg | 0.013% | 0.162% |
| Heavy Radial (0.9 m × 0.55 m) | Violet 16 G | 1,749.474 kg | 0.013% | 0.207% |
| 1 m | Green 4 G | 1,963.495 kg | 0.052% | 0.209% |
| 1 m | Cyan 8 G | 1,963.495 kg | 0.052% | 0.413% |
| 1 m | Amber 12 G | 1,963.495 kg | 0.048% | 0.582% |
| 1 m | Violet 16 G | 1,963.495 kg | 0.047% | 0.746% |
| 2 m | Green 4 G | 15,707.963 kg | 0.079% | 0.315% |
| 2 m | Cyan 8 G | 15,707.963 kg | 0.078% | 0.621% |
| 2 m | Amber 12 G | 15,707.963 kg | 0.073% | 0.874% |
| 2 m | Violet 16 G | 15,707.963 kg | 0.070% | 1.119% |
| 3 m | Green 4 G | 53,014.376 kg | 0.079% | 0.314% |
| 3 m | Cyan 8 G | 53,014.376 kg | 0.077% | 0.619% |
| 3 m | Amber 12 G | 53,014.376 kg | 0.073% | 0.870% |
| 3 m | Violet 16 G | 53,014.376 kg | 0.070% | 1.115% |
| 4 m | Green 4 G | 125,663.706 kg | 0.056% | 0.224% |
| 4 m | Cyan 8 G | 125,663.706 kg | 0.055% | 0.442% |
| 4 m | Amber 12 G | 125,663.706 kg | 0.052% | 0.621% |
| 4 m | Violet 16 G | 125,663.706 kg | 0.050% | 0.796% |

## Balance notes

- The four ratings are shared across every form factor; larger parts do not automatically have a higher Max G.
- Power caps rise both with physical size and rating. Within a size, the Violet 16 G cap is twice the Green 4 G cap, while the Cyan and Amber caps are 1.33× and 1.67×. This raises the high-rating power ceiling without making it scale linearly with Max G.
- The caps are deliberately substantial: this equipment should remain power-hungry rather than being balanced around solar panels alone.
- `PowerPerGLoad` and `PowerPerTonne` multiply in the current implementation. Keep `PowerPerTonne` at `1` when using the table so the listed `k` values remain easy to compare.
- Because lower ratings have both lower G ceilings and lower draw, stacking several low-rated units may be more attractive than installing a high-rated unit. Part mass, manufacturing cost, attachment count, and available power should help make that a real tradeoff.
- These parts affect measured/experienced G, not vehicle trajectory or delta-v.

## Existing content example

`AccelerationAbsorbers/Content/AccelerationAbsorbers.xml` currently patches a command capsule with a 20 G module and a 50–200 ms delay. That is the existing module example, not one of the proposed part variants. Update or remove it when the new parts are implemented so it does not leave an inconsistent example in the shipped content.
