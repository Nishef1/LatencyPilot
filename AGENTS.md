# AGENTS.md — LatencyPilot Engineering Contract

This file is authoritative for coding agents and contributors working in this repository.

LatencyPilot is a **measurement-first Windows 11 latency experimentation platform**, not a generic optimizer. The primary engineering objective is to make low-level tuning measurable, attributable, reversible, and safe enough to reason about.

If an implementation conflicts with this file, `SYSTEM_DESIGN.md`, or the benchmark contract, stop and resolve the conflict before continuing.

## 1. Mission

LatencyPilot must help a user answer:

- what is currently causing latency or interrupt concentration;
- which change is being tested;
- whether that change measurably improved the target subsystem;
- whether another subsystem regressed;
- how confident the result is;
- whether the system should keep or revert the change;
- how to restore the exact pre-experiment state.

The core loop is:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

## 2. Non-goals

Do not turn LatencyPilot into:

- a registry tweak collection;
- a debloater;
- a generic cleanup utility;
- a service-disabling script;
- an FPS booster that reports only a single score;
- a timer/HPET folklore tool;
- a tool that disables security features for performance by default;
- a hardware overclocking/undervolting utility;
- a benchmark that claims hardware-level input latency without appropriate hardware measurement.

## 3. Frozen stack for V0.1

Unless an ADR explicitly changes it:

- C# 14
- .NET 10 LTS
- WPF desktop application
- Windows 11 x64
- Windows Service for privileged operations
- Named Pipes for local IPC
- ETW / Microsoft.Diagnostics.Tracing.TraceEvent
- PresentMon for graphics/frame telemetry where applicable
- SetupAPI + Configuration Manager for device discovery
- CPU Sets / processor topology APIs
- Raw Input for input-report measurements
- SQLite for persistent experiment state
- MSTest + Microsoft.Testing.Platform
- self-contained x64 release artifacts

Do not introduce a second application framework, second persistence engine, alternate IPC stack, or native component without measured need and an ADR.

## 4. Dependency rules

Intended projects:

```text
LatencyPilot.Core
LatencyPilot.Benchmarking
LatencyPilot.Protocol
LatencyPilot.Platform.Windows
LatencyPilot.Persistence
LatencyPilot.Service
LatencyPilot.App
```

Rules:

### Core

`LatencyPilot.Core` contains domain types and invariants.

It must not depend on:

- WPF;
- ETW implementations;
- Registry APIs;
- SetupAPI;
- SQLite;
- Windows Service infrastructure;
- PresentMon;
- machine-specific state.

### Benchmarking

`LatencyPilot.Benchmarking` contains experiment design, distributions, percentiles, noise-floor analysis, confidence logic, verdicts, and workload-independent comparison logic.

Keep it deterministic and testable without real hardware whenever possible.

### Protocol

`LatencyPilot.Protocol` contains versioned IPC commands, events, DTOs, errors, and compatibility contracts.

It must not contain privileged implementation logic.

### Platform.Windows

`LatencyPilot.Platform.Windows` owns Windows-specific inspection and mutation mechanisms:

- ETW sessions and parsers;
- SetupAPI/Configuration Manager;
- PCI/device topology;
- MSI/MSI-X and interrupt policy access;
- CPU topology and CPU Sets;
- Raw Input;
- USB/NDIS/platform telemetry;
- registry/device-policy adapters;
- PresentMon integration adapters.

Do not leak raw P/Invoke or registry paths across the solution.

### Persistence

`LatencyPilot.Persistence` owns SQLite, migrations, snapshots, experiment journals, recovery records, and stored result schemas.

### Service

`LatencyPilot.Service` is the privileged boundary. It validates and executes narrow commands; it is not a second business-logic layer.

### App

`LatencyPilot.App` is the non-elevated WPF client. It must never directly mutate privileged Windows state.

## 5. Privilege rules

The desktop application must not require permanent elevation.

Privileged mutations must:

1. cross the versioned local IPC boundary;
2. be explicitly enumerated commands, not arbitrary shell/registry execution;
3. validate all identifiers and values in the service;
4. verify the target still matches the snapshot before mutation where applicable;
5. journal intent and original state before apply;
6. independently verify the post-apply state;
7. expose a deterministic revert path;
8. fail closed if authorization, validation, or state verification fails.

