# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-13

`ROADMAP.md` defines required product/phase outcomes. `PROJECT_STATUS.md` records current evidence and the execution ladder. ADRs record accepted architecture changes.

## 1. Purpose

LatencyPilot is a Windows 11 latency experimentation platform:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

A system-changing feature is incomplete unless it can follow:

```text
Detect applicability
→ Snapshot exact original state
→ Validate candidate
→ Journal pending experiment
→ Apply one change
→ Verify actual state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
→ Close journal
```

Phase 2 deliberately stops before mutation and establishes the measurement substrate that future decisions depend on.

## 2. Target platform

Initial supported target:

- Windows 11 x64;
- active local interactive desktop session;
- local machine only;
- self-contained .NET / Windows App SDK deployment.

The current privileged observation path authorizes the active console session. RDP/multi-session support is not implied. ARM64 is not a 1.0 commitment until dependencies and physical validation exist.

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
- SetupAPI + Configuration Manager for PnP/device evidence;
- processor-topology / CPU-set APIs;
- Raw Input for host-observable input timing;
- SQLite for durable mutation journal/history when Phase 3 actually begins;
- MSTest + Microsoft.Testing.Platform for the capped critical suite;
- bounded structured local diagnostics.

Native C++ is not a baseline dependency. Add it only with profiling evidence and an ADR.

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
Protocol (standalone typed IPC contract)
```

Phase 3 adds the concrete persistence boundary when the real SQLite journal/recovery schema is introduced. There is intentionally no empty Phase 2 persistence project.

## 5. Project responsibilities

### `LatencyPilot.Core`
Stable domain concepts/invariants only. No WinUI, ETW implementation, registry paths, P/Invoke, Service hosting or SQLite.

### `LatencyPilot.Benchmarking`
Canonical percentiles, distributions, repeated-baseline quality, noise/drift, comparisons, guardrails and verdicts. Hardware-independent where possible.

### `LatencyPilot.Protocol`
Typed/versioned IPC commands, DTOs and errors only. Never a generic privileged execution surface.

Current read-only contract:

```text
ProtocolVersion.Current = 6
Pipe = LatencyPilot.Observation.v6
Commands = GetStatus, CaptureKernelLatency
MutationAvailable = false
```

### `LatencyPilot.Platform.Windows`
Raw Windows mechanisms: topology, inventory, ETW, SetupAPI/CM, resource/configuration evidence and later narrow affinity/MSI/Raw Input/USB/RSS/PresentMon adapters.

Windows-specific semantics should not leak into pure Core/Benchmarking layers.

### Future Phase 3 persistence boundary
SQLite migrations, exact original-state snapshots, pending/closed journal entries, recovery state and experiment history. Create this boundary only together with the concrete durable-state contract.

### `LatencyPilot.Service`
The privileged boundary. During Phase 2: privileged read-only observation only. During Phase 3: mutation authority may be added only with mutation-specific authorization, durable journal/recovery, validation, apply verification and rollback.

The Service never becomes a generic scripting host.

### `LatencyPilot.App`
Normal-user WinUI experience. Local non-privileged inventory may call `Platform.Windows` directly. Privileged observation/mutation crosses typed Protocol to the Service. Repeated-baseline interpretation belongs in deterministic Benchmarking, not duplicated in UI code.

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

No cycles and no speculative abstraction projects.

## 7. UI/deployment contract

ADR 0002 supersedes historical WPF with WinUI 3. ADR 0003 establishes the privileged read-only Service in Phase 2.

Current App deployment:

```text
WindowsPackageType=None
WindowsAppSDKSelfContained=true
RuntimeIdentifier=win-x64
```

The App stays non-elevated. Installer/portable Service installation places the privileged Service payload under the protected Program Files tree before LocalSystem registration.

Do not add a second UI toolkit or speculative MVVM/navigation/DI framework solely for architectural fashion.

## 8. Evidence semantics

Evidence layers remain distinct:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Examples:

- registry MSI/affinity fields = stored configuration;
- ConfigMgr resource assignment = allocated resource evidence;
- ETW DPC/ISR = runtime behavior.

A stored hint must not be named/displayed as proof of active delivery state. Optional device metadata failures degrade to partial evidence rather than inventing meaning or invalidating the entire inventory.

Native routine addresses remain unresolved unless authoritative image-range evidence maps them. Already-loaded images require image rundown/lifetime handling; do not guess module identity from an address alone.

## 9. Measurement hierarchy

The system deliberately distinguishes a fast diagnostic capture from decision-grade evidence.

### Quick diagnostic snapshot

```text
1 × 5 s
purpose = quick-diagnostic-snapshot
```

Use for:

- ETW integrity;
- attribution;
- CPU concentration;
- obvious tail buckets;
- hypothesis generation.

Do not emit a system-health/stability/optimization verdict from one quick snapshot.

### Repeated decision baseline

Current implemented method:

```text
method = baseline-quality-v2
purpose = repeated-decision-baseline
5 s observer/service settle
5 × 20 s authoritative windows
750 ms inter-window settle
```

The five-second pre-sequence delay is not workload warm-up. Real-world/before-after workloads must already be warmed/repeatable unless startup behavior is deliberately the subject.

Each window requires:

- requested duration >=20,000 ms;
- actual duration >=95% of request;
- clean capture integrity;
- >=1,000 DPC events;
- >=1,000 ISR events;
- finite positive DPC/ISR p99.

Stability policy:

- P10–P90 relative spread <=30%;
- early/late relative drift <=20%;
- no >50% extreme-window deviation;
- exactly five contiguous `WindowNumber` values;
- no silent outlier/window deletion.

`Valid` means repeatable enough for this versioned comparison contract, not “healthy” or “optimal”.

### Controlled experiment

Future mutation decisions require control/candidate repetition. Prefer balanced/interleaved ordering such as ABBA/BAAB where practical. Default/current Windows state remains a valid control; a non-default candidate must win on local measured target + guardrail evidence.

## 10. Percentile/sample semantics

All percentile layers use the canonical estimator in `LatencyPilot.Benchmarking.Statistics.Percentiles`:

```text
position = (n - 1) * p
linear interpolation between surrounding sorted samples
```

Current protocol response exposes:

- count;
- p50;
- p95;
- p99;
- p99.9 when adequate;
- max.

Protocol v6 exposes p99.9 only when the individual distribution has at least **10,000 samples**. This is an adequacy floor, not a formal confidence guarantee.

The broader product methodology still expects mean/p90/dispersion/outlier measures where later experiment layers need them. The narrow Phase 2 DTO is not the final analytics model.

## 11. DPC/ISR reference semantics

Current display may retain:

- DPC `>100 µs`: Microsoft driver-duration guidance reference;
- ISR `>25 µs`: Microsoft driver-duration guidance reference;
- `>1 ms` / `>3 ms`: LatencyPilot local diagnostic buckets.

These are not the optimizer objective and not a Windows/system-health score.

A global exceedance percentage can move because the denominator changes, so later candidate ranking must use distributions, module attribution, CPU concentration, repeated-run stability and workload-specific guardrails rather than one reference-line rate.

CPU0 concentration is an observation/hypothesis. It is not a hard fault or automatic candidate exclusion.

## 12. ETW architecture

```text
Service session control
→ kernel provider/session configuration
→ capture
→ decoding/normalization
→ processor attribution
→ image/module attribution
→ bounded distributions/aggregates
→ App/evidence
→ repeated-baseline analysis
```

Rules:

- ETW loss/invalid evidence is preserved;
- an event-limit hit is not silently treated as complete evidence;
- raw event sets are not transferred wholesale over IPC;
- unresolved mappings stay unresolved;
- no raw per-event diagnostic file logging in the measurement hot path.

## 13. Evidence schema/provenance

Current evidence schema:

```text
latencypilot-evidence-v8
```

Purpose is explicit:

```text
quick-diagnostic-snapshot
repeated-decision-baseline
```

Evidence carries source/product/protocol provenance, scenario, RequestIds, bounded environment/runtime context and capture/baseline data.

A clean owner-local build may claim its exact commit via `BUILD_INFO.txt`. A dirty working tree intentionally does not claim an exact authoritative clean revision.

After save, the App computes SHA-256. `scripts/Verify-Evidence.ps1` independently enforces v8/v2 envelope, source revision, RequestId uniqueness, p99.9 sample semantics, capture integrity and decision-baseline closure invariants.

## 14. Runtime context

Best-effort runtime context includes:

- system CPU busy percentage;
- AC/DC source;
- battery/Battery Saver state where applicable;
- active power scheme identifier;
- Windows configured power mode.

Runtime context is provenance. Missing optional context does not become fake zero and does not automatically invalidate clean DPC/ISR evidence. Power-state changes remain visible for later drift reasoning.

Thermal telemetry is not added merely because it might be useful; use an authoritative low-overhead source only when physical evidence justifies it.

## 15. IPC/privilege contract

Protocol v6 is local, typed, bounded and observation-only.

- no arbitrary command names;
- no shell/process execution;
- no arbitrary registry path/value;
- bounded request/response frames;
- unknown JSON members fail closed;
- bounded client/server deadlines;
- disconnect cancels active work;
- network identities denied;
- local interactive ACL plus active-console-session check;
- inability to resolve required client session identity fails closed;
- successful capture RequestId matches response/evidence/log correlation;
- protocol mismatch fails closed.

This observation authorization is not mutation authorization. Phase 3 adds a narrower write surface rather than weakening/reusing observation assumptions.

## 16. Future mutation contract

Narrow mutation mechanisms should expose concepts such as:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

No mutation command accepts arbitrary PowerShell, process command lines or registry paths.

For GPU affinity specifically:

- preserve default/current state as control;
- use processor-group and physical-core topology;
- treat SMT siblings deliberately;
- use CPU concentration only as candidate-prior evidence;
- screen bounded candidates;
- confirm finalists with longer balanced repeated runs;
- combine DPC/ISR evidence with applicable PresentMon frame/CPU/GPU/latency metrics;
- evaluate subsystem guardrails;
- Keep only on reproducible improvement, else Revert.

## 17. Persistence/recovery

Before the first real mutation:

1. create the actual Phase 3 persistence project/schema/migrations;
2. persist original state before apply;
3. persist experiment stage so interruption/reboot is recoverable;
4. re-read actual machine state during recovery;
5. never overwrite external changes blindly;
6. verify final kept/reverted state before closing the journal.

## 18. Test strategy

Permanent automated tests have a repository-wide hard maximum of **10**; current count is **8**.

Tests protect high-blast-radius contracts, not line coverage. Consolidate scenario matrices inside durable tests when readable. Temporary investigative tests may be created/executed/deleted.

Current high-value contracts include:

- experiment state legality;
- verdict + guardrail semantics;
- `baseline-quality-v2` stable/drifted/dirty/too-short/undersampled/sequence cases;
- non-finite measurement rejection;
- canonical percentile rule;
- fail-closed protocol framing/correlation;
- read-only protocol surface;
- Windows inventory/topology/runtime-context consistency.

Physical hardware validation remains separate from automated tests.

## 19. Diagnostics contract

Operational logging is local, structured and bounded. App and Service logs correlate request/capture activity through protocol v6 `RequestId` values.

Expected capture/session rejection paths preserve bounded failure provenance. Logging must not become the measurement workload: no per-event DPC/ISR file writes.

See `docs/DIAGNOSTICS.md`.

## 20. Observer effect

LatencyPilot can perturb what it measures. Avoid unnecessary allocation, GC pressure, synchronous I/O and detailed UI redraws inside authoritative windows.

Repeated baseline intentionally avoids full detailed redraws between windows. Optimization of the observer itself requires profiling evidence; performance work must not silently change event/statistical meaning.

## 21. Release/validation boundary

Hosted CI is intentionally test-only. It does not prove WinUI compile, Service execution, ETW correctness or physical behavior.

Phase 2 closes only with owner-local physical Windows evidence on one exact clean source revision, as defined in `docs/PHYSICAL_VALIDATION.md` and `PROJECT_STATUS.md`.

Release packaging/signing is a separate later gate; a local physical source run is not automatically release-ready.

## 22. Security invariants

The privileged boundary must never expose:

- generic shell execution;
- arbitrary process launch;
- arbitrary registry write;
- arbitrary file write;
- unauthenticated remote access;
- unconstrained device policy mutation.

Observation remains read-only through Phase 2. Mutation requires durable rollback/recovery plus explicit authorization and verification.

## 23. Completion discipline

A green build/test does not prove visual quality or physical measurement correctness. A clean five-second snapshot does not prove stability. A Valid repeated baseline does not prove a future candidate is better. A favorable primary metric does not hide a regressed guardrail.

Every completion claim should use the smallest current evidence that could falsify it and stop once the requested gate is actually proven.
