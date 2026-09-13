# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-13

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

Phase 2 deliberately stops before mutation. Its job is to establish a trustworthy read-only measurement substrate that every later decision depends on.

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
- SetupAPI + Configuration Manager for PnP/resource evidence;
- documented processor-topology / CPU-set APIs;
- Raw Input for host-observable input timing;
- SQLite only when Phase 3 introduces the real durable experiment journal/recovery schema;
- MSTest + Microsoft.Testing.Platform for the capped permanent suite;
- bounded structured local diagnostics.

Native C++ is not a baseline dependency. Add it only if profiling proves a real need and an ADR records the boundary.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ WinUI 3 / normal user / non-elevated      │
│ inventory + evidence + experiment UX       │
└───────────────────┬────────────────────────┘
                    │ typed/versioned local Named Pipe
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service                       │
│ privileged boundary                        │
│ Phase 2: read-only kernel observation      │
│ Phase 3+: narrow verified mutation         │
└───────────────────┬────────────────────────┘
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Platform.Windows              │
│ ETW / SetupAPI / CM / topology             │
│ later: affinity/MSI/Raw Input/RSS/etc.     │
└───────────────────┬────────────────────────┘
                    ▼
              Windows 11 / hardware

Pure/shared layers:
Benchmarking → Core
Platform.Windows → Core
Protocol = standalone typed IPC contract
```

Phase 3 adds the concrete persistence boundary only when real SQLite journal/recovery state is implemented. There is intentionally no empty Phase 2 persistence project.

## 5. Project responsibilities

### `LatencyPilot.Core`

Stable domain concepts and invariants only. No WinUI, ETW implementation, registry paths, P/Invoke, Service hosting or SQLite.

### `LatencyPilot.Benchmarking`

Canonical percentile estimator, metric series, repeated-baseline quality, noise/drift analysis, comparisons, guardrails and verdicts. Keep deterministic and hardware-independent where possible.

### `LatencyPilot.Protocol`

Typed/versioned IPC commands, DTOs and errors only. Never a generic privileged execution surface.

Current Phase 2 contract:

```text
ProtocolVersion.Current = 6
Pipe = LatencyPilot.Observation.v6
Commands = GetStatus, CaptureKernelLatency
MutationAvailable = false
```

### `LatencyPilot.Platform.Windows`

Windows-specific mechanisms: topology, inventory, ETW, SetupAPI/CM, resource/configuration evidence and later narrow affinity/MSI/Raw Input/USB/RSS/PresentMon adapters.

Windows-specific semantics should not leak into pure Core/Benchmarking layers.

### Future Phase 3 persistence boundary

SQLite migrations, exact original-state snapshots, pending/closed journal entries, recovery state and experiment history. Create this boundary together with the concrete durable-state contract, not as placeholder scaffolding.

### `LatencyPilot.Service`

Privileged boundary. During Phase 2 it hosts privileged read-only observation only. During Phase 3 it may gain mutation authority only with mutation-specific authorization, durable journal/recovery, validation, applied-state verification and rollback.

The Service never becomes a generic scripting host.

### `LatencyPilot.App`

Normal-user WinUI experience. Local non-privileged inventory may call `Platform.Windows` directly. Privileged observation/mutation crosses typed Protocol to the Service. Repeated-baseline interpretation belongs in deterministic Benchmarking rather than duplicated UI logic.

## 6. Dependency direction

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             ← standalone
Platform.Windows     → Core
Service              → Core + Benchmarking + Protocol + Platform.Windows
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

## 9. Phase 2 measurement modes

Phase 2 has two intentionally different measurement products.

### 9.1 Quick diagnostic snapshot

```text
1 × 5 seconds
purpose = quick-diagnostic-snapshot
```

Used for:

- ETW integrity;
- CPU concentration;
- module attribution;
- local tail/reference context;
- hypothesis generation.

It is **not** a health verdict, stability proof or optimization recommendation.

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

Current DPC/ISR distributions expose:

- count;
- p50;
- p95;
- p99;
- max;
- p99.9 only when the individual distribution contains at least **10,000 samples**.

The 10,000-sample p99.9 rule is an adequacy floor, not a confidence guarantee. Below it p99.9 is absent rather than emphasized from weak tail evidence.

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

No inconvenient window may be silently deleted.

The result is `Valid` only when all five windows and both DPC/ISR p99 stability screens pass. `Valid` means repeatable enough for the current comparison method; it does **not** mean “this PC is healthy”.

Runtime CPU/power context is provenance. It can reveal mismatched conditions but does not silently alter the versioned v2 formula.

## 12. DPC/ISR reference semantics

Current UI/evidence may surface:

```text
DPC >100 µs   Microsoft driver-duration guidance reference
ISR >25 µs    Microsoft driver-duration guidance reference
>1 ms          LatencyPilot local diagnostic bucket
>3 ms          LatencyPilot local diagnostic bucket
```

The 100/25 µs values are useful engineering references, not a LatencyPilot system-health score. A single exceedance does not establish user-visible impact. A five-second snapshot with no exceedance does not establish stable latency.

CPU0 concentration is evidence/hypothesis, not a rule that CPU0 must be avoided.

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
→ evidence / repeated-baseline interpretation
```