Never add:

- arbitrary PowerShell execution from the UI;
- arbitrary command execution through the service;
- arbitrary registry path/value mutation through IPC;
- generic "run as SYSTEM" capabilities;
- user-controlled DLL/plugin loading in the privileged service.

## 6. Mutation contract

Every system-changing feature must implement this lifecycle:

```text
Detect applicability
→ Snapshot original state
→ Validate candidate
→ Journal pending experiment
→ Apply
→ Verify actual state
→ Benchmark
→ Classify result
→ Keep or Revert
→ Verify final state
→ Close journal
```

A mutation is not complete if any step is missing.

### Required failure behavior

Tests must cover at least:

- snapshot failure;
- validation failure;
- partial apply;
- apply exception;
- verification mismatch;
- benchmark crash;
- service restart;
- app termination;
- Windows reboot between apply and verdict;
- revert failure;
- state changed externally during an experiment.

If state cannot be safely inferred, report it as unknown and require recovery instead of pretending success.

## 7. Benchmark rules

Read `docs/BENCHMARK_METHODOLOGY.md` before changing benchmark logic.

Minimum rules:

- establish baseline variability/noise before interpreting small changes;
- prefer repeated A/B-style measurements over one before/after run;
- preserve raw samples or sufficient aggregate data for auditability;
- report sample count;
- report tail metrics appropriate to the subsystem;
- never infer significance from percentage delta alone;
- distinguish target metrics from guardrail metrics;
- treat baseline drift as a validity problem;
- classify uncertainty explicitly;
- do not let a composite score hide a regression.

Supported verdict vocabulary should remain explicit, for example:

```text
ConfirmedImprovement
ConfirmedRegression
TradeOff
NoMeasurableDifference
Inconclusive
InvalidExperiment
```

Do not add a generic `Better = true` field as the authoritative result.

## 8. Statistics

Latency distributions are commonly non-normal. Do not assume normality without evidence.

Where appropriate, use robust statistics and resampling methods such as bootstrap confidence intervals.

At minimum test invariants such as:

```text
p50 <= p90 <= p95 <= p99 <= p99.9 <= max
```

and include synthetic datasets with:

- identical distributions;
- known improvement;
- known regression;
- heavy tails;
- multimodal distributions;
- sparse extreme outliers;
- insufficient sample counts;
- baseline drift.

## 9. ETW and telemetry

Prefer authoritative Windows providers and documented event semantics.

Do not silently reinterpret missing or unknown fields.

ETW parser changes require deterministic golden fixtures where feasible.

A parser must not crash the application because of:

- unknown provider versions;
- missing optional fields;
- unsupported event versions;
- truncated fixture data;
- unexpected but valid ordering.

Preserve provenance: result records should identify the trace/source, capture interval, relevant provider/version information where available, and parser/application version.

## 10. Hardware claims

GitHub-hosted CI is not evidence that a hardware optimization improves real hardware.

CI may validate:

- parsers;
- statistics;
- state machines;
- protocol contracts;
- persistence;
- synthetic/golden fixtures;
- read-only Windows integration where supported.

Real claims about GPU affinity, USB/xHCI, NIC/RSS, device interrupt behavior, or physical-system latency require physical-hardware validation.

Never fabricate or infer physical results from VM tests.

## 11. Windows tuning policy

Before implementing a new tuning mechanism:

1. search Microsoft documentation and authoritative vendor documentation;
2. document the supported mechanism;
3. identify Windows-version and driver assumptions;
4. identify whether a reboot is required;
5. identify rollback semantics;
6. identify primary and guardrail metrics;
7. identify failure modes;
8. add or update an ADR if architecture or policy changes.

Treat undocumented tweaks as high risk. They are not eligible for automatic application without unusually strong evidence and explicit project approval.

Do not add broad automatic changes to:

- HPET/platform clock settings;
- dynamic tick settings;
- Defender/VBS/security features;
- unrelated Windows services;
- undocumented scheduler values;
- mass network registry tweaks;
- mass MMCSS changes;
- power settings unrelated to the experiment.

## 12. Testing requirements

Use MSTest + Microsoft.Testing.Platform unless an ADR changes the test stack.

Tests must be:

