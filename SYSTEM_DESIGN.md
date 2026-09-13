# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-13

`ROADMAP.md` defines the final product and phase exit gates. `PROJECT_STATUS.md` records what is actually complete and the current execution ladder. ADRs record accepted architecture changes.

## 1. Purpose

LatencyPilot is a Windows 11 latency experimentation platform. It observes the machine, isolates a narrowly scoped candidate change, verifies what actually changed, compares target and collateral effects, and lets the user keep or revert the experiment.

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

For a system-changing feature:

```text
Detect applicability
→ Snapshot original state
→ Validate candidate
→ Journal pending experiment
→ Apply
→ Verify actual state
→ Benchmark
→ Classify
→ Keep or Revert
→ Verify final state
→ Close journal
```

A mutation that cannot satisfy that lifecycle is not eligible for automatic optimization.

## 2. Target platform

Initial supported target:

- Windows 11;
- x64;
- active local interactive desktop session;
- local machine only;
- self-contained .NET + Windows App SDK release.

The current Phase 2 privileged observation pipe authorizes only the active console session after the connection is accepted. RDP/multi-session behavior is not an implied supported path and must be deliberately validated/designed before being broadened. ARM64 is not a 1.0 commitment until every required dependency and hardware-validation path is demonstrated.

## 3. Frozen technology baseline

Unless an ADR explicitly changes it:

- C# 14;
- .NET 10 LTS;
- WinUI 3;
- Windows App SDK 2.4 Stable;
- unpackaged application with self-contained Windows App SDK runtime;
- Windows Service as the narrow privileged boundary for Phase 2 kernel observation and later Phase 3 mutation;
- versioned Named Pipes for local IPC;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`;
- PresentMon for graphics/frame telemetry where required;
- SetupAPI + Configuration Manager for PnP/device discovery;
- CPU Sets / processor-topology APIs;
- Raw Input for host-side input-report timing;
- SQLite for durable experiment/recovery state;
- MSTest + Microsoft.Testing.Platform for the small critical suite;
- `Microsoft.Extensions.Logging` plus bounded local Serilog file sinks for operational diagnostics.

Native C++ is not a baseline dependency. A native component requires profiling evidence and an ADR.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ WinUI 3 / normal user / non-elevated      │
│ local read-only inventory + evidence UX    │
│ baseline/experiment/decision UX            │
└───────────────────┬────────────────────────┘
                    │ typed/versioned local Named Pipe
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service                       │
│ privileged boundary                        │
│ Phase 2: read-only kernel observation      │
│ Phase 3+: validated mutation + recovery    │
└───────────────┬────────────────────────────┘
                │
       ┌────────┴───────────────────┐
       ▼                            ▼
┌─────────────────────────┐  ┌─────────────────────────┐
│ Platform.Windows        │  │ Persistence             │
│ ETW / SetupAPI / CM     │  │ SQLite                  │
│ MSI / affinity          │  │ snapshots + journal     │
│ CPU / Raw Input / USB   │  │ benchmark history       │
│ PresentMon / RSS        │  │ recovery records        │
└────────────┬────────────┘  └─────────────────────────┘
             │
             ▼
       Windows 11 / hardware

Pure/shared layers:
Benchmarking → Core
Platform.Windows → Core
Protocol (standalone typed IPC contract)
Persistence (reserved, dependency-free Phase 2 boundary)
```

Mutation remains disabled until Phase 3 safety infrastructure exists. The Service already exists in Phase 2 because privileged kernel ETW observation must not force the WinUI process to run elevated. The Persistence box is the accepted Phase 3 boundary; it is intentionally dependency-free and not wired into the Phase 2 Service graph yet.

## 5. Project responsibilities

### `LatencyPilot.Core`
Stable domain concepts and invariants only. No WinUI, ETW implementation details, registry paths, SQLite, P/Invoke, Windows Service hosting or machine state.

### `LatencyPilot.Benchmarking`
Percentiles, distributions, repeated-baseline quality, noise/drift analysis, comparisons, guardrails and verdicts. Keep deterministic and hardware-independent where possible.

### `LatencyPilot.Protocol`
Versioned IPC commands/events/DTOs/errors only. Never generic privileged execution. Phase 2 commands are observation-only and explicitly allowlisted. The project is intentionally standalone and must not acquire domain dependencies unless the wire contract actually requires them.

