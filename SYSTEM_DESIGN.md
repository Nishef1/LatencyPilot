# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-15

`ROADMAP.md` defines required product/phase outcomes. `PROJECT_STATUS.md` records current evidence and the execution ladder. ADRs record accepted architecture changes.

## 1. Purpose

LatencyPilot is a Windows 11 latency experimentation platform built around:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

A system-changing feature is incomplete unless it can follow:

```text
Detect applicability
→ Snapshot exact original state
→ Validate candidate
→ Journal pending experiment
→ Apply one narrow change
→ Verify actual applied state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
→ Close journal
```

Phase 2 establishes the trustworthy read-only measurement substrate. ADR 0004 permits targeted later-phase safety/candidate **source implementation** to overlap remaining Phase 2 physical closure after the required measurement evidence exists. That overlap never arms mutation and never changes a phase exit gate.

## 2. Supported target

Initial supported target:

- Windows 11 x64;
- active local interactive desktop session;
- local machine only;
- non-elevated desktop App;
- self-contained .NET / Windows App SDK deployment;
- privileged Service only where kernel observation or later verified mutation requires it.

Current observation authorization is intentionally limited to the active console session. RDP/multi-session support is not implied. ARM64 is not a 1.0 commitment until every dependency and physical-validation path exists.

## 3. Technology baseline

Unless an ADR changes it:

- C# 14;
- .NET 10 LTS;
- WinUI 3;
- Windows App SDK 2.4 Stable;
- unpackaged App with self-contained Windows App SDK runtime;
- narrow Windows Service privileged boundary;
- typed/versioned local Named Pipes;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`;
- PresentMon for graphics/frame telemetry where applicable;
- SetupAPI + Configuration Manager for PnP/resource evidence and exact-target refresh;
- documented processor-topology / CPU-set APIs;
- Raw Input for host-observable input timing;
- concrete SQLite journal/recovery state through `LatencyPilot.Persistence` / `Microsoft.Data.Sqlite`;
- MSTest + Microsoft.Testing.Platform for the deliberately small permanent suite;
- bounded structured local diagnostics with Serilog.

NuGet package versions are centrally owned by `Directory.Packages.props`.

Native C++ is not a baseline dependency. Add a native component only if profiling or an authoritative vendor SDK creates a concrete need and an ADR records the boundary.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ WinUI 3 / normal user / non-elevated      │
│ inventory + evidence + experiment UX      │
└───────────────────┬────────────────────────┘
                    │ typed/versioned local Named Pipe
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service                       │
│ privileged boundary                       │
│ public IPC: read-only observation         │
│ internal mutation/experiment source       │
│ remains unarmed                           │
└──────────────┬─────────────────┬───────────┘
               │                 │
               ▼                 ▼
┌──────────────────────────┐  ┌──────────────────────────┐
│ Platform.Windows         │  │ Persistence              │
│ ETW / SetupAPI / CM      │  │ SQLite mutation journal │
│ device/topology APIs     │  │ + recovery state        │
└──────────────┬───────────┘  └──────────────────────────┘
               ▼
         Windows 11 / hardware

Pure/shared layers:
Benchmarking → Core
Platform.Windows → Core
Protocol = standalone typed IPC contract
Persistence = concrete durable state boundary
```

There is intentionally no generic repository abstraction between the Service and the concrete mutation journal. Add one only when it solves an existing requirement rather than reserving hypothetical architecture.

## 5. Project responsibilities

### `LatencyPilot.Core`

Stable domain concepts and invariants only. No WinUI, ETW implementation, registry paths, P/Invoke, Service hosting or SQLite.

### `LatencyPilot.Benchmarking`

Canonical percentile estimator, metric series, repeated-baseline quality, noise/drift analysis, comparisons, guardrails and verdicts. Keep deterministic and hardware-independent where possible.

### `LatencyPilot.Protocol`

Typed/versioned IPC commands, DTOs and errors only. Never a generic privileged execution surface.

Current public contract:

```text
ProtocolVersion.Current = 6
Pipe = LatencyPilot.Observation.v6
Commands = GetStatus, CaptureKernelLatency
MutationAvailable = false
```

### `LatencyPilot.Platform.Windows`

Windows-specific mechanisms: topology, inventory, ETW, SetupAPI/CM, resource/configuration evidence, GPU interrupt-affinity state/applicability, exact-target device refresh, runtime placement evidence and PresentMon integration.

Windows-specific semantics must not leak into pure Core/Benchmarking layers.

### `LatencyPilot.Persistence`

Concrete SQLite boundary for mutation journal/recovery state. It owns the durable schema, compare-and-swap revisions, unresolved-state blocking and persisted experiment lifecycle needed for recovery. It is not a generic application repository layer.