- deterministic where possible;
- independent of execution order;
- isolated from a developer's real registry/device state unless explicitly marked integration/hardware;
- parallel-safe unless the test category requires serialization;
- named for behavior, not implementation detail.

Target coverage policy:

| Project | Line | Branch |
|---|---:|---:|
| Core | 90% | 85% |
| Benchmarking | 95% | 90% |
| Protocol | 95% | 90% |
| Persistence | 85% | 80% |
| Platform.Windows | 75% | 65% |
| Service | 80% | 75% |
| App/ViewModels | 80% | 70% |

Coverage is a guardrail, not a substitute for meaningful assertions.

## 13. Golden fixtures

Keep small deterministic fixtures in `test-assets/` when licensing and privacy permit.

Each fixture should have:

- a documented origin;
- anonymization status;
- expected parser output;
- SHA-256 in the manifest;
- no user credentials, machine secrets, personally identifying paths, or unnecessary identifiers.

Large traces must not bloat normal Git history without explicit approval.

## 14. Performance testing

Correctness tests and microbenchmarks are separate.

Use `perf/` for parser/statistics/serialization microbenchmarks.

Do not fail a PR solely because a GitHub-hosted VM reports a small timing regression. Hosted runners are noisy and are not the project's hardware benchmark source of truth.

## 15. Persistence and schema changes

Experiment history and rollback state are safety-critical.

Schema changes must:

- use migrations;
- preserve rollback records;
- be forward-auditable;
- avoid destructive migrations without an explicit migration/recovery plan;
- include migration tests from supported previous schemas.

Do not overwrite historical experiment results in place merely to match a newer interpretation. Prefer versioned interpretation or migration records.

## 16. Logging and diagnostics

Logs must be useful for diagnosing:

- experiment state;
- privilege/IPC errors;
- apply/verify/revert mismatches;
- parser failures;
- unsupported hardware/provider behavior.

Do not log:

- secrets;
- authentication tokens;
- arbitrary user files;
- full registry exports;
- unnecessary device serial numbers;
- personally identifying data not needed for diagnosis.

Diagnostic bundles must be reviewable before sharing and should be local-only by default.

## 17. UI rules

The UI must show evidence rather than marketing claims.

For each experiment, expose:

- exact change;
- original value/state;
- candidate value/state;
- whether apply was verified;
- benchmark validity;
- before/after distributions or key raw metrics;
- delta;
- uncertainty/confidence;
- guardrail regressions;
- keep/revert status;
- recovery status.

Avoid dark patterns that push the user toward keeping a change when the result is inconclusive or has a material trade-off.

## 18. Source organization

Prefer cohesive files and modules over extreme fragmentation. Split when a file contains multiple independent responsibilities or becomes difficult to reason about/test, not merely because it crosses an arbitrary line count.

Do not create abstraction layers with no current consumer or testability/safety benefit.

Remove dead code instead of preserving speculative paths.

## 19. Change discipline

For each meaningful change:

- understand existing architecture first;
- update tests with implementation;
- update docs/ADRs when contracts change;
- keep commits logically scoped;
- avoid unrelated formatting churn;
- never weaken a safety check merely to make a test pass;
- never silence warnings broadly without root-cause analysis.

If CI fails, inspect the actual failure and fix the cause. Do not disable tests, coverage, analyzers, or release validation as a shortcut.

## 20. Licensing and contributions

LatencyPilot is source-available and is not an open-source project.

Do not replace `LICENSE`, `CLA.md`, or contribution terms with an OSI license unless the repository owner explicitly instructs it.

Third-party dependencies must have compatible licenses for the intended distribution model and must be documented when required.

Do not copy code from public repositories merely because it is visible. Confirm license compatibility first.

## 21. Definition of done for a new optimizer

A new optimization domain is not done until it has:

- authoritative applicability detection;
- topology/device identification;
- original-state capture;
- validated candidate generation;
- safe apply;
- independent verification;
- subsystem-specific benchmark;
- system guardrails;
- noise-floor handling;
- repeated measurement strategy;
- explicit verdict semantics;
- revert and interrupted-run recovery;
- unit/integration/golden tests as applicable;
- user-facing explanation of trade-offs;
- documentation.

If any of these are intentionally deferred, the feature must remain experimental and unavailable to automatic recommendation.