### `LatencyPilot.Platform.Windows`
All raw Windows mechanisms: inventory, ETW, DPC/ISR interpretation, SetupAPI/CM, PCI/device topology, interrupt configuration/assignment, CPU topology, Raw Input, USB/xHCI, NDIS/RSS, PresentMon and narrow registry/device-policy adapters.

Raw P/Invoke and registry paths do not leave this project except for narrowly scoped Windows-host security/lifecycle calls that belong directly to the Service boundary (for example named-pipe client-session authorization).

### `LatencyPilot.Persistence`
Reserved Phase 3 boundary for SQLite, migrations, snapshots, pending/closed journal records, benchmark history and recovery state. It remains intentionally empty and dependency-free in Phase 2 rather than carrying placeholder helpers or speculative references with no active persistence contract.

### `LatencyPilot.Service`
The narrow privileged execution boundary. During Phase 2 it hosts only privileged read-only observation. During Phase 3 it may gain mutation authority only after durable journaling, validation, authorization, verification and recovery exist. It never becomes a generic scripting host.

### `LatencyPilot.App`
The non-elevated WinUI 3 experience. It presents inventory, evidence, trade-offs, decisions and recovery state. It may perform local non-privileged read-only inventory directly through `Platform.Windows`; privileged observation/mutation crosses `Protocol` to the Service. Repeated-baseline interpretation is delegated to deterministic `Benchmarking` logic rather than duplicated in the UI.

