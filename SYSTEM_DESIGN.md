# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-12

`ROADMAP.md` defines the final product and phase exit gates. `PROJECT_STATUS.md` records what is actually complete. This document defines how the system is structured so those goals can be implemented safely.

## 1. Purpose

LatencyPilot is a Windows 11 latency experimentation platform. It observes the machine, isolates a narrowly scoped candidate change, verifies what actually changed, compares target and collateral effects, and lets the user keep or revert the experiment.

It is not a generic optimizer or tweak pack. The central claim is not that a setting is universally faster; it is that LatencyPilot can determine whether a supported setting measurably helps a particular system and workload without hiding trade-offs.

The product loop is:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

For a system-changing feature the safety loop is:

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
- self-contained .NET release.

The Windows target framework is allowed to compile against a newer Windows SDK while `SupportedOSPlatformVersion` remains Windows 11 21H2 (`10.0.22000.0`) unless a later API requirement is intentionally introduced.

ARM64 is not a 1.0 commitment until every required dependency and hardware-validation path is demonstrated.

## 3. Frozen technology baseline

Unless an ADR explicitly changes it:

- C# 14;
- .NET 10 LTS;
- WPF;
- Windows Service for privileged operations once mutations are implemented;
- versioned Named Pipes for local IPC;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent` for kernel/subsystem observation;
- PresentMon where graphics/frame telemetry is required;
- SetupAPI + Configuration Manager for PnP/device discovery;
- CPU Sets / processor-topology APIs;
- Raw Input for host-side input-report timing;
- SQLite for durable experiment/recovery state when persistence is introduced;
- MSTest + Microsoft.Testing.Platform for the small critical automated suite.

Native C++ is not a baseline dependency. A native component requires profiling evidence and an ADR showing why managed code is insufficient.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ WPF / normal user / non-elevated          │
│                                            │
│ inventory + baseline UX                    │
│ experiment setup                           │
│ evidence / trade-offs                      │
│ keep / revert / recovery UX                │
└───────────────────┬────────────────────────┘
                    │ versioned Named Pipe
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service                       │
│ privileged boundary                        │
│                                            │
│ authorization + validation                 │
│ mutation orchestration                     │
│ journal/recovery coordination              │
└───────────────┬────────────────────────────┘
                │
       ┌────────┴───────────────────┐
       ▼                            ▼
┌─────────────────────────┐  ┌─────────────────────────┐
│ Platform.Windows        │  │ Persistence             │
│                         │  │                         │
│ ETW                     │  │ SQLite                  │
│ SetupAPI / CM           │  │ snapshots               │
│ MSI / affinity          │  │ experiment journal      │
│ CPU topology            │  │ benchmark history       │
│ Raw Input / USB         │  │ recovery records        │
│ PresentMon              │  │ migrations              │
│ RSS/network             │  │                         │
└────────────┬────────────┘  └─────────────────────────┘
             │
             ▼
       Windows 11 / hardware

Pure/shared domain layers:

Core ← Benchmarking
Core ← Protocol
```

Phase 1 intentionally keeps `ServiceBoundary.MutationAvailable = false`; no system mutation is allowed before the safety substrate in Phase 3 exists.

## 5. Project responsibilities

### `LatencyPilot.Core`

Owns stable domain concepts and invariants:

- experiment states and legal transitions;
- metric definitions and direction;
- validated measurement-series contracts;
- verdict/result types;
- stable system/device domain models;
- recovery/safety invariants as they are introduced.

It must not depend on WPF, ETW implementation details, registry paths, SQLite, P/Invoke, Windows Service hosting or machine-specific state.

### `LatencyPilot.Benchmarking`

Owns evidence interpretation:

- percentile/distribution calculations;
- before/after comparison;
- minimum-sample policy;
- noise-floor/drift logic as it is introduced;
- target versus guardrail handling;
- explicit verdict classification;
- later confidence/resampling logic when justified by actual benchmark design.

This layer must remain deterministic enough to exercise without physical hardware.

### `LatencyPilot.Protocol`

Owns versioned local IPC contracts:

- protocol version;
- command/event envelopes;
- DTOs and structured error contracts as required.

It must never expose arbitrary shell, registry, file or process execution.

### `LatencyPilot.Platform.Windows`