Already-loaded kernel images require supported rundown/capture-state behavior rather than relying only on future ImageLoad events.

The Service returns bounded aggregate evidence, not the raw event stream.

ETW loss is a validity concern. Loss/invalid/event-limit provenance must remain explicit and decision-grade gates fail closed rather than treating missing evidence as zero.

## 14. IPC and privilege contract

Phase 2 protocol-v6 IPC is local, typed, versioned and observation-only.

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
- successful capture carries the enclosing `RequestId` for log/evidence correlation;
- protocol/version mismatch fails closed.

The current observation ACL/session check is **not** future mutation authorization. Phase 3 must add mutation-specific authorization without weakening the read-only contract.

## 15. Evidence schema v8

Current export contract:

```text
schema = latencypilot-evidence-v8
protocolVersion = 6
purpose = quick-diagnostic-snapshot | repeated-decision-baseline
sourceRevisionId = exact clean build revision when available
```

Observation artifacts contain one bounded capture. Baseline artifacts contain aligned captures/windows/runtime-windows plus `baseline-quality-v2` output.

Evidence serialization/file I/O happens after the authoritative measurement interval/sequence, not between baseline windows.

Saved JSON is hashed with SHA-256. `scripts/Verify-Evidence.ps1` independently verifies provenance, shape, RequestId uniqueness, p99.9 adequacy, hash and optional strict closure gates.

User-defined power-plan friendly names remain local UI context and are not required in portable evidence; stable identifiers/configured-mode provenance are sufficient.

## 16. Runtime context

Best-effort context currently brackets App capture requests using documented Windows APIs:

- system CPU busy percentage from cumulative system-time deltas;
- AC/DC source and battery state;
- Battery Saver;
- active power-scheme GUID and local UI friendly name;
- user-configured Windows power mode.

The configured power mode is the user's preference, not proof of the effective instantaneous hardware power state.

Optional runtime-context failure must not become a fake zero or invalidate otherwise clean DPC/ISR evidence.

Thermal context is deferred unless a sufficiently authoritative low-overhead source is identified and physical evidence shows it changes decisions.

## 17. Diagnostics contract

Operational diagnostics are local, structured and bounded. App and Service logs correlate through the protocol-v6 `RequestId`, which is also preserved in evidence-v8 capture objects.

No raw per-event DPC/ISR logging and no synchronous file I/O in ETW callbacks. Logging failure must not prevent App/Service startup.

See `docs/DIAGNOSTICS.md`.

## 18. Mutation interface contract

Future mutation mechanisms expose narrow operations such as:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

