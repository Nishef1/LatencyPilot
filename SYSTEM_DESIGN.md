# LatencyPilot System Design

Status: **Authoritative baseline for V0.1**

Last updated: 2026-09-12

## 1. Purpose

LatencyPilot is a Windows 11 latency experimentation platform that measures system behavior, applies narrowly scoped tuning candidates, verifies the resulting state, benchmarks target and collateral effects, and lets the user keep or revert the change.

It is intentionally not designed as a bulk tweak pack. Its central product claim is not that a specific setting is universally faster; it is that LatencyPilot can help determine whether a setting measurably improves a particular system and workload.

## 2. Product contract

Every supported optimization follows:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

Every system mutation follows:

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

A feature that cannot satisfy this lifecycle must not be promoted as a supported automatic optimization.

## 3. Target platform

V0.1 target:

- Windows 11
- x64
- self-contained .NET release
- interactive desktop user
- local machine only

ARM64 is not a V0.1 release target. The architecture should avoid unnecessary x64 assumptions outside the platform layer, but no ARM64 compatibility claim is made until all required dependencies and hardware tests support it.

## 4. Technology baseline

- C# 14
- .NET 10 LTS
- WPF
- Windows Service
- Named Pipes
- Microsoft.Diagnostics.Tracing.TraceEvent
- PresentMon integration where applicable
- SetupAPI / Configuration Manager
- CPU Sets / processor topology APIs
- Raw Input
- SQLite
- MSTest + Microsoft.Testing.Platform

Native C++ is not part of the baseline. A small native component may be introduced only if profiling demonstrates a real limitation that cannot be solved acceptably in managed code, and the change is documented in an ADR.

## 5. High-level architecture

```text
┌──────────────────────────────────────────────┐
│ LatencyPilot.App                             │
│ WPF / non-elevated                          │
│                                              │
│ Dashboard                                    │
│ Baseline / experiment workflow               │
│ Results / trade-offs                         │
│ History / recovery                           │
└───────────────────┬──────────────────────────┘
                    │
                    │ Versioned Named-Pipe IPC
                    ▼
┌──────────────────────────────────────────────┐
│ LatencyPilot.Service                         │
│ Windows Service / privileged boundary        │
│                                              │
│ command authorization + validation           │
│ mutation orchestration                       │
│ journaling / recovery coordination           │
└───────────────┬──────────────────────────────┘
                │
       ┌────────┴─────────────────────────┐
       ▼                                  ▼
┌───────────────────────────┐   ┌───────────────────────────┐
│ Platform.Windows          │   │ Persistence               │
│                           │   │                           │
│ ETW                       │   │ SQLite                    │
│ SetupAPI / CM             │   │ experiment journal        │
│ MSI / affinity            │   │ snapshots                 │
│ CPU topology              │   │ benchmark history         │
│ Raw Input                 │   │ recovery records          │
│ PresentMon adapter        │   │ schema migrations         │
│ network / USB adapters    │   │                           │
└─────────────┬─────────────┘   └───────────────────────────┘
              │
              ▼
┌───────────────────────────┐
│ Windows 11 / hardware     │
└───────────────────────────┘

Shared domain layers:

Core ← Benchmarking
Core ← Protocol
```

## 6. Project boundaries

### LatencyPilot.Core

Owns stable domain concepts:

- device identity abstractions;
- CPU/topology domain models;
- experiment identity and lifecycle states;
- metric descriptors;
- result/verdict models;
- safety/recovery domain invariants.

It must remain free of WPF, SQLite, ETW implementation details, registry paths, P/Invoke, and Windows Service hosting.

### LatencyPilot.Benchmarking

Owns measurement analysis:

- baselines;
- candidate comparisons;
- percentile calculations;
- robust dispersion metrics;
- noise-floor estimation;
- bootstrap/resampling logic;
- confidence/uncertainty;
- drift detection;
- target vs guardrail classification;
- verdict calculation;
- workload-specific weighting where explicitly required.

It should be deterministic enough to test with synthetic and fixture data independently of physical hardware.

### LatencyPilot.Protocol

Owns local IPC contracts:

- command envelopes;
- event envelopes;
- DTOs;
- error codes;
- protocol versioning;
- compatibility policy.

The protocol must not expose arbitrary registry, shell, file, or process execution capabilities.

### LatencyPilot.Platform.Windows

Owns Windows-specific mechanisms:

- ETW session control and parsing;
- DPC/ISR event interpretation;
- device enumeration;
- SetupAPI and Configuration Manager interop;
- PCI/device topology;
- interrupt-affinity policy;
- MSI/MSI-X inspection/mutation where supported;
- CPU topology and CPU Sets;
- Raw Input capture;
- USB/xHCI tracing;
- networking/RSS integration;
- power/configuration adapters;
- PresentMon process/session adapter;
- Windows reboot/restart requirements;
- low-level registry/device-policy access.