Owns all raw Windows mechanisms:

- OS/CPU/device inventory;
- ETW session control/parsing;
- DPC/ISR interpretation;
- SetupAPI / Configuration Manager;
- PCI and device topology;
- interrupt-affinity policy;
- MSI/MSI-X inspection/mutation where supported;
- CPU topology / CPU Sets;
- Raw Input;
- USB/xHCI telemetry;
- NDIS/RSS/network integration;
- PresentMon adapters;
- narrow registry/device-policy implementation details.

Raw P/Invoke and registry paths must not leak out of this project.

### `LatencyPilot.Persistence`

Owns durable local state once persistence is introduced:

- SQLite and migrations;
- experiment snapshots;
- pending/closed journal records;
- benchmark history;
- recovery state;
- diagnostic metadata.

Original state required for rollback must be durable before a mutation is attempted.

### `LatencyPilot.Service`

Owns privileged execution once Phase 3 begins:

- Windows Service host;
- local IPC listener;
- client authorization;
- server-side validation;
- mutation orchestration;
- recovery execution;
- service-side diagnostics.

The service is not a generic privileged scripting host.

### `LatencyPilot.App`

Owns the non-elevated WPF experience:

- system/baseline views;
- experiment setup;
- raw evidence and trade-offs;
- Keep/Revert decisions;
- recovery/history UX;
- non-privileged preferences.

The app must never directly mutate privileged Windows state.

## 6. Dependency direction

Keep dependencies narrow:

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             → Core
Platform.Windows     → Core
Persistence          → Core
Service              → Core + Protocol + Platform.Windows + Persistence
App                  → Core + Platform.Windows now
App (future mutate)  → Protocol client; never raw privileged mutation
CriticalTests        → only projects required by current critical scenarios
```

Avoid cyclic references and speculative abstraction projects.

## 7. Repository structure

```text
LatencyPilot/
├── LatencyPilot.slnx
├── global.json
├── Directory.Build.props
├── .editorconfig
├── .gitignore
├── README.md
├── LICENSE
├── CLA.md
├── CONTRIBUTING.md
├── SECURITY.md
├── AGENTS.md
├── SYSTEM_DESIGN.md
├── ROADMAP.md
├── PROJECT_STATUS.md
│
├── src/
│   ├── LatencyPilot.Core/
│   ├── LatencyPilot.Benchmarking/
│   ├── LatencyPilot.Protocol/
│   ├── LatencyPilot.Platform.Windows/
│   ├── LatencyPilot.Persistence/
│   ├── LatencyPilot.Service/
│   └── LatencyPilot.App/
│
├── tests/
│   └── LatencyPilot.CriticalTests/
│
├── docs/
│   ├── BENCHMARK_METHODOLOGY.md
│   └── adr/
│
└── .github/
    ├── workflows/
    ├── ISSUE_TEMPLATE/
    ├── pull_request_template.md
    └── CODEOWNERS
