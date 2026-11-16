# Acceleration Absorbers — conceptual plan

KSA mod that adds an XML-configurable part module which negates the *experienced*
(acceleration) G-force inside a vehicle, up to a configured maximum, at an electric
power cost. Affects KSA's structural limits, crew G-effects (via API), and exposes
per-vehicle G data to other mods.

Scope: measurement-space absorption only — the ship's trajectory, delta-v and fuel
use are unchanged; only the measured/reported acceleration is reduced. Based on the
Perry Rhodan Andruckabsorber concept ("5-dimensional effects" handwave, nothing else
from the wiki page applies).

## Confirmed design decisions

- Absorption is measurement-only (experienced G inside the ship; external view
  unchanged).
- v1 ships the module attachable to any part via `<Components>`; an example patch
  adds it to an existing vanilla part. No new mesh/part assets.
- Cross-mod API via a small `AccelerationAbsorbers.Contracts` assembly shared through
  StarMap `ExportedAssemblies`/`ImportedAssemblies`, consumed as an optional
  `IModService`. **KSAGEffects integration itself is out of scope** — it will be
  wired separately later; AA only provides the API + management UI.
- Multiple absorbers on one vehicle combine proportional to each module's `MaxGLoad`.
- Delay model = **per-delta transport queue** (see algorithm): each change in the
  clamped G signal enqueues its delta with an independent random delay in
  [MinDelayMs, MaxDelayMs]. Constant delay ⇒ pure time shift; variable delay ⇒
  distorted reconstruction. No ramp/retarget semantics.
- Insufficient power or player toggle-off ⇒ **immediate** loss of absorption
  (physical constraint, not computer lag — bypasses the delay queue's effect on
  output; the queue keeps tracking so power-up resumes the delayed signal).
- Every absorber module gets a player toggle (per part, symmetry-propagated,
  persisted in save data) plus per-module stats in `DrawStateInfo`.
- A management ImGui window lists all absorbers: power draw, current/max absorbed
  G, and raw G (magnitude + per-axis) per vehicle.
- Each module has a max power draw (`MaxPower`, W; `<= 0` = uncapped). Total
  consumption is clamped to it and the existing power-deficit scaling delivers
  proportionally less absorption. Because the draw is mass-proportional, the cap
  makes larger ships get proportionally less absorption — the "one absorber can't
  carry a capital ship" limit emerges from the wattage rating, no mass/coverage
  stat needed. N modules → N× budget, same pooling as `MaxGLoad`.
- Throttle-cap bonus uses *deliverable* capacity, not installed `Σ MaxGLoad`:
  per enabled module `min(MaxGLoad, capImpliedG)` where
  `capImpliedG = (MaxPower − BasePower) / (massTonnes × PowerPerGLoad × PowerPerTonne)`
  (uncapped or zero coefficients ⇒ `MaxGLoad`), summed per vehicle. Battery state
  is ignored — theoretical limit at current mass.

## Key KSA internals (verified against KSA.dll 2026.11.10.5801 decompile)

### The single choke point for "experienced G"

`PhysicsBubble.IngestStepKinematicMeasurements(VehicleUpdateState, ReadOnlyPhysicsStates, ref KinematicMeasurements, double)` — private instance method, called once per vehicle per physics sub-step on **all** full-physics paths:

- `FullPhysicsUnconstrainedStep` (maneuvering/rolling/sailing/dragging integrators)
- `FullPhysicsConstrainedStep` (contact-constrained substeps)
- ladder-parked kitten substeps

Inside it, in order: `FlightComputer.ReadMeasurements` → `VehicleStructuralLimits.ComputeLoadFactor(stepKinematicMeasurements.AccelerationBody.Length())` → `FilteredGLoad`/`PeakGLoad` accumulation → dynamic pressure → accumulation into `UpdateData.NewKinematicMeasurements` (which becomes `vehicle.KinematicMeasurements` → `vehicle.AccelerationBody`).

`KinematicMeasurements.AccelerationBody` is a **derived measurement** (specific force,
non-gravitational). Scaling it does not touch `DeltaVelocityCci`, kinematic states,
or the integrators — trajectory is unaffected by construction.

A Harmony **prefix** on this method can mutate `stepKinematicMeasurements.AccelerationBody`
(`ref` parameter) before the original runs, so *every* downstream consumer sees the
absorbed value: structural load, flight computer, vehicle readouts, save data, and
`vehicle.AccelerationBody` (which KSAGEffects already reads).

### Paths that bypass ingest — and how they're covered anyway

`ApplySingleVehicleMotion` analytic path (`Situation.Landed/Floating/Bottomed` when
no full physics is needed) writes `NewKinematicMeasurements` directly and never calls
ingest. `ApplySurfaceMotion` sets `kinematicMeasurements.AccelerationBody` = local
gravity vector (~local g as specific force). This is exactly the gas-giant-landing
case: sustained high gravity is reported as experienced G with no structural check.

Coverage: a **postfix on `ApplySurfaceMotion`** (private static,
`(VehicleUpdateState, ref readonly BubbleOrigin, ref KinematicMeasurements, bool)`)
scales `AccelerationBody` by the same absorbed fraction and writes `LastRawG`
back — absorbers negate surface gravity too (crew floats on the pad; that's the
spec, power cost is the counterweight). `UpdateModules(window)` already runs on
this path, so power/delay logic works unchanged.

