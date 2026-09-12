# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-12

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
- interactive desktop user;
- local machine only;
- self-contained .NET + Windows App SDK release.

ARM64 is not a 1.0 commitment until every required dependency and hardware-validation path is demonstrated.

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
- MSTest + Microsoft.Testing.Platform for the small critical suite.

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
Core ← Benchmarking
Core ← Protocol
```

Mutation remains disabled until Phase 3 safety infrastructure exists. The Service already exists in Phase 2 because privileged kernel ETW observation must not force the WinUI process to run elevated.

## 5. Project responsibilities

### `LatencyPilot.Core`
Stable domain concepts and invariants only. No WinUI, ETW implementation details, registry paths, SQLite, P/Invoke, Windows Service hosting or machine state.

### `LatencyPilot.Benchmarking`
Percentiles, distributions, noise/drift analysis, comparisons, guardrails and verdicts. Keep deterministic and hardware-independent where possible.

### `LatencyPilot.Protocol`
Versioned IPC commands/events/DTOs/errors only. Never generic privileged execution. Phase 2 commands are observation-only and explicitly allowlisted.

### `LatencyPilot.Platform.Windows`
All raw Windows mechanisms: inventory, ETW, DPC/ISR interpretation, SetupAPI/CM, PCI/device topology, interrupt configuration/assignment, CPU topology, Raw Input, USB/xHCI, NDIS/RSS, PresentMon and narrow registry/device-policy adapters.

Raw P/Invoke and registry paths do not leave this project.

### `LatencyPilot.Persistence`
SQLite, migrations, snapshots, pending/closed journal records, benchmark history and recovery state.

### `LatencyPilot.Service`
The narrow privileged execution boundary. During Phase 2 it hosts only privileged read-only observation. During Phase 3 it may gain mutation authority only after durable journaling, validation, authorization, verification and recovery exist. It never becomes a generic scripting host.

### `LatencyPilot.App`
The non-elevated WinUI 3 experience. It presents inventory, evidence, trade-offs, decisions and recovery state. It may perform local non-privileged read-only inventory directly through `Platform.Windows`; privileged observation/mutation crosses `Protocol` to the Service.

## 6. Dependency direction

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             → Core
Platform.Windows     → Core
Persistence          → Core
Service              → Core + Protocol + Platform.Windows + Persistence
App                  → Core + Protocol + Platform.Windows
CriticalTests        → only projects needed by the current critical scenarios
```

No cyclic references. No speculative abstraction projects.

## 7. UI and deployment contract

ADR 0002 supersedes the WPF portion of ADR 0001. ADR 0003 moves the privileged Service boundary into Phase 2 for read-only kernel observation.

`LatencyPilot.App` uses WinUI 3 and Windows App SDK 2.4 Stable. Current deployment is:

```text
WindowsPackageType=None
WindowsAppSDKSelfContained=true
runtime target=win-x64
```

The artifact publishes App and Service separately under one Windows x64 artifact. The app is unpackaged. MSIX/package identity is not introduced without a separate need/decision. `PublishSingleFile` is not enabled by default because it adds extraction and publish constraints without current value.

The UI should use WinUI controls and Windows 11 interaction/accessibility conventions, but should not add a second UI toolkit or speculative MVVM/DI/navigation framework.

## 8. Observation semantics

Do not collapse different evidence levels into one field.

Examples:

- registry `MSISupported` / affinity policy = **stored configuration**;
- ConfigMgr allocated IRQ resources = **assigned resource state**;
- ETW DPC/ISR events = **runtime behavior**.

A configuration hint must not be named or displayed as proof of active interrupt delivery.

Partial device metadata is representable. A device without a readable hardware key is not equivalent to “no interrupt configuration”; preserve availability/error provenance instead of failing the entire inventory or silently inventing null semantics.

A single short ETW capture is an **observation**, not a trustworthy baseline. Baseline terminology requires repeated windows plus quality/noise/drift handling.

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

Phase 2 observation currently supports DPC/ISR count and duration distributions including p50/p95/p99/p99.9/max. Repeated baseline windows, empirical noise floor, drift detection and quality verdicts are still separate required work.

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

Phase 2 IPC is local, typed, versioned and observation-only. The current surface contains only explicit supported operations such as service status and kernel-latency observation.

- no arbitrary command name;
- no arbitrary shell/process execution;
- no arbitrary registry path/value;
- bounded request/response frames;
- bounded client/server I/O deadlines;
- remote/network access denied by the pipe security boundary;
- protocol/version mismatch fails closed.

Phase 3 mutation must extend this contract with mutation-specific authorization rather than weakening the Phase 2 observation surface.

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

If a later risk is more important, replace/merge a lower-value test. More than 10 permanent tests requires explicit owner approval plus ADR justification that remaining at 10 is more harmful.

Hardware validation and release checklists are separate and do not count toward the cap.

## 16. Release and CI contract

Main CI:

```text
restore
→ Release build
→ permanent critical tests
→ self-contained win-x64 App publish
→ self-contained win-x64 Service publish
→ combined artifact upload
```

Warnings are errors. Fix root causes instead of broad suppression.

CI proves build/package and selected invariants only; it does not prove hardware latency improvement or close physical-hardware validation items.

## 17. Hardware-validation contract

Optimization claims and hardware-dependent observation gates require physical Windows 11 evidence with enough system/app/hardware context to interpret the result. GitHub-hosted Windows Server runners can validate API/build behavior but cannot close hardware-dependent performance claims.

## 18. Mandatory step-back review

Before closing a subsection, re-review assumptions, API semantics, naming/evidence claims, partial-error behavior, resource lifetime, privilege impact, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints. Fix contradictions before calling work complete.

Every closed stage must also leave an explicit next-stage sequence in `PROJECT_STATUS.md`; a completion report without next steps is incomplete.

## 19. Architecture change rule

A new ADR is required for changes to language/runtime/UI framework, privilege model, IPC authority, persistence/recovery semantics, benchmark verdict semantics, native-code introduction, permanent-test cap, packaging identity/model, or supported OS/architecture commitment.