### `LatencyPilot.Service`

Privileged boundary. The public protocol currently hosts read-only observation only. Internal source may prepare/apply/revert narrowly supported changes only behind the durable journal and fail-closed recovery logic. The owner-only validation harness may access those internals for Gate A, but it is not a product surface. User-reachable mutation remains unavailable through Gate A, Gate B implementation and Gate C validation; it is armed only at Gate D.

The Service never becomes a generic scripting host.

### `LatencyPilot.App`

Normal-user WinUI experience. Local non-privileged inventory may call `Platform.Windows` directly. Privileged observation and future armed mutation cross the typed Protocol to the Service. Repeated-baseline and optimization interpretation belong in deterministic Benchmarking rather than duplicated UI logic.

## 6. Dependency direction

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             ← standalone
Platform.Windows     → Core
Persistence          ← no LatencyPilot project dependency
Service              → Core + Benchmarking + Protocol + Platform.Windows + Persistence
App                  → Core + Benchmarking + Protocol + Platform.Windows
CriticalTests        → only projects needed by durable critical scenarios
```

No cyclic references and no speculative abstraction projects.

## 7. Deployment and privilege contract

The current App uses:

```text
WindowsPackageType=None
WindowsAppSDKSelfContained=true
runtime target=win-x64
```

The App remains non-elevated. Installer deployments place App and Service under protected Program Files paths. A portable distribution may place the normal-user App in an ordinary extraction folder, but elevated Service installation must copy the privileged Service payload to:

```text
%ProgramFiles%\LatencyPilot\Service
```

before LocalSystem registration.

A LocalSystem Service must never execute from an ordinary user-writable portable extraction path.

## 8. Evidence semantics

Do not collapse distinct evidence levels:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Examples:

- registry `MSISupported` / affinity policy = stored configuration;
- ConfigMgr allocated IRQ resources = assigned resource evidence;
- ETW DPC/ISR events = runtime behavior.

A stored configuration hint is not proof of active interrupt delivery mode. Unavailable optional device metadata remains explicit partial evidence rather than being converted to “none”.

A raw DPC/ISR routine address is not a driver identity until authoritative kernel image evidence maps it to a valid image range/lifetime. Missing or ambiguous mappings remain unresolved.

## 9. Measurement modes

### 9.1 Quick diagnostic snapshot

```text
1 × 5 seconds
purpose = quick-diagnostic-snapshot
```

Used for ETW integrity, CPU concentration, module attribution, local tail/reference context and hypothesis generation. It is **not** a health verdict, stability proof or optimization recommendation.

### 9.2 Repeated decision baseline

```text
workload already warmed/repeatable where applicable
5 s LatencyPilot/service settle
5 × 20 s authoritative windows
750 ms inter-window settle
purpose = repeated-decision-baseline
method = baseline-quality-v2
```

The initial five seconds are a LatencyPilot/service settle period, not workload warm-up. A game/application workload must already be at the intended repeatable point before the sequence starts.

## 10. Canonical percentile contract

The canonical estimator belongs to `LatencyPilot.Benchmarking.Statistics.Percentiles`.

For sorted samples it uses linear interpolation at:

```text
position = (sampleCount - 1) * percentile
```

Service observation summaries, baseline quality and benchmark comparisons must use this same estimator unless a deliberately versioned methodology replaces it.

Current DPC/ISR distributions expose count, p50, p95, p99, max and p99.9 only when the individual distribution contains at least **10,000 samples**. Below that adequacy floor p99.9 is absent.

## 11. `baseline-quality-v2`

The v2 decision baseline requires exactly five contiguous windows.

A window is eligible only when:

```text
requested duration >= 20,000 ms
actual duration >= 95% of request
capture integrity clean
DPC event count >= 1,000
ISR event count >= 1,000
finite positive DPC p99
finite positive ISR p99
```

Capture integrity fails when ETW loss is unavailable/non-zero, invalid latency/image evidence is present, or the configured event limit is reached.

Across the five eligible p99 values for each metric:

```text
relative noise floor = (P90 - P10) / |median|
maximum relative noise floor = 30%
maximum early/late relative drift = 20%
extreme window deviation limit = 50%
```

No inconvenient window may be silently deleted. `Valid` means repeatable enough for the current comparison method; it does not mean the PC is globally healthy.

Runtime CPU/power context is provenance. It can reveal mismatched conditions but does not silently alter the versioned formula.

## 12. DPC/ISR reference semantics

Current UI/evidence may surface:

```text
DPC >100 µs   Microsoft driver-duration guidance reference
ISR >25 µs    Microsoft driver-duration guidance reference
>1 ms         LatencyPilot local diagnostic bucket
>3 ms         LatencyPilot local diagnostic bucket
```

The 100/25 µs values are engineering references, not a LatencyPilot system-health score. CPU0 concentration is evidence/hypothesis, not a rule that CPU0 must be avoided.

## 13. ETW architecture

```text
Service/session control
→ kernel provider configuration
→ capture
→ event decoding
→ bounded normalized observations
→ processor attribution
→ authoritative image/module attribution
→ distributions/aggregates
→ evidence / repeated-baseline / experiment interpretation
```

Already-loaded kernel images require supported rundown/capture-state behavior. The Service returns bounded evidence rather than the raw event stream. ETW loss/invalid/event-limit provenance stays explicit and decision-grade gates fail closed.

## 14. IPC and privilege contract

Protocol-v6 public IPC is local, typed, versioned and observation-only.

- no arbitrary command names;
- no shell/process execution primitive;
- no arbitrary registry path/value primitive;
- bounded request/response frames;
- unknown JSON members fail closed;
- bounded I/O/operation deadlines;
- active capture cancels when the owning client disconnects or violates the one-request contract;
- network identities are denied;
- pipe ACL is narrowed to required local identities;
- connected client session must match the active console session;
- inability to establish session identity rejects access;
- successful capture carries the enclosing RequestId;
- protocol/version mismatch fails closed.

The observation ACL/session check is **not** mutation authorization. The four gates remain:

```text
Gate A  owner-only internal physical substrate proof; protocol v6 stays read-only
Gate B  mutation-specific typed/allowlisted IPC + mutation authorization source
Gate C  physical proof of the real client/App → Service mutation boundary
Gate D  user-facing product arming
```

## 15. Evidence schema v8

Current export contract:

```text
schema = latencypilot-evidence-v8
protocolVersion = 6
purpose = quick-diagnostic-snapshot | repeated-decision-baseline
sourceRevisionId = exact clean build revision when available
```

Observation artifacts contain one bounded capture. Baseline artifacts contain aligned captures/windows/runtime-windows plus `baseline-quality-v2` output. Serialization/file I/O happens after the authoritative measurement interval/sequence. Saved JSON is SHA-256 hashed and `scripts/Verify-Evidence.ps1` independently verifies shape/provenance/integrity.

## 16. Runtime context

Best-effort context brackets App capture requests using documented Windows APIs: system CPU busy percentage, AC/DC source/battery, Battery Saver, active power scheme and configured Windows power mode. Configured mode is preference provenance, not proof of effective instantaneous hardware power state. Optional runtime-context failure must not become a fake zero or invalidate otherwise clean DPC/ISR evidence.

Thermal context remains deferred until a trustworthy low-overhead source is identified and evidence shows it changes decisions.

## 17. Diagnostics contract

Operational diagnostics are local, structured and bounded. App and Service logs correlate through RequestId, which is also preserved in evidence. No raw per-event DPC/ISR logging and no synchronous file I/O in ETW callbacks. Logging failure must not prevent startup.

See `docs/DIAGNOSTICS.md`.

## 18. Mutation interface contract

Internal mutation mechanisms expose only narrow supported operations conceptually equivalent to:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

The public Protocol exposes none of those operations. Gate A validates them only through the owner-only non-shipping harness. Gate B may later introduce mutation-specific typed/allowlisted IPC and authorization; no command may accept arbitrary registry paths, PowerShell or arbitrary process command lines.

## 19. Persistence and recovery

Current durable invariants:

1. exact original state is persisted before an owned write;
2. journal transitions use explicit state and compare-and-swap revision checks;
3. an unresolved experiment blocks another experiment;
4. startup recovery re-reads actual machine state rather than trusting stale intent;
5. unknown or externally diverged state fails closed instead of being blindly overwritten;
6. failed/incomplete rollback remains unresolved;
7. final kept/reverted state must be verified before journal closure.

GPU affinity’s two stored values are treated as one owned logical state. The write layer now either commits/verifies the intended pair or attempts exact bounded compensation; if exact restoration cannot be proven, the journal retains recovery ownership rather than misclassifying the operation as a harmless pre-write abort.

Schema evolution must preserve active recovery records. Do not add a second storage engine or generic repository abstraction.

## 20. Internal GPU experiment architecture

The first mutation workflow does not assume that default Windows affinity, CPU0 avoidance, a community tweak or another machine’s result is universally optimal.

Current internal source path is:

```text
valid authoritative baseline
→ bounded physical-core candidates from measured pressure
→ capture exact original affinity state
→ per-candidate journaled apply + exact-target activation
→ enter Measuring
→ synchronized ETW + raw PresentMon capture
→ verify stored state before/after
→ verify effective GPU ISR placement for Candidate runs
→ exact rollback before next screening candidate
→ screening may nominate one finalist only
→ fixed ABBA + BAAB eight-run confirmation
→ AwaitingDecision only after final Candidate block
→ Keep only after final stored candidate + driver identity re-read
   otherwise exact RestoreOriginal / RecoveryRequired