`ApplySingleFreefallMotion` leaves `AccelerationBody = 0` — weightless, nothing to
absorb, no patch needed.

### Structural limits consumers of the same data

- `DetectStructuralFailure` (per frame): `PeakGLoad / EffectiveMaxGLoad(radius)` →
  `StructuralLoad.GLoadFraction` (+ terrain/ocean/contact flags) →
  `VehicleDestructionEvent` (`ExcessiveGForce` etc.). `IsKitten` vehicles get ×2.5 —
  only `KittenEva` has `IsKitten`, and EVA kittens have no parts, so this branch is
  unaffected in practice.
- `PartFailure.Detect` + `PartContactLoad`: contact-pressure part failures —
  **not** G-driven, unaffected.
- `LoadAlert`: G-force warning alerts via `Vehicle.GetControlledStructuralLoad()` —
  absorbed automatically.
- `FlightComputer.SolveGLoadThrottleCap` (private, called from `ComputeControl`):
  caps engine throttle so predicted accel ≤ `0.9 × MaxGLoad × g`, where `MaxGLoad`
  is a public field reset every `ReadMeasurements` from `EffectiveMaxGLoad(radius)`.
  **This is a core target**: absorbers must raise the effective limit so engines
  aren't throttled below the absorbed envelope (e.g. 10 g structural + 50 g
  absorbed → allow ~59 g raw, destroy only at ~60 g). See "Throttle-cap integration".
- `Vehicle.IsThrottleGLoadLimited` / `TotalAcceleration` → HUD/telemetry — absorbed.
- `Universe.DestroyVehicleFromEvent` → explosion intensity ∝ `PeakGLoad` —
  side effect: post-absorption destructions produce smaller explosions.
- Kitten EVA render lean (`KittenEva` → `UpdateRenderData(AccelerationBody,…)`) —
  absorbed (EVA has no absorbers anyway).
- `AngularAccelerationBody` — control/damping path, **not scaled** (spec: linear G
  magnitude only).

### Module system (all public, verified)

- `ModuleBase.TemplateDataBase` / `SaveDataBase`; modules derive
  `ModuleStateful<TModule,TState,TGlobalState,TFxState>` (`TState`/`TGlobalState`
  unmanaged, `TFxState` struct). `Module<TModule>` provides `virtual Dispose()`.
- `PowerConsumer` is the reference implementation for: `Template`/`SaveData` nested
  classes, `CreateComponents(Part, PartTemplate, TransformDataReference<SubPartTemplate>?)`,
  `CreateStates`, static `UpdateModules(ref readonly ModuleUpdateContext)`,
  `PowerManager` + `IFlowManagerHost.RecreateManager`, `GetSaveData`/`ApplySaveData`,
  `DrawStateInfo`.
- `ModuleList.CreateModules(part, template, instance, parent)` is a **hardcoded**
  static dispatch — postfix it and call `AccelerationAbsorber.CreateComponents`.
  `part.Modules.Add(module)` registers the type list automatically.
- `VehicleUpdateState.UpdateModules(SimStep)` builds a `ModuleUpdateContext` and calls
  each module family's static `UpdateModules` — postfix it and call
  `AccelerationAbsorber.UpdateModules(in context)` (context is a `ref struct`,
  reconstructible from public members: `GetReadOnlyStates()`, `ReadOnlyVehicle.Parts.Modules`,
  `UpdateData.Updaters`, `FlightComputer.Ctrl2Asmb`).
- State lists: `ModuleStateful<,,>.AddStatesInto` is found by reflection — state
  registration is automatic for any `ModuleStateful` subclass. Per-vehicle committed
  states live in `PartTree.States` (`ModuleStateList`); per-substep mutable copies in
  `VehicleUpdateData.Updaters` (`ModuleStateUpdaters`, public field on public
  `VehicleUpdateState.UpdateData`).
- `TGlobalState` is one per vehicle per module type — ideal for vehicle-level
  aggregates (totals, effective G) readable by the API via `StateList.GlobalState`.
- `ModuleStateUpdaters.TryGetTypeStateUpdater<TModule,TState,TGlobalState,TFxState>`
  (non-creating) lets the ingest patch reach module states without allocating.
- `IFlowManagerHost` (`RecreateManager(PartTree)` + `OnDrawUi`) —
  `PartTree.RecreateResourceManagers` iterates it; implement for `PowerManager` rebuilds.
- Electricity: `PowerManager` per module bound to `Parent` part;
  `ResourceAvailable(batteryStates)` / `Consume(batteryNewStates, joules, out actual)`;
  batteries via `ModuleStateful<Battery,BatteryState,EmptyStruct,EmptyStruct>` updater —
  copy `PowerConsumer.UpdateModules` exactly. Connector `Electricity` capability is
  on by default → whole connected vehicle is one circuit.

### XML / content loading

- Mod `assets` files are supported natively: declare via csproj
  `<None Include="..." ModManifestArray="assets" CopyToOutputDirectory="PreserveNewest">`
  → tooling emits `assets = [...]` in `mod.toml`. KSA's own `Modify="Patch"/"Extend"`
  merge applies — **no KittenExtensions dependency required** for content.
- `PartTemplate.Components` is `[XmlArray("Components")] List<TemplateDataBase>`;
  item types registered as `XmlArrayItemAttribute` in `XmlArrayItems`.
  `PartInstance.Components` (save data) is `XmlElements`-based.