```

Do not create empty folder trees or one test project per production project merely to mirror architecture. Add a directory/tool only when the implementation needs it.

## 8. Experiment lifecycle

The experiment state machine is explicit. Phase 1 begins with a deliberately small set:

```text
Planned
→ MeasuringBaseline
→ CandidateApplied
→ MeasuringCandidate
→ AwaitingDecision
→ Kept | Reverted
```

Failure/termination may lead to `Aborted` where no unresolved mutation exists.

When persistent mutation/recovery arrives in Phase 3, extend the model with whatever intermediate states are necessary to distinguish at least:

- snapshot durably persisted;
- apply pending;
- applied but unverified;
- applied and verified;
- benchmark running/completed;
- revert pending;
- recovery required.

Never infer those conditions from nullable fields or silently convert an interrupted applied state into success.

## 9. Measurement and comparison contract

Phase 1 comparison provides the minimum trustworthy semantics:

- validated finite samples;
- deterministic percentile calculation;
- configurable minimum sample count;
- configurable minimum relative change/noise threshold;
- explicit lower-is-better / higher-is-better direction;
- primary improvement/regression classification;
- guardrail regression converting a local win into `Tradeoff`;
- `Inconclusive` when evidence is structurally insufficient.

Later phases add empirically measured baseline noise, drift detection, repetitions and uncertainty without changing the rule that raw evidence remains visible.

A composite score may summarize but may never become the sole authoritative result.

## 10. ETW architecture

Phase 2 introduces read-only ETW. Separate responsibilities conceptually:

```text
Session control
→ provider/kernel configuration
→ capture
→ event decoding
→ normalized observations
→ attribution
→ distributions
→ benchmark evidence
```

DPC/ISR analysis must preserve enough provenance to identify at least CPU and module/driver where the provider data allows it. Unknown provider/event versions must not be silently reinterpreted as known semantics.

GitHub-hosted VM traces are useful for correctness only; they are not hardware-performance evidence.

## 11. Mutation interface contract

Each supported mutation mechanism should conceptually expose narrow operations such as:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

The platform mutation adapter must not decide whether benchmark evidence is better; comparison belongs to the benchmark/experiment layer.

No IPC command may accept an arbitrary registry path, PowerShell command or process command line for privileged execution.

## 12. Persistence and recovery

Before the first real mutation ships:

1. original state must be written durably before apply;
2. journal state must identify an incomplete experiment after app/service/Windows interruption;
3. recovery must re-read actual machine state before deciding what to do;
4. external state changes must not be overwritten blindly;
5. final Keep/Revert state must be verified before the journal closes.

If safety cannot be established, surface `RecoveryRequired`/unknown state rather than pretending rollback succeeded.

## 13. UI design contract

The UI should answer, in order:

1. what was measured;
2. whether the measurement was valid/stable;
3. what exact candidate is proposed/applied;
4. what changed in target metrics;
5. what changed in guardrails;
6. why the verdict was assigned;
7. what state will be kept or restored.

Do not display synthetic/demo measurements as if they came from the user's machine. During Phase 1 the application explicitly says tuning is unavailable.

The normal desktop UI remains non-elevated.

## 14. Focused test strategy

Automated tests are a **small critical-path safety net**, not an attempt to prove the whole application through test volume.

Default policy:

- normally 5–10 active automated tests total;
- one `LatencyPilot.CriticalTests` project;
- no coverage-percentage target;
- no tests for getters, labels, trivial mappings, framework behavior or every fixed bug;
- favor high-blast-radius bad paths and scenario behavior;
- when a later phase introduces a more important risk, replace/merge lower-value tests instead of growing indefinitely;
- more than 10 active automated tests requires explicit owner approval or an ADR.

Physical hardware validation and release checklists are mandatory where relevant but are not counted as automated tests.

Phase 1's critical scenarios are:

- illegal experiment transition;
- insufficient samples;
- change inside noise threshold;
- clear primary improvement;
- target improvement plus guardrail regression;
- clear primary regression;
- non-finite measurement input.

CI success proves build/package and these selected invariants only. It never proves a latency improvement on real hardware.

## 15. Release and CI contract

The repository pins .NET SDK in `global.json` and explicitly selects Microsoft.Testing.Platform for .NET 10 test execution.

Main CI must:

```text
restore
→ Release build
→ critical tests
→ self-contained win-x64 publish
→ artifact upload
```

Warnings are treated as errors. Do not globally suppress analyzers to make CI green; fix or narrowly justify the cause.

The eventual 1.0 release adds installer/service lifecycle, signing/provenance, upgrade/uninstall recovery and clean-machine/reboot validation as defined in `ROADMAP.md`.

## 16. Hardware-validation contract

A claim such as “GPU affinity X improved latency” requires physical hardware evidence. GitHub runners cannot close hardware-dependent roadmap items.

Real optimization validation must capture enough context to reproduce/interpret the result, including relevant Windows/app version, hardware identity at a non-sensitive level, candidate state and before/after measurement evidence.

Do not claim physical click-to-photon latency from Raw Input alone; software-only input timing is host-side evidence.

## 17. Architecture change rule

If a change alters any of these, add/update an ADR before treating it as established:

- language/runtime/UI framework;
- privilege model;
- IPC transport or authority;
- persistence engine/recovery semantics;
- benchmark verdict semantics;
- native-code introduction;
- test-policy cap;
- supported OS/architecture commitment.

Do not silently redefine “done.” Phase completion is governed by `ROADMAP.md` and evidenced in `PROJECT_STATUS.md`.