All raw Windows interop belongs here.

### LatencyPilot.Persistence

Owns persistence:

- SQLite database;
- migrations;
- snapshots;
- experiment journal;
- benchmark records;
- recovery state;
- diagnostic references.

Rollback state is safety-critical and must be committed before the corresponding mutation is attempted.

### LatencyPilot.Service

Owns privileged execution:

- service host;
- local IPC listener;
- client identity/authorization checks;
- command validation;
- mutation orchestration;
- recovery execution;
- service-side logging.

The service does not become a generic privileged automation host.

### LatencyPilot.App

Owns desktop UX:

- navigation;
- experiment setup;
- baseline workflow;
- recommendation and trade-off views;
- raw metric inspection;
- keep/revert confirmation;
- recovery UX;
- settings that do not violate privilege boundaries.

It remains non-elevated during normal operation.

## 7. Repository layout

```text
LatencyPilot/
│
├── LatencyPilot.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── NuGet.Config
├── .editorconfig
├── README.md
├── LICENSE
├── CLA.md
├── CONTRIBUTING.md
├── SECURITY.md
├── AGENTS.md
├── SYSTEM_DESIGN.md
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
│   ├── LatencyPilot.Core.Tests/
│   ├── LatencyPilot.Benchmarking.Tests/
│   ├── LatencyPilot.Protocol.Tests/
│   ├── LatencyPilot.Platform.Windows.Tests/
│   ├── LatencyPilot.Persistence.Tests/
│   ├── LatencyPilot.Service.IntegrationTests/
│   ├── LatencyPilot.Windows.IntegrationTests/
│   ├── LatencyPilot.App.Tests/
│   └── LatencyPilot.HardwareTests/
│
├── test-assets/
│   ├── etw/
│   ├── presentmon/
│   ├── raw-input/
│   ├── cpu-topology/
│   ├── devices/
│   ├── statistics/
│   ├── expected/
│   └── manifest.json
│
├── perf/
│   └── LatencyPilot.Microbenchmarks/
│
├── tools/
│   ├── TraceInspector/
│   ├── FixtureBuilder/
│   └── DiagnosticBundle/
│
├── docs/
│   ├── adr/
│   ├── architecture/
│   ├── benchmarks/
│   ├── safety/
│   └── releases/
│
├── eng/
│   ├── build.ps1
│   ├── test.ps1
│   ├── package.ps1
│   └── validate.ps1
│
└── .github/
    ├── workflows/
    ├── ISSUE_TEMPLATE/
    ├── pull_request_template.md
    └── CODEOWNERS
```

Empty directories do not need placeholder files until implementation reaches them.

## 8. Experiment state machine

The authoritative experiment lifecycle should be explicit rather than inferred from nullable fields.

Recommended states:

```text
Created
BaselinePending
BaselineRunning
BaselineCompleted
CandidatePrepared
SnapshotPersisted
ApplyPending
AppliedUnverified
AppliedVerified
BenchmarkRunning
BenchmarkCompleted
VerdictReady
Kept
RevertPending
Reverted
RecoveryRequired
Aborted
Invalid
```

State transitions must be validated.

A process crash must not silently transform an `AppliedUnverified` experiment into success.

## 9. Snapshot model

A snapshot must contain enough information to restore the specific setting being changed without exporting or overwriting unrelated machine state.

Each snapshot should include:

- experiment ID;
- mutation kind;
- stable target identity;
- original value/state;
- target metadata required for verification;
- capture timestamp;
- Windows/build/application version where relevant;
- hash/version information needed to detect stale state;
- schema version.

Avoid giant registry exports or opaque binary dumps when a minimal, typed snapshot is possible.

## 10. Mutation model

Each mutation implementation should expose conceptually separate operations:

```text
IsApplicable(target)
CaptureSnapshot(target)
ValidateCandidate(snapshot, candidate)
Apply(target, candidate)
ReadActualState(target)
Verify(expected, actual)
Revert(snapshot)
```

The mutation implementation must not decide whether the benchmark result is better. That belongs to the experiment/benchmark layer.

## 11. IPC design

Named Pipes are the baseline local transport.

Requirements:

- local-machine only;
- explicit protocol version;
- bounded message sizes;
- typed command allow-list;
- request IDs;
- cancellation where safe;
- structured error codes;
- no arbitrary command execution;
- no arbitrary registry paths from the UI;
- service re-validates all input even if the UI already validated it;
- logs never expose secrets or unnecessary identifiers.

Example conceptual commands:

```text
GetServiceCapabilities
GetPendingRecovery
CaptureSystemInventory
StartTraceSession
StopTraceSession
PrepareExperiment
ApplyCandidate
VerifyCandidate
RevertExperiment
FinalizeExperiment
```