- `XmlHelper.BuildAttributeOverrides()` scans **only KSA.dll** — a mod `TemplateDataBase`
  is invisible to both serializers unless registered. Two override instances exist:
  static `XmlHelper.AttributeOverrides` (save path) and a fresh instance per
  `ContentTagTable` built by `GameContentTags.Build()` (content path, consumed by
  `ContentSerializerCache`).
- Verified: adding `XmlElementAttribute` to `XmlElements` on an `XmlArray` member
  **breaks serializer construction** — `PartTemplate.Components` needs
  `XmlArrayItemAttribute` in `XmlArrayItems`. KXC's `KxAssetInject` only writes
  `XmlElements`, so it cannot target `Components` as-is; AA self-registers instead
  (~60 lines, no KXC dependency).

## Architecture / data flow

Per physics sub-step, per vehicle (worker thread):

```
UpdateModulesJob → VehicleUpdateState.UpdateModules(subStep)     [module updates]
    └─ AA.UpdateModules (postfix), two passes over module+state pairs:
         pass 1 — per module:
           rawG       = state.LastRawG (fresh, finite) else 0
           delayedG_i = module.TickDelayQueue(step, rawG)
                          // per-module queue+EMA → delayed estimate of rawG
           deliverable_i = Enabled ? min(MaxGLoad, capImpliedG or MaxGLoad) : 0
                          // theoretical — globalState/throttle bonus/clamp
           effective_i  = adaptive delivered-capability estimate:
                          0 if !ResourceAvailable; else _effectiveDeliverableG
                          which DROPS to last-delivered CommandedAbsorptionG
                          when prev TargetAbsorptionG > CommandedAbsorptionG
                          (proof of power deficit) and recovers toward
                          theoretical with tau=0.2 s otherwise
           accumulate: requiredG = max(rawG);  ΣeffectiveG;
                          store deliverable_i[], effectiveG_i[], delayedG_i[]
         aggregate (array passes):
           shareG_i    = min(delayedG_i × effective_i/ΣeffectiveG, effective_i)
                          // capacity-proportional delayed share; queue is linear
                          // so scaling the delayed rawG ≡ delaying the share
           floorG      = min(requiredG, ΣeffectiveG)    // current requirement
           deficit     = max(0, floorG − ΣshareG_i)     // only when UNDER-delivering
           iterative headroom-fill: distribute deficit ∝ (effective_i − shareG_i
                          − boost_i) into boost_i[] until covered/exhausted
         pass 2 — per module i:
           _boostG_i    = EMA(boost_i, tau=(MinDelayMs+MaxDelayMs)/2 ms, cap 0.25 s)
                          // coverage ramps in with ~queue-delay — modules keep
                          // a delay before increasing even via the deficit fill
           commandedG_i  = min(shareG_i + _boostG_i, deliverable_i)
           gate     = Enabled && ResourceAvailable
           power    = gate ? min(BasePower + massTonnes×PowerPerGLoad×cmd×PowerPerTonne,
                              MaxPower if > 0 else ∞) : 0
           joules   = watts.Integrate(dt); Consume(...); state.ConsumedWatts = actual
           write state: CommandedAbsorptionG = cmd×powerScale, ConsumedWatts, Active
           aggregate into GlobalState: Σcapacity, ΣdeliverableG, Σpower, commanded totals
           publish vehicle telemetry snapshot (rawG vec, absorbed, per-module rows)

Integration (VelocityVerlet / Rk8 / constrained) → KinematicMeasurements

IngestStepKinematicMeasurements(vehicleState, states, ref measurements, dt)  [prefix]
    rawG      = measurements.AccelerationBody.Length() / STANDARD_GRAVITY
    eff_i     = state_i.Active ? state_i.CommandedAbsorptionG : 0   // immediate gate
    absorbed  = min(Σ eff_i, rawG)          // never negative, capped by reality
    fraction  = rawG > 0 ? absorbed / rawG : 0
    measurements.AccelerationBody *= (1 - fraction)   // per-axis via magnitude %
    write back per-module state: LastRawG, RawGSimSeconds, AbsorbedG share
    update GlobalState: RawG(vec), AbsorbedG, EffectiveG = rawG - absorbed

→ original ingest runs: FlightComputer, FilteredGLoad/PeakGLoad,
  StructuralLoad, NewKinematicMeasurements accumulation
→ frame end: DetectStructuralFailure, UpdateBulkStates commits states
→ vehicle.AccelerationBody, vehicle.StructuralLoad, API consumers see absorbed G

Analytic surface path (once per window, not per substep):
ApplySingleVehicleMotion → UpdateModules(window) → ApplySurfaceMotion
    └─ AA postfix: same absorbed-fraction scaling on AccelerationBody (= gravity)
```

Timing note: module update (pre-integration) computes power/command from the
previous sub-step's measured G; ingest (post-integration) applies absorption
immediately. One sub-step of latency is inherent and negligible vs ms-scale delays.
On the analytic path the same one-window latency applies.

## Components to build

### 1. `AccelerationAbsorber` module (`AccelerationAbsorber.cs`)

`ModuleStateful<AccelerationAbsorber, AccelerationAbsorberState, AccelerationAbsorberGlobalState, EmptyStruct>`
+ `IFlowManagerHost`. Copy `PowerConsumer` structure:

- `Template : TemplateDataBase` — `[XmlType(TypeName = "AccelerationAbsorber")]`,
  `KeyedTemplate.Key` for multiple instances per part.
- `SaveData : SaveDataBase` — `[XmlType(TypeName = "AccelerationAbsorberData")]`
  with `[XmlAttribute("Active")] bool` storing the `Enabled` desired-state
  (same convention as `PowerConsumer.SaveData`).
- Fields from template: `BasePower`, `MaxPower`, `PowerPerGLoad`
  and `PowerPerTonne` (`PowerReference`→`Watts`; `MaxPower <= 0` = uncapped),
  `MaxGLoad` (double, g), `MinDelayMs`, `MaxDelayMs` (double, ms→s), `PowerManager`,
  `Parent`, config snapshot.
- Instance fields: `Enabled` (player desired-state, default true — the toggle),
  delay-event buffer ordered by `readySimTime` (sorted array / min-heap, ~256
  entries) + `commanded`/`lastClampedG`/`residual` accumulators, `Random` for
  delay rolls.
- `CreateStates` → default state; `CreateComponents` scans `template.Components`.
- `UpdateModules(ref readonly ModuleUpdateContext)` static — updater pattern from
  `PowerConsumer.UpdateModules`; battery updater via
  `ModuleStateful<Battery,BatteryState,EmptyStruct,EmptyStruct>`.
- `RecreateManager(PartTree)` + `OnDrawUi` — `PowerManager` lifecycle.
- `SetEnabled(bool)` — mirror `PowerConsumer.SetLightIsActive`:
  `VehicleEditor.ForEachModuleWithSymmetry` propagation to symmetric counterparts.
- `DrawStateInfo(ref readonly State)` — mirror `PowerConsumer.DrawStateInfo`:
  `ImGuiHelper.DrawCheckbox` toggle wrapped in `VehicleEditor.RecordsHistoryFor`
  (undoable in editor), plus rows for power draw (W), absorbed G now / max
  (`MaxGLoad`), and the vehicle's raw G magnitude + per-axis.
- `GetSaveData`/`ApplySaveData` — persist `Enabled` (desired toggle) into
  `SaveData.Active`; on apply restore `Enabled` on the instance and seed the
  state (mirror `PowerConsumer` lines 180–207).
- `Dispose` — free via `Module<TModule>`.

### 2. State structs (unmanaged)

`AccelerationAbsorberState` (per module):
`Active` (enabled && powered — actual absorption live), `Enabled` mirror not needed
(instance holds desired state), `ConsumedWatts`, `CommandedAbsorptionG` (queue
output snapshot, read by ingest), `AbsorbedGThisStep`, `LastRawG`,
`LastRawGSimSeconds`.

The delay queue itself is **not** in the state struct (states must be unmanaged) —
it lives on the module instance as a small buffer of `(readySimTime, deltaG)`
entries ordered by ready time (managed scratch, transient, not saved; reseeded on
load — see algorithm).

`AccelerationAbsorberGlobalState` (per vehicle):
`TotalCapacityG`, `TotalDeliverableG` (Σ per-module deliverable-at-current-mass,
enabled modules only — throttle bonus source), `TotalCommandedG`, `TotalPowerW`,
`RawG` (double3 — magnitude derivable, per-axis for UI/API), `AbsorbedG`,
`EffectiveGX/Y/Z`, `ActiveModuleCount`, `LastIngestSimSeconds`.

### 3. Absorption algorithm (per module queue + vehicle-level distribution)

Each module keeps an **individual** delay queue, but all queues track the same
unclamped signal — capacity is enforced at distribution time, not in the queue:

```
// per module, in TickDelayQueue(step, rawG):
rawG      = state.LastRawG if (Step.NextTime - LastRawGSimSeconds) < staleness
            and finite, else 0              // analytic-freefall / NaN guard
signalG   = max(0, rawG)                    // unclamped — queue sees vehicle G

// buffer on module instance: (readySimTime, deltaG) entries kept ORDERED BY
// readySimTime (sorted insert / min-heap — random delays break enqueue order;
// a later delta can become due before an earlier one). ~256 entries.
// commanded: instance accumulator seeded = signalG on first update / after load
residual += signalG - lastSignalG           // accumulate sub-eps drift
if |residual| > eps:                          // eps ~1e-4 g, kills float-noise flooding
    insert(residual, ready = Step.NextTime + rand(minDelay,maxDelay)); residual = 0
lastSignalG = signalG
while head.readySimTime <= Step.NextTime:    // head = earliest ready entry
    commanded += head.deltaG; dequeue
if queue full: commanded = signalG; clear     // rare overflow → snap-resync
// commanded is NOT clamped — deltas telescope exactly (Σ enqueued deltas =
// signalG − seed), so partial sums may dip below 0 transiently when random
// delays flush negative deltas first. Clamping here would permanently lose
// that negative area and leave commanded offset HIGH forever (the "pinned at
// 20 g after a crash" bug). Output is clamped instead: delayedG_i = max(0,
// smoothed); the EMA tracks commanded including transient negatives.
//
// stall watchdog: enqueued deltas telescope ⇒
//   _commandedG + _pendingDeltaSum + _residualG ≡ signalG   (exact, ~1e-15)
// _pendingDeltaSum tracks Σ(enqueued − flushed) in O(1). Each tick, if the
// accounting diverges from signalG by > DriftEpsilonG (1e-3) or goes
// non-finite — i.e. a delta was lost, a clamp crept back, or NaN slipped in —
// the queue snap-resyncs (_commandedG = _smoothedG = signalG, queue cleared).
// Shares the overflow resync path; should never trigger in healthy operation.
// per-module EMA smoothing of the reconstruction:
//   tau = clamp((MaxDelayMs - MinDelayMs) × 0.00025 s, 0..0.25)
//   alpha = 1 - exp(-dt / tau);  smoothed += (commanded - smoothed) × alpha
//   (tau = 0 for constant delay ⇒ smoothed = commanded, no extra lag)
delayedG_i = smoothed                        // this module's delayed raw-G estimate
```