## 6. Dependency direction

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             ← no project dependency
Platform.Windows     → Core
Persistence          ← no project dependency in Phase 2
Service              → Core + Benchmarking + Protocol + Platform.Windows
App                  → Benchmarking + Protocol + Platform.Windows
CriticalTests        → only projects needed by the current critical scenarios
```

The Service and App depend on `Benchmarking` only for shared deterministic evidence/statistics semantics. They must not duplicate layer-specific percentile, noise or drift interpretations. `Protocol` stays independent because its wire DTOs/framing currently need no Core types. `Persistence` stays dependency-free until Phase 3 durable journaling/recovery introduces a demonstrated dependency. The App does not carry a redundant direct Core reference when its active features are already expressed through Benchmarking and Platform.Windows boundaries.

No cyclic references. No speculative abstraction projects. Do not retain project references merely for possible future work.

## 7. UI and deployment contract

ADR 0002 supersedes the WPF portion of ADR 0001. ADR 0003 moves the privileged Service boundary into Phase 2 for read-only kernel observation.

`LatencyPilot.App` uses WinUI 3 and Windows App SDK 2.4 Stable. Current deployment is:

```text
WindowsPackageType=None
WindowsAppSDKSelfContained=true
runtime target=win-x64
```

The release artifact publishes App and Service separately under one Windows x64 distribution. The app is unpackaged. MSIX/package identity is not introduced without a separate need/decision. `PublishSingleFile` is not enabled by default because it adds extraction and publish constraints without current value.

Installer deployments place App and Service under the protected Program Files tree. Portable distributions may place the normal-user App in a user-controlled extraction directory, but an elevated `Install-Service.ps1` copies the privileged Service payload to `%ProgramFiles%\LatencyPilot\Service` before LocalSystem registration. A LocalSystem service must not execute from an ordinary user-writable portable extraction path.

The UI should use WinUI controls and Windows 11 interaction/accessibility conventions, but should not add a second UI toolkit or speculative MVVM/DI/navigation framework. Stable reusable visual trees should prefer XAML/UserControl composition over large imperative code-built trees when doing so reduces lifecycle and maintenance complexity without introducing a framework.

## 8. Observation semantics

Do not collapse different evidence levels into one field.

Examples:

- registry `MSISupported` / affinity policy = **stored configuration**;
- ConfigMgr allocated IRQ resources = **assigned resource state**;
- ETW DPC/ISR events = **runtime behavior**.

A configuration hint must not be named or displayed as proof of active interrupt delivery.

Partial device metadata is representable. A device without a readable hardware key is not equivalent to “no interrupt configuration”; preserve availability/error provenance instead of failing the entire inventory or silently inventing null semantics. Optional property/resource failures should degrade that device to partial evidence where safe; only an actual inventory-enumeration failure should normally abort the whole snapshot.

A single short ETW capture is an **observation**, not a trustworthy baseline. Baseline terminology requires repeated windows plus explicit capture-integrity, sample-adequacy, noise and drift handling.

Microsoft driver guidance thresholds (currently 100 µs DPC and 25 µs ISR) may be labeled as guidance. Additional `>1 ms` / `>3 ms` counts are local diagnostic tail buckets only and must not be represented as official Windows pass/fail or user-impact severity boundaries.

## 9. Experiment lifecycle

The initial state machine is deliberately small:

```text
Planned
→ MeasuringBaseline
→ CandidateApplied
→ MeasuringCandidate
→ AwaitingDecision
→ Kept | Reverted
```

When persistent mutation/recovery arrives, extend states to distinguish snapshot persisted, apply pending, applied/unverified, verified, benchmark complete, revert pending and recovery required. Never infer safety state from nullable fields.

## 10. Measurement contract

Current comparison semantics include finite samples, deterministic percentile calculation, minimum sample policy, minimum relative change/noise threshold, metric direction, guardrails and explicit `Inconclusive` behavior.

The canonical percentile estimator is owned by `LatencyPilot.Benchmarking.Statistics.Percentiles`. For sorted samples it uses linear interpolation at zero-based position:

```text
position = (sampleCount - 1) * percentile
```

between the surrounding samples. Service observation summaries, baseline quality and benchmark comparisons must use this same estimator; introducing a second nearest-rank or layer-specific percentile implementation is prohibited unless the methodology is intentionally versioned and documented.

Phase 2 observation supports DPC/ISR count and duration distributions including p50/p95/p99/max. p99.9 is exposed only when the individual distribution has at least 1,000 samples; otherwise it is explicitly absent rather than presenting a mathematically available but under-supported extreme percentile as strong evidence.

`baseline-quality-v1` adds the first conservative repeated-baseline gate:

- exactly five sequential five-second windows are requested by the current App flow;
- authoritative ordering is the contiguous `WindowNumber` sequence, while UTC timestamps are provenance and are not treated as a monotonic clock;
- a metric window requires at least 20 DPC/ISR events and a finite positive p99 value;
- any unavailable or non-zero ETW loss count, invalid latency/image event, or event-limit hit makes that capture window invalid;
- per-metric normal variability is summarized as `(P90 - P10) / |median|` across eligible window-level p99 values;
- variability above 30% is inconclusive;
- drift compares early and late window medians relative to the overall median; above 20% is inconclusive;
- a window more than 50% away from the median is explicitly reported as extreme and is never silently discarded;
- all required windows and both DPC/ISR p99 metrics must pass for the result to become `Valid`; otherwise the baseline is `Inconclusive`.

These values are a versioned quality gate, not a claim of statistical significance. Physical evidence may justify revising them in a later methodology version. Background-load and thermal/power quality signals remain open until authoritative, sufficiently low-overhead evidence is chosen.

A neutral target cannot hide a regressed guardrail; collateral regression must remain visible in the verdict.

## 11. ETW architecture

```text
service/session control
→ kernel provider configuration
→ capture
→ event decoding
→ normalized observations
→ processor attribution
→ authoritative image/module attribution
→ distributions/aggregates
→ repeated-baseline evidence
```

DPC/ISR analysis must preserve enough provenance to identify processor and module/driver where provider data allows it. Unknown versions or unresolved addresses must not be silently reinterpreted.

For native routine addresses, module attribution requires authoritative image ranges. Kernel ImageLoad evidence must include modules that were already loaded before the observation, using supported rundown/CAPTURE_STATE behavior. If that evidence is absent or ambiguous, preserve the raw address and mark the module unknown.

Raw event sets are not transferred wholesale through IPC. The privileged Service should perform bounded normalization/aggregation and return typed evidence needed by the UI/benchmarking layers.

## 12. IPC and privilege contract

Phase 2 protocol v5 IPC is local, typed, versioned and observation-only. The current surface contains only explicit supported operations such as service status and kernel-latency observation.

- no arbitrary command name;
- no arbitrary shell/process execution;
- no arbitrary registry path/value;
- bounded request/response frames;
- JSON framing rejects unknown members rather than silently accepting a wider contract;
- bounded client/server I/O deadlines;
- an active capture is cancelled when its client disconnects or violates the one-request connection contract;
- remote/network identities are denied by the pipe security boundary;
- the pipe ACL admits local interactive identities plus required service identities, then the Service fail-closes unless the connected client session matches the active console session;
- failure to resolve client session identity is a rejection, not a fallback to broad access;
- each successful capture carries the same `RequestId` as its enclosing response so exported evidence can correlate directly to App/Service diagnostics;
- protocol/version mismatch fails closed.

The current active-console rule deliberately narrows Phase 2 local observation. It does not establish RDP/multi-session support. The current Phase 2 ACL/session check is **not** mutation authorization. Phase 3 mutation must extend this contract with narrower mutation-specific authorization/allowlisting rather than reusing or weakening the observation surface.

## 13. Mutation interface contract

Each mutation mechanism should expose narrow concepts such as:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

No IPC command accepts arbitrary registry paths, PowerShell or process command lines.

## 14. Persistence and recovery

Before the first real mutation ships:

1. original state is durable before apply;
2. incomplete experiments survive app/service/Windows interruption;
3. recovery re-reads actual machine state before action;
4. external changes are not overwritten blindly;
5. Keep/Revert final state is verified before journal closure.

## 15. Permanent test strategy

**Maximum: 10 permanent automated tests repository-wide.**

No coverage-percentage target. No test-per-file policy. Permanent tests protect only high-blast-radius correctness/safety contracts. Temporary implementation/debug tests may be created and removed before finalization.

Prefer a small portfolio of:

- deterministic domain/statistics contract tests;
- protocol framing and privilege-surface safety contracts;
- one or a few Windows integration invariants that exercise real read-only APIs;
- data/scenario matrices consolidated inside a durable high-value test rather than one permanent test per branch.

The repeated-baseline quality gate is a high-blast-radius safety contract because accepting a noisy/lossy baseline would invalidate all later optimization decisions; one consolidated permanent scenario test protects stable, drifted, capture-loss, sequence-gap and wall-clock-adjustment cases.

The protocol/framing contract also carries v5 correlation and p99.9-adequacy round-trip scenarios inside its existing test method rather than consuming another permanent slot.

If a later parser/recovery/mutation risk is more important, replace/merge a lower-value test. More than 10 permanent tests requires explicit owner approval plus ADR justification that remaining at 10 is more harmful.

Hardware validation and release checklists are separate and do not count toward the cap.

## 16. Diagnostics contract

Operational logging is local, structured and bounded. App and Service write separate compact-JSON rolling files and correlate request/capture activity through the protocol `RequestId`. Protocol v5 preserves that ID in capture evidence too. Expected kernel-capture unavailability and rejected client-session access must retain bounded structured failure provenance. Logging is asynchronous so file I/O does not run in the ETW callback path.

Do not emit one log event per raw DPC/ISR event, dump arbitrary registry/environment state, or treat operational logs as benchmark persistence. Logging failure must not prevent App/Service startup. See `docs/DIAGNOSTICS.md` for paths, retention, event IDs and privacy rules.

## 17. Release and CI contract

GitHub Actions is intentionally a deterministic **test-only** gate:

```text
checkout
→ pinned .NET SDK
→ NuGet cache
→ dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Hosted CI does **not** build or publish the WinUI App/Service, perform GUI launch smoke, build Setup/portable distributions, upload production binaries or publish releases. App/Service compile evidence belongs to the owner-local Windows path, matching the repository policy in `AGENTS.md`.

