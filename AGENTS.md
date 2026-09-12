# AGENTS.md — LatencyPilot Engineering Contract

This file is authoritative for coding agents and contributors working in this repository.

LatencyPilot is a **measurement-first Windows 11 latency experimentation platform**, not a generic optimizer. The engineering objective is to make low-level tuning measurable, attributable, reversible and safe enough to reason about.

Before changing code, read:

1. `ROADMAP.md` — authoritative final target and phase exit gates;
2. `PROJECT_STATUS.md` — live completion ledger;
3. `SYSTEM_DESIGN.md` — architecture and privilege boundaries;
4. `docs/BENCHMARK_METHODOLOGY.md` — measurement contract.

If implementation conflicts with these documents, resolve the conflict before continuing.

## 1. Mission

LatencyPilot must help a user answer:

- what is currently causing latency or interrupt concentration;
- which exact change is being tested;
- whether that change measurably improved the target subsystem;
- whether another subsystem regressed;
- how uncertain/noisy the result is;
- whether the system should keep or revert the change;
- how to restore the pre-experiment state.

Core loop:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

## 2. Non-goals

Do not turn LatencyPilot into:

- a registry tweak collection;
- a debloater or cleanup utility;
- a service-disabling script;
- an FPS booster with one opaque score;
- a timer/HPET folklore tool;
- a tool that disables Windows security features for performance by default;
- an overclocking/undervolting tool;
- a benchmark that claims physical click-to-photon latency without physical instrumentation.

## 3. Frozen V0.1 stack

Unless an ADR explicitly changes it:

- C# 14;
- .NET 10 LTS;
- WPF desktop application;
- Windows 11 x64;
- Windows Service for privileged operations when mutation begins;
- Named Pipes for local IPC;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`;
- PresentMon where graphics telemetry is required;
- SetupAPI + Configuration Manager for device discovery;
- CPU Sets / processor topology APIs;
- Raw Input for input-report measurements;
- SQLite for durable experiment state;
- MSTest + Microsoft.Testing.Platform;
- self-contained x64 release artifacts.

Do not add a second UI framework, persistence engine, IPC stack or native component without measured need and an ADR.

## 4. Project boundaries

```text
LatencyPilot.Core
LatencyPilot.Benchmarking
LatencyPilot.Protocol
LatencyPilot.Platform.Windows
LatencyPilot.Persistence
LatencyPilot.Service
LatencyPilot.App
```

### Core
Domain types and invariants only. No WPF, ETW implementation, Registry, SQLite, P/Invoke or machine state.

### Benchmarking
Percentiles, distributions, noise/drift analysis, comparisons, guardrails and verdicts. Keep deterministic and hardware-independent where possible.

### Protocol
Versioned IPC commands/events/DTOs/errors only. Never privileged implementation logic.

### Platform.Windows
All raw Windows mechanisms: ETW, SetupAPI/CM, PCI/device topology, interrupt policy, MSI/MSI-X, CPU topology, Raw Input, USB/NDIS telemetry, registry/device-policy adapters and PresentMon adapters.

### Persistence
SQLite, migrations, snapshots, experiment journal, recovery state and stored benchmark history.

### Service
The narrow privileged boundary. It validates and executes explicit supported operations; it must never become a generic automation host.

### App
Non-elevated WPF UX. It must never directly mutate privileged Windows state.

## 5. Privilege and mutation rules

The desktop application must not require permanent elevation.

Never expose through the privileged boundary:

- arbitrary PowerShell;
- arbitrary process/command execution;
- arbitrary registry paths or values;
- generic run-as-SYSTEM;
- user-provided privileged plugins or DLL loading.

Every system mutation must follow:

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

If the current state cannot be proven, report it as unknown and enter recovery. Never manufacture success.

## 6. Benchmark rules

Read `docs/BENCHMARK_METHODOLOGY.md` before changing measurement logic.

At minimum:

- measure baseline variability before interpreting small deltas;
- prefer repeatable A/B-style runs over a single before/after observation;
- preserve raw samples or auditable aggregates;
- report sample count and relevant tail metrics;
- do not infer significance from percentage delta alone;
- separate target metrics from guardrails;
- treat baseline drift as a validity problem;
- represent uncertainty explicitly;
- never let a composite score hide a regression.

Authoritative verdicts remain explicit, such as:

```text
Improved
Regressed
Tradeoff
NoMeasurableDifference
Inconclusive
```

Do not introduce an authoritative `Better = true` shortcut.

## 7. Testing policy — focused, not exhaustive

LatencyPilot intentionally does **not** pursue high unit-test counts or coverage percentages.

The active automated critical suite should normally contain **5–10 high-value tests total**. Adding a test requires a credible high-blast-radius failure mode; do not write tests for getters, labels, trivial mappings, framework behavior or every historical bug.

Prefer one scenario test that crosses several important invariants over many microscopic regression tests.

The critical suite should collectively target roughly the failures most likely to make the application unsafe or fundamentally misleading, for example:

- illegal experiment-state transition;
- bad/insufficient/non-finite measurement input;
- false improvement inside the noise threshold;
- clear improvement/regression misclassification;
- target improvement masking a guardrail regression;
- unsafe mutation/recovery failure once mutation exists;
- protocol/persistence corruption once those paths become safety-critical.

When a new phase introduces a more important risk, merge, replace or retire lower-value tests so the suite stays small. More than 10 active automated tests requires explicit repository-owner approval or an ADR explaining why the cap is no longer sufficient.

Physical-hardware validation, exploratory benchmark runs and release checklists are not counted as automated tests.

CI green means the known critical paths build and pass; it is **not** proof of hardware latency improvement.

## 8. Hardware claims

GitHub-hosted CI is not evidence that a hardware optimization improves real hardware.

CI may prove buildability, deterministic comparison behavior, parser behavior using fixtures, protocol/persistence integrity and other machine-independent contracts.

Claims about GPU affinity, USB/xHCI, NIC/RSS, interrupt placement or latency improvement require physical Windows 11 hardware evidence.

Never fabricate physical results from VM data.

## 9. Windows tuning policy

Before implementing a tuning mechanism:

1. research Microsoft and authoritative vendor documentation;
2. document the supported mechanism and assumptions;
3. identify reboot requirements;
4. define snapshot and rollback semantics;
5. define primary and guardrail measurements;
6. identify meaningful failure modes;
7. add/update an ADR if architecture or policy changes.

Undocumented tweaks are high risk and are not eligible for automatic application without explicit project-owner approval and unusually strong evidence.

Do not automatically change unrelated HPET/platform-clock settings, dynamic tick, Defender/VBS/security features, unrelated services, undocumented scheduler values, mass network registry values or mass MMCSS settings.

## 10. ETW and telemetry

Prefer authoritative Windows providers and documented semantics.

Do not silently reinterpret unknown/missing fields. Preserve source provenance where practical: capture interval, provider/driver identity, parser/application version and trace reference.

Parser robustness should be handled by design first. Add a fixture/test only when a malformed or versioned input represents a high-value failure scenario under the focused test policy.

## 11. Persistence and recovery

Experiment history and rollback state are safety-critical.

Before any mutation is implemented, persistence must guarantee that original state is durably recorded before apply. Schema changes must preserve active recovery records and be forward-auditable.

Do not rewrite historical benchmark results merely to match a newer interpretation; prefer versioned interpretation/migration metadata.

## 12. Logging and diagnostics

Logs should diagnose experiment state, IPC/privilege errors, apply/verify/revert mismatches, parser failures and unsupported hardware/provider behavior.

Do not log secrets, tokens, arbitrary user files, full registry exports, unnecessary device serials or unrelated personally identifying information.

Diagnostic bundles must remain local-first and reviewable before sharing.

## 13. UI rules

Show evidence, not marketing claims.

For each real experiment eventually expose:

- exact change;
- original and candidate state;
- whether apply was verified;
- benchmark validity;
- meaningful before/after metrics;
- delta and uncertainty/noise context;
- guardrail regressions;
- keep/revert and recovery status.

Do not push the user toward keeping an inconclusive or materially trade-off-heavy change.

Do not show fake/synthetic values as real machine results.

## 14. Source organization

Prefer cohesive files/modules over extreme fragmentation. Split by responsibility, not arbitrary line count.

Do not add abstractions with no current safety, clarity or testability benefit. Remove dead/speculative code rather than preserving it for possible future use.

## 15. Roadmap and completion discipline

`ROADMAP.md` defines what 100% means and the exact exit gate for each phase. `PROJECT_STATUS.md` records what is actually complete.

Rules:

- never call a phase complete because code merely exists;
- check a roadmap item only when its evidence exists on `main`;
- build-dependent items require a successful CI run;
- hardware-dependent items require physical-hardware evidence;
- "implemented but unverified" remains incomplete;
- when completing or discovering a blocker, update `PROJECT_STATUS.md` in the same work cycle;
- if the scope changes, update the roadmap before silently redefining "done".

Agents must use these files instead of relying on chat memory.

## 16. Change discipline

For every meaningful change:

- understand the existing architecture first;
- keep commits logically scoped;
- avoid unrelated formatting churn;
- update documentation when contracts change;
- add/modify tests only when justified by the focused critical-test policy;
- never weaken safety logic merely to make CI pass;
- inspect actual CI failures and fix root causes rather than disabling validation.

## 17. Licensing and contributions

LatencyPilot is source-available and is not an OSI open-source project.

Do not replace `LICENSE`, `CLA.md` or contribution terms unless explicitly instructed by the repository owner.

Third-party dependencies must have compatible licenses for the intended distribution model. Publicly visible source is not automatically reusable source; verify licensing before copying code.

## 18. Definition of done for an optimizer

A supported optimization domain is not done until it has:

- authoritative applicability detection;
- topology/device identification;
- original-state capture;
- validated candidate generation;
- safe apply and independent verification;
- subsystem-specific target measurement;
- cross-subsystem guardrails;
- noise/drift handling appropriate to the benchmark;
- explicit verdict semantics;
- revert and interrupted-run recovery;
- the minimal critical test coverage justified by risk;
- user-facing explanation of trade-offs;
- documentation and physical-hardware validation where required.

If any of these are deferred, keep the feature experimental and unavailable to automatic recommendation.