Vehicle-level distribution (UpdateModules, two passes + array aggregate):

```
// effective_i = adaptive estimate of what module i can actually deliver now:
//   0 when !ResourceAvailable; otherwise _effectiveDeliverableG which drops
//   instantly to last-tick CommandedAbsorptionG when
//   TargetAbsorptionG > CommandedAbsorptionG + eps (proof the power budget
//   fell short — NOT merely low demand), and recovers toward theoretical
//   deliverable with tau = 0.2 s otherwise. Delivered-amount alone can't
//   measure capability (low rawG ⇒ low delivered legitimately), so the
//   deficit signature is required.
shareG_i    = min(delayedG_i × effective_i / ΣeffectiveG, effective_i)
              // each module's delayed, capacity-proportional share of rawG,
              // weighted by EFFECTIVE (adaptive) capacity so a power-limited
              // module's share shrinks and healthy modules pick it up.
floorG      = min(requiredG_now, ΣeffectiveG)
deficit     = max(0, floorG − ΣshareG_i)     // ONLY when under-delivering —
              // all-high spool-down gives deficit 0 (untouched)
boost_i     = iterative ∝-headroom fill of deficit into
              (effective_i − shareG_i − boost_i), ≤ moduleCount rounds
commanded_i = min(shareG_i + EMA(boost_i), deliverable_i)
              // boost EMA tau = (MinDelayMs+MaxDelayMs)/2 ms (cap 0.25 s) ⇒
              // coverage ramps in with ~the queue delay — modules keep a
              // delay before increasing even through the deficit fill.
```

Result: `Σcommanded_i ≈ min(floorG, coverable)` once boost EMAs settle ⇒
experienced G stays 0 through module desync and partial power failures.
On ramp-down `ΣshareG_i > floorG` ⇒ deficit = 0 ⇒ pure delayed spool-down.
Ramp-up deficit coverage is delayed by the boost EMA rather than instant —
per-module commanded still lags; the vehicle may leak G for ~the boost
tau while coverage ramps in (the intended delay behavior).

Then per module (pass 2):

```
gateOpen  = module.Enabled && PowerManager.ResourceAvailable(batteryStates)
if gateOpen:
    requiredW = BasePower + massTonnes×PowerPerGLoad×commandedG×PowerPerTonne
    if MaxPower > 0: requiredW = min(requiredW, MaxPower)   // hard draw ceiling
    joules = watts.Integrate(dt); Consume(...); state.ConsumedWatts = actual
    // deficit scaling: effective commanded = commandedG × clamp(
    //   (actualW − BasePower) / (mass×perG×commandedG×perTonne), 0, 1)
    // — denominator stays the UNCAPPED requirement, so the cap and battery
    //   shortfall compose through the same ratio.
    state.CommandedAbsorptionG = commandedG × powerScale
    state.Active = actual > 0
else:
    consume nothing; state.ConsumedWatts = 0; state.Active = false
    // queues keep tracking; ingest applies gateOpen per module — see below
```

Ingest side (per vehicle): per-module effective command =
`state.Active ? state.CommandedAbsorptionG : 0`;
`absorbed = min(Σ effective, rawG)`; `absorbed_i = absorbed × eff_i / Σeff`
(proportional shares); `fraction = absorbed / rawG`; scale `AccelerationBody`
by `(1 - fraction)`.

Properties:
- Constant delay τ ⇒ `delayedG_i(t) ≈ rawG(t−τ_i)` per module — pure phase
  shift; variable per-event τ ⇒ distorted reconstruction (intended).
- Per-module delay preserved: each queue reconstructs `rawG(t−τ_i)` with
  independent random delays; `shareG_i = delayedG_i × effective_i/Σeffective`
  gives each module its own delayed capacity-proportional target ⇒ different
  absorption rates during transitions (no forced equalization). At steady
  state all `delayedG_i ≈ rawG` ⇒ shares ∝ effective capacity ⇒ proportional.
- 0 g priority via deficit coverage: `floorG = min(rawG_now, Σeffective)`;
  deficit fires ONLY when delayed shares under-deliver (all-high spool-down
  ⇒ deficit 0, untouched). The shortfall fills ∝ headroom
  (`effective_i − shareG_i − boost_i`) — lagging modules cover what
  ahead-of-schedule ones can't. The boost then passes through a per-module
  EMA (`tau = mean queue delay`) so coverage itself ramps in with delay —
  modules keep a delay before increasing even via the fill.