Owner-local Windows validation owns build/publish/package/runtime evidence:

```text
local Debug/F5 or dev.ps1 for normal iteration
→ owner-local App/Service build
→ owner-local self-contained App/Service publish
→ owner-local PRI and App launch-smoke validation
→ owner-local Setup + portable creation/publication
```

The explicit owner-run publication flow is documented in `docs/RELEASING.md` and implemented by `scripts/Publish-Release.ps1`. Warnings remain errors; fix root causes instead of broad suppression.

Hosted Tests evidence proves only the selected deterministic/integration contracts exercised by that suite. Owner-local Windows build/publish/package evidence proves buildability and the releasable distribution for the tested revision. Neither alone proves hardware latency improvement or closes physical-hardware validation items.

## 18. Hardware-validation contract

Optimization claims and hardware-dependent observation gates require physical Windows 11 evidence with enough system/app/hardware context to interpret the result. GitHub-hosted Windows runners supply only the repository's selected automated-test evidence and cannot close App/Service build, publish/package/runtime, or physical-hardware performance claims.

## 19. Mandatory step-back review

Before closing a subsection, re-review assumptions, API semantics, naming/evidence claims, partial-error behavior, resource lifetime, privilege impact, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints. Fix contradictions before calling work complete.

Every closed stage must also leave an explicit next-stage sequence in `PROJECT_STATUS.md`; a completion report without next steps is incomplete.

## 20. Architecture change rule

A new ADR is required for changes to language/runtime/UI framework, privilege model, IPC authority, persistence/recovery semantics, benchmark verdict semantics, native-code introduction, permanent-test cap, packaging identity/model, or supported OS/architecture commitment.
