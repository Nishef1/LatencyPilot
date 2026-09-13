# LatencyPilot Engineering Contract

This file is authoritative for coding agents and contributors working in this repository.

LatencyPilot is a **measurement-first Windows 11 latency experimentation platform**, not a generic optimizer. The engineering objective is to make low-level tuning measurable, attributable, reversible and safe enough to reason about.

Before changing code, read:

1. `ROADMAP.md` — authoritative final target and phase exit gates;
2. `PROJECT_STATUS.md` — live completion ledger and current execution ladder;
3. `SYSTEM_DESIGN.md` — architecture and privilege boundaries;
4. `docs/BENCHMARK_METHODOLOGY.md` — measurement contract;
5. `docs/adr/*` — accepted architecture changes.

If implementation conflicts with these documents, resolve the conflict before continuing.

## 1. Mission

LatencyPilot must help a user answer what causes latency, what exact change is being tested, what improved or regressed, whether the result exceeds normal noise, and how to restore the original state.

Core loop:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

## 2. Non-goals

Do not turn LatencyPilot into a registry tweak collection, debloater, service-disabling script, opaque FPS booster, timer/HPET folklore tool, security-disabling utility, overclocking tool, or benchmark that claims physical click-to-photon latency without physical instrumentation.

## 3. Frozen current stack

Unless an ADR explicitly changes it:

- C# 14;
- .NET 10 LTS;
- **WinUI 3** desktop application;
- **Windows App SDK 2.4 Stable**;
- unpackaged, self-contained Windows 11 x64 release;
- Windows Service as the narrow privileged boundary for Phase 2 kernel observation and later Phase 3 mutation;
- Named Pipes for local typed/versioned IPC;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`;
- PresentMon where graphics telemetry is required;
- SetupAPI + Configuration Manager for device discovery;
- CPU Sets / processor topology APIs;
- Raw Input for input-report measurements;
- SQLite for durable experiment state;
- MSTest + Microsoft.Testing.Platform.

Do not add a second UI framework, persistence engine, IPC stack, DI container, MVVM framework, native component or packaging model without demonstrated need and an ADR where architecture changes.

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

`Core` owns domain invariants only. `Benchmarking` owns evidence interpretation. `Protocol` owns typed/versioned IPC contracts. `Platform.Windows` owns raw Windows APIs and interop. `Persistence` owns durable experiment/recovery state. `Service` is the narrow privileged boundary. `App` is the normal-user WinUI 3 UX.

Raw P/Invoke, SetupAPI, ConfigMgr, registry paths and privileged implementation details must not leak into Core or Protocol.

## 5. Privilege and mutation rules

The desktop application must not require permanent elevation.

Phase 2 service authority is read-only and limited to supported privileged observation. Mutation remains disabled until Phase 3 safety infrastructure exists.

Never expose arbitrary PowerShell, arbitrary process execution, arbitrary registry paths/values, generic run-as-SYSTEM, or user-provided privileged DLL/plugin loading.

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

- measure baseline variability before interpreting small deltas;
- prefer repeatable A/B-style runs over a single before/after observation;
- preserve raw samples or auditable aggregates;
- report sample count and relevant tail metrics;
- do not infer significance from percentage delta alone;
- separate target metrics from guardrails;
- treat baseline drift as a validity problem;
- represent uncertainty explicitly;
- never let a composite score hide a regression.

Authoritative verdicts remain explicit: `Improved`, `Regressed`, `Tradeoff`, `NoMeasurableDifference`, `Inconclusive`.

## 7. Hard test cap

LatencyPilot intentionally does **not** pursue high unit-test counts or coverage percentages.

**Hard repository rule: no more than 10 permanent automated tests total.**

A permanent test requires a credible high-blast-radius failure mode. Do not write permanent tests for getters, labels, trivial mappings, framework behavior, minor historical regressions, every parser branch, or every bug fix.

Temporary investigative tests are allowed while implementing/debugging interop, parsers or framework migration. Delete them before the final commit when they do not protect a lasting high-blast-radius contract.

Exceeding 10 permanent tests is prohibited unless the repository owner explicitly approves it and an ADR explains why staying at 10 would create more risk than the additional permanent test creates maintenance cost.

When a later phase introduces a more important risk, merge, replace or retire a lower-value permanent test rather than growing the suite.

Physical hardware validation, exploratory benchmark runs and release checklists are not counted as automated tests.

## 8. Hardware claims

GitHub-hosted CI is not evidence that a hardware optimization improves real hardware. Claims about GPU affinity, USB/xHCI, NIC/RSS, interrupt placement or latency improvement require physical Windows 11 evidence. Never fabricate physical results from VM data.

## 9. Windows tuning policy

Before implementing a tuning mechanism:

1. research Microsoft and authoritative vendor documentation;
2. document supported semantics and assumptions;
3. identify reboot requirements;
4. define snapshot and rollback semantics;
5. define primary and guardrail measurements;
6. identify meaningful failure modes;
7. update an ADR if architecture/policy changes.

Undocumented tweaks are not eligible for automatic application without explicit owner approval and unusually strong evidence.

## 10. ETW and telemetry

Prefer authoritative Windows providers and documented semantics. Do not silently reinterpret unknown/missing fields. Preserve source provenance where practical. Configuration hints such as registry MSI settings must not be presented as proof of active interrupt delivery; use the appropriate Windows resource/trace evidence for actual state.

For native DPC/ISR routine attribution, an address is not a driver name. Module attribution requires authoritative kernel image mapping, including already-loaded images via the appropriate rundown/CAPTURE_STATE path. If mapping evidence is missing, preserve the raw address and report attribution as unknown.

## 11. Persistence and recovery

Before any mutation is implemented, original state must be durably recorded before apply. Schema changes must preserve active recovery records. Do not rewrite historical benchmark results merely to match a newer interpretation; use versioned interpretation/migration metadata.

## 12. UI rules

The WinUI 3 app remains non-elevated and must show evidence, not marketing claims. A single short ETW capture is an **observation**, not a trustworthy baseline. The term baseline is reserved for repeated, quality-checked measurements with noise/drift handling.

For real experiments expose exact change, original/candidate state, apply verification, benchmark validity, before/after metrics, noise/uncertainty, guardrail regressions, and keep/revert/recovery state. Never show synthetic values as machine measurements.

## 13. YAGNI and source organization

Prefer cohesive modules over extreme fragmentation. Add an abstraction only when it improves current safety, clarity, testability or an already-required extension point. Do not prebuild speculative subsystems. Remove dead/speculative code instead of preserving it for hypothetical future use.

## 14. Step-back review — mandatory

Before calling any subsection, feature or phase item complete, perform a deliberate second-pass review from the opposite direction. Re-check at least:

- assumptions about Windows/API semantics;
- naming claims versus what the source actually proves;
- error/partial-data behavior;
- resource/handle lifetime and cleanup;
- privilege boundary impact;
- YAGNI violations and speculative abstractions;
- scale implications of loops, snapshots and repeated enumeration;
- test-cap compliance;
- roadmap/status/document drift;
- the repository owner's latest explicit constraints.

Ask: **“If the current design is wrong, where is the most likely hidden assumption?”**

If the review finds a contradiction, fix the design first. Do not preserve an earlier decision merely because code already exists.

## 15. Roadmap and completion discipline

`ROADMAP.md` defines what 100% means. `PROJECT_STATUS.md` records what is actually complete.

- never call a phase complete because code merely exists;
- check a roadmap item only when its required evidence exists on `main`;
- deterministic correctness contracts may use the GitHub Actions **Tests** workflow as automated evidence;
- GitHub Actions is test-only: do not add cloud solution builds, WinUI publish, installer/package builds, GUI smoke, artifact uploads or release publication unless the repository owner explicitly reverses this policy;
- build/package-dependent items require owner-local Windows build/package evidence, not hosted CI;
- hardware-dependent items require physical-hardware evidence;
- implemented-but-unverified remains incomplete;
- update status when completing or discovering a blocker;
- if scope changes, update the roadmap rather than silently redefining done.

## 16. Progress-report contract — mandatory

After every meaningful implementation stage or closed subsection, the progress report must state all five items below explicitly:

1. **Completed now** — exact code/capability completed in this stage, without inflating partial work into completion.
2. **Evidence** — commit, test-only CI run, owner-local build result, hardware result, or other proof required by the relevant gate.
3. **Still open in this stage/phase** — remaining blockers or unchecked requirements.
4. **Next stage** — the immediately following stage, broken into concrete substeps in execution order.
5. **After that** — the next one or two stages so the direction is visible and work does not become locally optimized or circular.

A report that only says “done” or only lists completed work is incomplete.

When a stage is not actually closable because tests, local build/package validation, hardware validation, attribution, quality gates, or documentation are missing, say **implemented but not closed** and name the exact missing evidence.

`PROJECT_STATUS.md` must maintain a current execution ladder with the same structure. When the next action changes, update that ladder in the same logical change or immediately afterward.

## 17. Change discipline

For every meaningful change: understand existing architecture first, keep commits logically scoped, avoid unrelated churn, update documentation when contracts change, add/modify permanent tests only under the hard-cap policy, never weaken safety logic to make tests pass, and inspect actual failures instead of disabling validation.

For **owner-directed automation work**, write directly to `main` by default and do not create or switch to a new branch unless the repository owner explicitly requests one. This constraint does not prohibit normal contributor pull-request workflows; it governs agent/automation changes performed on the owner's behalf.

## 18. Licensing

LatencyPilot is source-available and is not an OSI open-source project. Do not replace `LICENSE`, `CLA.md` or contribution terms unless explicitly instructed by the repository owner. Verify third-party license compatibility before copying or adding dependencies.

## 19. Definition of done for an optimizer

A supported optimization domain is not done until it has authoritative applicability detection, topology/device identification, original-state capture, validated candidates, safe apply and independent verification, subsystem-specific target measurement, cross-subsystem guardrails, appropriate noise/drift handling, explicit verdicts, revert/interrupted-run recovery, minimal justified permanent tests, user-facing trade-off explanation, documentation and physical-hardware validation where required.

If any of these are deferred, keep the feature experimental and unavailable to automatic recommendation.