- Power-failure coverage: `effective_i` is an adaptive estimate — drops to
  last-delivered G when `TargetAbsorptionG > CommandedAbsorptionG` proves a
  power deficit (delivered-amount alone can't distinguish "needed less"
  from "couldn't"), recovers toward theoretical with tau=0.2 s. A browned-out
  or unpowered module's share shrinks ⇒ healthy modules cover; `0` when
  `ResourceAvailable` fails outright. `deliverable_i` (theoretical) still
  caps commanded and feeds the throttle bonus.
- Limited absorbers handled structurally: `deliverable_i` folds in `MaxGLoad`
  and `MaxPower`→`capImpliedG`; shares/headroom use `effective_i` so
  power-limited modules get smaller shares automatically. Saturated shares
  clamp to `effective_i` before summing so excess demand redistributes.
- Delay asymmetry: ramp-down keeps full spool-down delay; ramp-up coverage
  arrives via the delayed boost EMA ⇒ vehicle may leak G for ~boost tau
  while coverage ramps (intended delay, not a bug). `absorbed ≤ rawG` at
  ingest ⇒ never negative G. ✓
- Non-finite guard: `rawG` NaN/∞ is rejected in `Apply` (never written to
  `LastRawG`) and in `UpdateModules` (treated as stale → 0). Previously a
  single NaN sample poisoned `_residualG` permanently (`|NaN| > eps` is
  always false → no delay events ever enqueued → commanded froze at its last
  value — the "stuck at 20 g on the pad" bug).
- Delta conservation: `_commandedG` must stay unclamped so enqueued deltas
  telescope exactly. Violent signals (crash spikes) produce negative partial
  sums when random delays flush drop-deltas before rise-deltas; clamping at
  0 loses that area ⇒ permanent positive offset ⇒ absorption pinned at
  capacity. Observed as "stuck at 20 g after crashing on the pad".
- Power loss/toggle-off: `state.Active=false` → effective command 0 **this
  substep** — immediate physical cutoff; queue continues so power-up resumes
  the delayed signal (no artificial restart transient). Disabled modules get
  `deliverable_i = 0` ⇒ zero share and zero headroom, but their queues keep
  tracking, so re-enabling rejoins seamlessly.
- Queue sizing: `maxDelay / minSubstep + margin` entries (~256 covers 1 s at
  4 ms); overflow policy = snap `commanded` to `signalG` and clear (rare —
  needs >1 s of continuous change at max delay — and self-healing).
- Save/load: queue is transient — on first update after load, seed
  `lastSignalG = commanded = smoothed = signalG(now)` so the absorber starts
  synced to current G rather than replaying a stale history.
- "Active" semantics: `Enabled` (player toggle, default true) && power
  available. Only `BasePower` is drawn at zero commanded G; the absorption
  term (mass×perG×G×perTonne) scales with commanded absorption and is the
  only part eligible to power absorption — delivered-vs-required ratio
  scales `CommandedAbsorptionG` under deficit.

### 4. Harmony patches (`Patches.cs`)

- `[HarmonyPatch(typeof(ModuleList), "CreateModules")]` postfix →
  `AccelerationAbsorber.CreateComponents(part, template, instance)`.
- `[HarmonyPatch(typeof(VehicleUpdateState), "UpdateModules")]` postfix →
  rebuild `ModuleUpdateContext` from `__instance` public members →
  `AccelerationAbsorber.UpdateModules(in context)`.
- `[HarmonyPatch(typeof(PhysicsBubble), "IngestStepKinematicMeasurements")]` prefix →
  read absorber states via `vehicleState.UpdateData.Updaters.TryGetTypeStateUpdater<…>`
  (non-creating; early-out when absent), compute absorbed fraction, scale
  `stepKinematicMeasurements.AccelerationBody`, write `LastRawG`/`AbsorbedG`/
  global aggregates back through `updater.GetNewStates()`/`GlobalState`.
- `[HarmonyPatch(typeof(PhysicsBubble), "ApplySurfaceMotion")]` postfix →
  same fraction math on `kinematicMeasurements.AccelerationBody` (= gravity) for
  the analytic `Landed`/`Floating`/`Bottomed` path; writes `LastRawG` back too.
- `[HarmonyPatch(typeof(FlightComputer), "SolveGLoadThrottleCap")]` prefix+postfix →
  temporarily add `bonusG / 0.9f` to `__instance.MaxGLoad` during the solve,
  restore after (algebraically = additive force budget; see "Throttle-cap integration").

Ordering: AA update runs after all vanilla module updates (postfix) → vanilla
consumers get power priority; ingest prefix runs before `ReadMeasurements` →
flight computer and all limit checks see absorbed G.

### Throttle-cap integration

`SolveGLoadThrottleCap` builds a **force budget** and solves throttle against it:

```csharp
float num  = 0.9f * MaxGLoad * 9.80665f;   // accel limit (m/s²)
float num2 = num * mass;                    // force budget (N)
num3 = clamp(num2 / thrust, minThrottle, 1) // + 2 refinement iterations
```

The absorber capacity must extend the budget **additively after** the 0.9 safety
factor — absorbed G isn't structural load, so it must not be discounted by it:

```
num'  = (0.9 × MaxGLoad + bonusG) × g       // bonusG = Σ absorber MaxGLoad
num2' = num' × mass
```

With MaxGLoad=10, bonus=50 → raw accel allowed ≈ 59 g → experienced 9 g
(= 0.9 × 10, full use of the safety margin), destruction at raw ≈ 60 g.
Inflating `MaxGLoad` directly (`0.9 × (10+50) = 54`) would cap raw at 54 g →
only 4 g experienced — wrong.