Avoid commands such as:

```text
RunPowerShell(string)
SetRegistry(path, name, value)
Execute(string)
```

## 12. ETW architecture

ETW is the primary low-level observation mechanism for DPC/ISR and supported subsystem traces.

The design should separate:

```text
Session control
    ↓
Raw event ingestion
    ↓
Provider/version-specific decoding
    ↓
Normalized event model
    ↓
Aggregation/distribution analysis
    ↓
Experiment metrics
```

Provider-version differences must not leak into benchmark logic.

Golden ETW fixtures should validate normalized output for known traces.

## 13. DPC/ISR metrics

Where data permits, preserve:

- CPU/logical processor;
- module/driver;
- function where resolvable;
- DPC vs ISR;
- start/end or duration;
- interrupt vector where available;
- message index/MSI information where available;
- event count;
- total duration;
- distribution/tail statistics.

Useful aggregations include:

```text
By CPU
By module
By module + CPU
By DPC/ISR type
By time window
```

CPU0 concentration is an observation, not automatically a defect.

## 14. CPU topology

Do not treat logical processor IDs as independent physical cores.

Model at least:

- logical processor;
- physical core;
- SMT sibling relationship;
- processor group if relevant;
- CPU Set identity;
- NUMA node where relevant;
- efficiency class on hybrid CPUs where available.

Candidate generation must use topology rather than iterating arbitrary CPU numbers blindly.

## 15. Benchmark design

The benchmark engine should model an experiment as:

```text
Environment snapshot
+ workload definition
+ baseline runs
+ one candidate mutation
+ candidate runs
+ guardrail measurements
+ validity checks
+ statistical comparison
+ verdict
```

The first objective is attribution, not maximum search breadth.

### Baseline

Measure baseline-to-baseline variation before interpreting small improvements.

### Repetition

Prefer repeated sequences such as:

```text
A1 → B1 → B2 → A2
```

when the mutation is safely switchable, or equivalent multi-boot designs when a reboot is required.

### Drift

Detect and flag substantial baseline drift caused by thermal state, background activity, workload mismatch, or other uncontrolled changes.

### Tail behavior

Do not rely on average alone. Depending on metric, retain p50/p90/p95/p99/p99.9/max and robust dispersion.

### Verdicts

Recommended result classes:

```text
ConfirmedImprovement
ConfirmedRegression
TradeOff
NoMeasurableDifference
Inconclusive
InvalidExperiment
```

A result may be a trade-off even when the primary metric improves.

## 16. Workload profiles

Profiles change priorities, not raw measurements.

Examples planned for later:

- General responsiveness
- Competitive gaming
- Audio/DAW
- Streaming/content creation
- Low-latency networking

A profile may weight target/guardrail importance, but the UI must still expose the underlying vector of results.

## 17. GPU-affinity V0.1 domain

V0.1 should prove the full experiment lifecycle using GPU interrupt-affinity candidates.

The implementation must:

1. identify the display/GPU device robustly;
2. inspect current interrupt/MSI policy;
3. capture original policy;
4. generate topology-aware candidate CPU sets;
5. apply one candidate at a time;
6. verify actual policy;
7. capture ETW metrics;
8. integrate PresentMon when a repeatable graphics workload is available;
9. measure guardrails;
10. compare repeated runs;
11. keep/revert explicitly;
12. recover after interruption.

The tool must not claim that a specific core is universally optimal.

## 18. USB design direction

USB optimization is not part of V0.1 automatic tuning, but the architecture should support later correlation:

```text
HID device
→ USB port/hub
→ xHCI controller
→ interrupt/DPC behavior
→ CPU
```

Raw Input can measure report-interval behavior; it does not by itself measure true physical click-to-photon latency.

## 19. Network design direction

NIC tuning must consider both interrupt affinity and RSS behavior.

Future network experiments may require:

- NDIS DPC metrics;
- RSS processor distribution;
- queue configuration;
- local controlled RTT/jitter tests;
- throughput guardrails;
- loss/retransmission indicators.

Internet RTT is supplemental evidence, not a clean primary benchmark because route/ISP conditions are uncontrolled.

## 20. Persistence model

SQLite is the baseline local store.

Logical entities should include:

```text
SystemSnapshot
DeviceSnapshot
Experiment
MutationSnapshot
BenchmarkRun
MetricSeries / MetricSummary
ExperimentVerdict
RecoveryRecord
ApplicationVersion
```

Exact schema is intentionally deferred until implementation, but these concepts should remain separable.

## 21. Recovery model

On service/application startup:

```text
Load open journal records
→ determine last durable state
→ read actual machine state
→ compare with expected/original state
→ choose safe recovery action
→ require user intervention if state is ambiguous
```