```

### 20.1 Synchronized evidence

`GpuOptimizationEvidenceCollector` runs ETW and PresentMon concurrently under one deadline and requires at least 95% common overlap with the requested >=30 s interval. It retains session/workload/environment/source revision and unique capture identity.

Primary evidence is raw DPC duration samples. PresentMon contributes raw frame guardrails where exposed: CPU frame time, CPU/GPU busy/wait, GPU/display latency and dropped-frame observations. Missing optional metrics are not invented. Process/API/window mismatch, incomplete required samples, dirty identity or capture-integrity failure makes the run unusable.

### 20.2 Effective placement verification

Candidate stored registry equality is necessary but not sufficient. The same ETW capture is passed through `GpuInterruptRuntimePlacementVerifier`:

- GPU-driver ISR events are identified only through exact driver/module attribution;
- at least one attributed GPU ISR must be observed on the finalist logical processor;
- any attributed GPU ISR on an off-target processor makes that candidate run unusable;
- unresolved ISR attribution remains explicit and never counts as successful placement.

This is a source validity contract, not a claim that a hosted runner proved the owner’s physical GPU behavior.

### 20.3 Decision semantics

`GpuOptimizationDecisionEngine.Screen` can return only `ConfirmFinalist` or `RestoreOriginal` from screening. It cannot Keep directly.

`GpuOptimizationConfirmation` consumes exactly eight ABBA+BAAB runs from the same identities. Every run must have verified expected state, clean capture integrity, adequate duration and at least 1,000 samples per required metric. Guardrails remain named and visible; a material guardrail regression prevents automatic Keep. The final result preserves raw deltas, counts, noise/drift and reasons.

`GpuOptimizationOrchestrator` owns execution sequencing and journal transitions but remains internal. Protocol v6 and `MutationAvailable=false` remain unchanged until the physical gate sequence authorizes product mutation.

## 21. Permanent test strategy

The default/target permanent suite remains **10 tests**, currently **10**. The repository owner has authorized growth to at most **20** only when genuinely required to keep every test source file at or below **1200 lines** or to preserve a materially safer durable separation.

A higher count is not a quality target. Remove temporary/obsolete tests and consolidate duplication before adding permanent TestMethods. Current durable tests protect high-blast-radius lifecycle/recovery, comparison, baseline, finite-value, percentile, IPC, read-only inventory and optimizer contracts.

Temporary implementation/debug tests may be created and must be removed if they do not protect a lasting critical invariant. Hardware validation is separate from this budget.

## 22. CI and release evidence

Hosted GitHub Actions is intentionally test-only. It does not establish WinUI App/Service launch, physical ETW/device behavior, package integrity or signing.

```text
hosted Tests → deterministic/source contracts
owner-local Windows → App/Service compile + runtime + physical validation
owner-local release path → publish/package/launch smoke/checksums/signing/release
```

A physical claim must identify the exact clean source revision used to build/run the App and the exact evidence artifact/hash where applicable.

## 23. UI contract

The UI must keep exact measurements visible; distinguish diagnostic snapshot from decision baseline; distinguish reference values from local buckets; distinguish stored configuration, assigned resources and runtime behavior; expose integrity/sample inadequacy; invalidate stale evidence; remain understandable without color; support keyboard/UI Automation, High Contrast and representative text scaling; and keep the App non-elevated.

Do not add a second UI toolkit or speculative MVVM/DI/navigation framework solely for architectural fashion.

## 24. Completion discipline

Source implementation does not close a phase or arm mutation.

Phase 2 still requires its remaining physical read-only checks. Phase 3 remains physically gated:

1. **Gate A — internal physical substrate proof:** current-main App/Service compile/launch, journal/recovery inspection, controlled unresolved classification, exact-target restart/reboot-required behavior, candidate apply/runtime evidence/exact rollback and forced-failure recovery while v6 remains read-only.
2. **Gate B — typed mutation IPC:** only after Gate A; mutation-specific typed/allowlisted commands and authorization; product still unarmed.
3. **Gate C — physical IPC proof:** real App/client → Service authorization, journal ownership and recovery/rollback.
4. **Gate D — product arming:** supported one-click workflow becomes user reachable only after Gate C and credible target/guardrail UX.

Passing Gate A authorizes Gate B source work, not public mutation. `PROJECT_STATUS.md` owns the exact current execution sequence.