Implementation: `SolveGLoadThrottleCap` is private and its locals aren't reachable,
but `num` is linear in the public `MaxGLoad` field. Prefix/postfix pair:
prefix sets `__instance.MaxGLoad += bonusG / 0.9f`, postfix restores — algebraically
identical to the additive budget (`0.9 × (MaxGLoad + bonus/0.9) = 0.9×MaxGLoad + bonus`).
The `0.9f` is a literal inside the method — AA keeps a named constant mirroring it;
if KSA changes the factor the bonus is slightly mis-scaled (tunable, low risk).

Bonus source: static `ConditionalWeakTable<FlightComputer, double>` mapping each
vehicle's flight computer to the vehicle's `TotalDeliverableG` — Σ over **enabled**
modules of `min(MaxGLoad, capImpliedG)` at current mass (see algorithm; uncapped
modules contribute `MaxGLoad`). Computed in `UpdateModules` where `massTonnes`
is already in hand, read from `updater.GlobalState` in the ingest/surface patches
(`vehicleState.FlightComputer` is the same instance `ComputeControl` later uses;
self-heals if `NewFlightComputer` is ever recreated). Battery state is ignored —
the bonus reflects the theoretical deliverable limit, so insufficient charge or
G above delivered capacity still passes through and can destroy the vessel. That
passthrough is intended: the player owns the risk. Disabled modules contribute
nothing (player intent, not power insufficiency).

### 5. XML serializer self-registration (`XmlRegistration.cs`)

- `[StarMapBeforeMain]`/early init + `GameContentTags.Build` postfix:
  - `XmlHelper.AttributeOverrides`: `(PartTemplate,"Components")` →
    `XmlArrayItems.Add(XmlArrayItemAttribute("AccelerationAbsorber", typeof(Template)))`;
    `(PartInstance,"Components")` → `XmlElements.Add(XmlElementAttribute("AccelerationAbsorberData", typeof(SaveData)))`.
  - same additions into each `ContentTagTable.Overrides` (fresh instance per build).
- Timing: before `ContentLoader` deserializes parts; `ContentSerializerCache` keys
  on the overrides instance so post-build mutation is safe.

### 6. Content XML (`Content/AccelerationAbsorbers.xml`, `assets` in mod.toml)

```xml
<Assets>
  <!-- example: attach an acceleration absorber to a crewed vanilla command capsule -->
  <Part Id="CoreCommandA_Prefab_MediumCapsuleVariantA" Modify="Patch">
    <Components Modify="Extend">
      <AccelerationAbsorber MaxGLoad="20" MinDelayMs="50" MaxDelayMs="200">
        <BasePower W="50" />
        <MaxPower W="1000" />
        <PowerPerGLoad W="2" />
        <PowerPerTonne W="1" />
      </AccelerationAbsorber>
    </Components>
  </Part>
</Assets>
```

Power fields are `PowerReference` child elements (same convention as
`PowerConsumer`); `MaxPower <= 0` means uncapped.

### 7. Cross-mod API

- New `AccelerationAbsorbers.Contracts` project (net10.0, references KSA for `Vehicle`):
  - `VehicleGForceSnapshot` struct: `VehicleId`, `RawG` (body-frame vector, g),
    `EffectiveG` (post-absorption vector, g), `AbsorbedFraction`,
    `TotalCapacityG`, `TotalPowerW`, `ActiveModuleCount`.
  - `IGForceDataProvider` interface: `bool TryGetSnapshot(Vehicle, out VehicleGForceSnapshot)`.
- AA registers the service via StarMap `IModServiceRegistry` in `[StarMapImmediateLoad]`;
  snapshot reads committed `PartTree.States` type list (`StateList.GlobalState` +
  per-module states) — main-thread safe.
- AA `mod.toml`: `[StarMap] ExportedAssemblies = ["AccelerationAbsorbers.Contracts"]`.
- Consumers (e.g. KSAGEffects) wire the optional dependency on their side later —
  out of scope here. Effective G ≈ what `vehicle.AccelerationBody` already returns
  once absorption is live — the API adds raw-G/absorbed detail.

### 8. Management UI + telemetry