Do not mark experiments complete solely because the service restarted successfully.

## 22. UI information architecture

Initial surfaces:

### Overview

- system readiness;
- unresolved recovery state;
- current latency summary;
- major DPC/ISR contributors;
- last experiment outcome.

### Analyze

- trace capture;
- per-CPU DPC/ISR distribution;
- top drivers/modules;
- timeline and tail metrics;
- device/topology correlation where known.

### Experiments

- candidate selection;
- benchmark plan;
- active run progress;
- before/after comparison;
- guardrail trade-offs;
- keep/revert decision.

### History

- immutable experiment record;
- exact change;
- benchmark validity;
- raw/aggregate results;
- final state;
- recovery events.

### Diagnostics

- application/service logs;
- capability detection;
- sanitized diagnostic bundle creation.

## 23. Diagnostic bundles

Bundles should be local-only by default and user-reviewable before sharing.

Potential contents:

```text
manifest.json
system-summary.json
cpu-topology.json
devices.json
interrupt-policy.json
experiments.json
application.log
service.log
optional trace references / explicitly included ETL
```

Do not include secrets, browser data, user files, credentials, or unnecessary hardware serial numbers.

## 24. Testing architecture

Test categories:

### Unit

Core, statistics, state machines, candidate generation, protocol validation.

### Property/statistical

Synthetic distributions, percentile ordering, bootstrap behavior, noise-floor boundaries, drift, heavy tails, insufficient samples.

### Golden fixtures

ETW/PresentMon/topology parser outputs for deterministic captured assets.

### Persistence/recovery

Schema migration, journal durability, crash points, partial mutation states, recovery decisions.

### Windows integration

Read-only or safely isolated Windows API tests on GitHub-hosted Windows runners when supported.

### Hardware

Physical machine validation for interrupt affinity, xHCI, NIC/RSS, GPU, timing, and claims impossible to validate in a VM.

Hosted CI must never be treated as evidence of physical latency improvement.

## 25. CI gates

Pull-request CI should eventually enforce:

```text
restore --locked-mode
format verification
Release build
warnings as errors
unit/statistical tests
fixture tests
architecture tests
coverage thresholds
license/dependency checks
security analysis
```

Nightly jobs may add:

```text
full fixture corpus
fuzz/parser robustness
mutation/recovery stress tests
microbenchmarks
dependency audit
```

Release jobs must build from a clean tag/commit and publish hashes for artifacts.

## 26. Architecture tests

Add automated dependency tests once projects exist.

Examples:

- Core must not reference WPF/WindowsBase/SQLite/TraceEvent.
- App must not reference raw registry/SetupAPI mutation implementations.
- Protocol must not reference Service or App.
- Benchmarking must not depend on WPF.
- privileged mutation implementations must live in Platform.Windows/Service boundaries.

## 27. Security model

Primary threats include:

- misuse of privileged service commands;
- arbitrary registry/process execution;
- malicious/untrusted IPC clients;
- stale target identity causing mutation of the wrong device;
- tampered snapshot/recovery state;
- path/DLL hijacking;
- unsafe diagnostic bundle contents;
- unsigned/untrusted update mechanisms if added later.

Security-sensitive architecture changes require explicit review.

## 28. Update model

No self-update mechanism is part of V0.1.

When releases begin, prefer verifiable GitHub Release artifacts with hashes. Do not add an updater that executes downloaded binaries without an explicit signing/verifiability design.

## 29. Configuration policy

Avoid hidden magic defaults.

Each automatic recommendation should be explainable in terms of:

- applicability;
- measured baseline;
- candidate tested;
- measured delta;
- uncertainty;
- guardrails;
- final verdict.

## 30. V0.1 definition of done

V0.1 is complete only when the following path works end-to-end on supported Windows 11 x64 hardware:

```text
Inventory
→ topology
→ ETW baseline
→ DPC/ISR analysis
→ GPU interrupt-affinity candidate
→ snapshot
→ apply
→ verify
→ benchmark
→ statistical comparison
→ keep/revert
→ history
→ interrupted-run recovery
```

The release must not depend on undocumented manual recovery steps for normal supported operations.

## 31. Architecture change process

Changes to any of the following require an ADR in `docs/adr/`:

- core language/runtime;
- UI framework;
- privileged-process model;
- IPC transport or trust model;
- persistence engine;
- benchmark verdict semantics;
- introduction of native code;
- updater/distribution trust model;
- support for a new architecture such as ARM64;
- changes that weaken snapshot/revert guarantees.

## 32. Non-negotiable principle

If LatencyPilot cannot explain what changed, measure what happened, detect important collateral regressions, and restore the prior state, it must not automatically apply that optimization.