No IPC mutation command accepts arbitrary registry paths, PowerShell or arbitrary process command lines.

## 19. Persistence and recovery

Before the first real mutation ships:

1. create the concrete Phase 3 persistence project with real SQLite schema/migrations;
2. persist exact original state before apply;
3. journal the pending mutation before changing Windows state;
4. survive App/Service/Windows interruption;
5. on recovery, re-read actual machine state before acting;
6. do not blindly overwrite external changes;
7. verify final kept/reverted state before closing the journal.

## 20. Phase 3 GPU experiment direction

The first mutation workflow must not assume that default Windows affinity, CPU0 avoidance, a community tweak or another machine's result is universally optimal.

Required structure:

1. preserve current/default state as a control;
2. generate topology-aware physical-core candidates;
3. screen candidates in bounded fashion;
4. confirm a small finalist set using longer balanced/interleaved A/B sequences such as ABBA/BAAB;
5. combine DPC/ISR target evidence with applicable PresentMon frame-time, CPU/GPU busy/wait, GPU/display latency and dropped-frame guardrails;
6. include subsystem guardrails where the workload profile requires them;
7. Keep only a measured improvement; otherwise Revert.

PresentMon metrics are guardrails/targets where they actually observe the relevant graphics workload; they are not synthesized into a universal score.

## 21. Permanent test strategy

**Hard maximum: 10 permanent automated tests repository-wide.** Current count: **8**.

Permanent tests protect high-blast-radius contracts, not files or coverage percentages. Scenario matrices should be consolidated into durable tests where practical.

Current high-value contracts include:

- experiment-state legality;
- comparison/guardrail semantics;
- `baseline-quality-v2` duration/sample/integrity/noise/drift/sequence behavior;
- finite metric requirements;
- canonical percentile estimator;
- fail-closed protocol framing and v6 p99.9 round trip;
- read-only protocol command surface;
- real Windows read-only inventory/topology/runtime-context invariants.

Temporary implementation/debug tests may be created/run/deleted. If a future parser/recovery/mutation risk is more important, merge or replace a lower-value permanent test rather than casually exceeding the cap.

Hardware validation is separate from this cap.

## 22. CI and release evidence

Hosted GitHub Actions is intentionally test-only. It does not establish WinUI App/Service compilation, GUI launch, ETW correctness, package integrity or hardware behavior.

Current evidence split:

```text
hosted Tests → deterministic contracts
owner-local Windows → App/Service compile + runtime + physical validation
owner-local release path → publish/package/launch smoke/checksums/release
```

A physical claim must identify the exact clean source revision used to build/run the App and the exact evidence artifact/hash.

## 23. UI contract

The UI must:

- keep exact measurements visible;
- distinguish diagnostic snapshot from decision baseline;
- distinguish Microsoft reference values from LatencyPilot local buckets;
- distinguish stored configuration, assigned resources and runtime behavior;
- expose capture-integrity/sample inadequacy instead of manufacturing healthy-looking states;
- invalidate stale evidence when the measurement scenario changes;
- remain understandable without color;
- support keyboard/UI Automation, High Contrast and representative text scaling;
- keep the App non-elevated.

Do not add a second UI toolkit or speculative MVVM/DI/navigation framework solely for architectural fashion.

## 24. Completion discipline

Source implementation does not close Phase 2. Closure requires the same final clean revision to have:

- green deterministic Tests;
- owner-local App/Service compile/run;
- evidence-v8 quick snapshot integrity/provenance;
- valid Real-world five × 20-second baseline-v2;
- valid Controlled-idle five × 20-second baseline-v2;
- representative GPU/NIC/xHCI evidence;
- attribution plausibility;
- disconnect/failure/stale-ETW cleanup;
- active-console authorization sanity;
- accessibility/responsive sanity;
- zero-mutation confirmation;
- JSON/SHA/source-revision reconciliation.

Only after that exit gate closes may Phase 3 introduce persistent mutation authority.