- Per-part toggle + stats: `DrawStateInfo` on the module (see "AccelerationAbsorber
  module" above) — appears in the part info panel automatically; no extra work.
- Management window: extend the existing `[StarMapAfterGui]` ImGui window in
  `Mod.cs` — a toggleable "Acceleration Absorbers" window listing, per vehicle
  with absorbers: vehicle name, raw G magnitude + per-axis (GX/GY/GZ), and per
  absorber module: part name, power draw (W), absorbed G now, max absorbed G
  (`MaxGLoad` capacity), enabled state.
- Data source: the ingest/update patches already compute everything — publish a
  per-vehicle immutable telemetry snapshot into a static store
  (`ConcurrentDictionary<Vehicle, VehicleAbsorberTelemetry>` or
  `ImmutableInterlocked` swap) keyed by `ReadOnlyVehicle`. UI reads the store on
  the main thread — no `AccessTools` into `PhysicsBubble._vehicleStates` needed,
  no committed-state spelunking, and vehicles without absorbers simply have no
  entry (prune stale entries by sim-time heartbeat or on vehicle destroy).
- Saved state = the per-module `Enabled` toggle only (via `SaveData`); telemetry
  and window open-state are transient.

## Consumer map — everything absorption touches (answer to "investigate other things")

Affected (all via scaled `AccelerationBody`/`PeakGLoad`):
vehicle destruction check, `StructuralLoad`/`LoadAlert` warnings, kitten-vehicle
×2.5 limit path (moot — no parts on EVA), `FlightComputer.TotalAcceleration`
telemetry, `vehicle.AccelerationBody` (console readouts, KSAGEffects, EVA lean),
explosion sizing on destruction, `SolveGLoadThrottleCap` (via `MaxGLoad` bonus),
analytic surface path (`ApplySurfaceMotion` → gravity G absorbed, e.g. gas-giant
landings).

Not affected: trajectory/delta-v/fuel, `DeltaVelocityCci`, `AngularAccelerationBody`
(control), `PartContactLoad`/part contact failures, analytic freefall (~0 g anyway).

## Optional extensions (explicitly out of v1 scope)

- `AngularAccelerationBody` absorption.
- Absorb a configurable minimum-G threshold (e.g. ignore the 1 g pad load while
  still covering gas-giant gravity) — v1 absorbs all magnitude per spec.
- Throttle-cap bonus further scaled by actual battery state / active modules —
  v1 uses deliverable-at-current-mass (enabled modules, power cap applied,
  battery ignored); G passthrough under insufficient charge is a feature.
- KittenExtensions content patching (not needed — vanilla `Modify` covers it).

## Risks / open questions

- `IngestStepKinematicMeasurements`, `ApplySurfaceMotion`,
  `SolveGLoadThrottleCap` are all private — signatures must match exactly
  (`ReadOnlyPhysicsStates` is a `readonly ref struct`; `KinematicMeasurements` a struct;
  `SolveGLoadThrottleCap` returns `float`, takes `ref readonly FlightComputerOutput`).
- Worker-thread safety: all state access goes through per-vehicle `Updaters`;
  no main-thread APIs in module update or measurement patches.
- `ConditionalWeakTable<FlightComputer, double>` bonus map relies on
  `UpdateData.NewFlightComputer` instance identity surviving between ingest and
  `ComputeControl`; self-heals if recreated, but verify in testing.
- Surface-path absorption means absorbers negate resting gravity too — continuous
  base+dynamic power draw while landed. Intended per spec; tune XML if oppressive.
- Save compat: saves containing `<AccelerationAbsorberData>` fail to load if AA is
  removed (standard mod risk; KSA tolerance for unknown `Components` items unverified).
- Staleness threshold for `LastRawGSimSeconds` needs tuning (analytic freefall
  transition; surface path now feeds G every window).
- Delay-queue edge cases: epsilon residual accumulator, ring-buffer sizing vs
  substep dt, overflow snap-resync, reseed-on-load behavior, behavior
  across analytic↔physics transitions (queue keeps tracking `LastRawG`).
- Telemetry store is written on worker threads and read on the main thread —
  must be immutable snapshots + thread-safe publication; prune destroyed vehicles.
- Throttle-cap bonus mirrors the `0.9f` literal inside `SolveGLoadThrottleCap`;
  a KSA update changing that factor mis-scales the bonus (low risk, tunable).
- `MaxPower <= 0` means uncapped — a modder omitting it gets unlimited draw
  (backward compatible, but the mass-limit guarantee silently disappears).
- `capImpliedG` divides by `massTonnes × PowerPerGLoad × PowerPerTonne`; zero
  coefficients mean free absorption ⇒ treated as uncapped for both the draw cap
  and the deliverable bonus.

## Implementation steps

- [x] Create `AccelerationAbsorbers.Contracts` project + `IGForceDataProvider`/`VehicleGForceSnapshot`; wire `ExportedAssemblies`/project reference.
- [x] `AccelerationAbsorber.cs`: Template/SaveData/state structs/module class (`CreateComponents`, `CreateStates`, `UpdateModules`, `RecreateManager`, `OnDrawUi`, save-data overrides, `DrawStateInfo`).
- [x] `XmlRegistration.cs`: override registration for `PartTemplate.Components` (XmlArrayItems) + `PartInstance.Components` (XmlElements), both override instances.
- [x] `Patches.cs`: Harmony patches — CreateModules postfix, UpdateModules postfix, Ingest prefix (absorption math + state writeback + throttle-bonus map + telemetry publish), ApplySingleSurfaceMotion postfix, SolveGLoadThrottleCap prefix/postfix (deliverable-G bonus).
- [x] `Content/AccelerationAbsorbers.xml` + csproj `ModManifestArray="assets"` entry; example part patch.
- [x] `Mod.cs`: service registration via `IModServiceRegistry`; management ImGui window reading the telemetry store; keep/adjust welcome window.
- [x] Build `dotnet build -c Release`; fix compile issues.
- [x] Power draw cap (`MaxPower`) + deliverable-capacity throttle bonus.
- [ ] In-game verification (ksa-launch): spawn patched part, confirm module creates, battery drains, console G-readout shows reduced G under thrust, no destruction at >limit G, engines not throttled below absorbed envelope, gas-giant-style surface G absorbed, per-part toggle works + propagates to symmetric parts, management window shows all absorbers/raw G, save/load round-trip preserves toggles, heavy ships get proportionally less absorption under the power cap.
- [ ] (Later, separate repo) KSAGEffects optional-dependency integration.
