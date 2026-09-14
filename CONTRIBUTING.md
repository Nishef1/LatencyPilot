# Contributing to LatencyPilot

LatencyPilot welcomes issues, benchmark reports, documentation improvements, focused tests, and narrowly scoped pull requests that improve the official project.

This repository is **source-available, not open source**. Contributions are accepted only for inclusion in the official repository and are governed by [`LICENSE`](LICENSE) and [`CLA.md`](CLA.md).

## Before contributing

Read:

1. [`AGENTS.md`](AGENTS.md) — engineering rules and non-negotiable safety constraints.
2. [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md) — architecture and dependency boundaries.
3. [`ROADMAP.md`](ROADMAP.md) and [`PROJECT_STATUS.md`](PROJECT_STATUS.md) — completion gates and current execution state.
4. [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md) — measurement and experiment requirements.
5. [`CLA.md`](CLA.md) — contribution grant.

## Issues are welcome

Please open an issue for:

- reproducible crashes or service failures;
- unsafe or incomplete rollback behavior;
- benchmark anomalies;
- DPC/ISR attribution problems;
- unsupported Windows/hardware behavior;
- proposed benchmark metrics;
- documentation defects;
- proposed tuning domains.

For security-sensitive findings, follow [`SECURITY.md`](SECURITY.md) instead of opening a public issue.

## Pull requests

A pull request should be narrowly scoped and explain:

- what problem it solves;
- why the change belongs in LatencyPilot;
- which current architecture boundary it touches;
- what measurements or authoritative Windows documentation support it;
- what existing permanent test was updated, replaced, or why no permanent-test change is justified;
- whether any mutation, privilege boundary, future persistence/recovery contract, IPC contract, or rollback behavior changes;
- any known limitations or hardware-specific assumptions.

Large features should normally begin with an issue or ADR before implementation.

Current Phase 2 source boundaries are:

```text
Core
Benchmarking
Protocol
Platform.Windows
Service
App
CriticalTests
```

The future SQLite persistence boundary is a Phase 3 requirement, not a placeholder Phase 2 project. Do not add an empty `LatencyPilot.Persistence` project or speculative repository abstraction merely to reserve a namespace; introduce the real persistence boundary together with its schema, migrations, journal and recovery contract.

## Forks and local copies

GitHub's public-repository functionality may permit users to fork this repository. The project license does not grant permission to maintain or distribute an independent LatencyPilot fork or derivative product.

A GitHub fork or local modified copy may be used only as reasonably necessary to prepare and submit a contribution back to the official repository, subject to the terms of [`LICENSE`](LICENSE) and [`CLA.md`](CLA.md).

Do not publish releases, packages, mirrors, modified binaries, or standalone derivative distributions from a contribution fork.

## Engineering requirements

### No tweak without evidence

Do not add a setting because it is popular in optimization guides, scripts, videos, forum posts, or tweak packs.

A supported mutation must eventually have:

1. an applicability detector;
2. an original-state snapshot;
3. candidate validation;
4. a narrowly scoped apply operation;
5. independent post-apply verification;
6. benchmark coverage for the target subsystem;
7. collateral/guardrail metrics;
8. deterministic revert behavior;
9. interrupted-run recovery behavior;
10. durable journal/recovery state before mutation ships.

Phase 2 remains read-only. Do not add mutation commands before the Phase 3 safety substrate exists.

### One variable at a time

Experiments should isolate a single change. Combination experiments may be added only after the individual effects can be measured and attributed.

### Prefer authoritative interfaces

Prefer documented Windows APIs, ETW providers, device policies, SetupAPI/Configuration Manager, CPU Sets, Raw Input, and other supported mechanisms.

Undocumented registry values, timer folklore, blanket service disabling, security-feature disabling, or broad "gaming tweak" collections require extraordinary justification and must not enter the automatic optimization path by default.

### Preserve raw measurements

Do not replace raw measurements with a single score. Reports must preserve enough data to understand trade-offs and reproduce the verdict.

### Privilege separation

The **WinUI 3** application must remain a normal non-elevated desktop process. Privileged observation and future supported mutations belong in the narrowly scoped Windows Service and must cross the typed, versioned IPC contract.

The current Phase 2 active-console authorization rule is observation authorization only; it is not sufficient future mutation authorization.

### Rollback is part of the feature

A mutation without verified rollback and recovery is incomplete and must not be exposed as a supported optimization.

## Testing policy

LatencyPilot intentionally keeps a small permanent suite. The repository-wide hard maximum is **10 permanent automated tests** unless the owner explicitly approves an exception and an ADR explains why remaining within the cap creates greater risk.

Do **not** add one permanent test per file, branch, bug fix, getter, label, parser case, or framework behavior. A permanent test needs a credible high-blast-radius correctness or safety contract. Prefer folding scenario matrices into an existing durable test or replacing a lower-value permanent test when a more important risk appears.

The useful portfolio is deliberately small:

- deterministic domain/statistics contracts;
- fail-closed protocol/privilege-surface contracts;
- a small number of Windows read-only integration invariants;
- later, the highest-blast-radius persistence/recovery/mutation contracts.

Temporary investigative/debug tests are welcome while developing interop, parsers, migrations or recovery behavior. Remove them before finalization unless they justify one of the permanent slots.

Tests must not depend on execution order or on a developer's machine-specific mutable state. Performance microbenchmarks are separate from correctness tests, and GitHub-hosted runner timing is not trustworthy hardware-performance evidence.

GitHub Actions is intentionally **test-only**. Hosted CI does not build/publish the WinUI App or Service, create installers/packages, run GUI smoke tests, publish releases, or prove physical latency behavior. Build/package evidence remains owner-local; hardware claims require physical Windows 11 evidence.

## Benchmark evidence

When a change claims a latency or performance improvement, include enough information to distinguish the effect from normal baseline variance. Where relevant, include:

- workload and duration;
- hardware and Windows build;
- driver version;
- sample counts;
- repeated baseline/candidate runs;
- p50/p95/p99/p99.9/max or other applicable tail metrics;
- noise-floor or baseline drift information;
- uncertainty/validity context;
- guardrail regressions.

Do not describe a numerically smaller value as an improvement if the difference is inside measured baseline variability or the evidence is otherwise inconclusive.

## Code quality

- Target the SDK pinned by `global.json`.
- Keep NuGet package versions centralized in the repository-root `Directory.Packages.props`; project files should declare package usage without duplicating version numbers.
- Treat compiler/analyzer warnings as errors where the project is compiled; fix root causes rather than adding broad suppressions.
- Keep Core and Benchmarking independent of WinUI and direct Windows mutation APIs.
- Keep raw Windows interop/platform-specific code inside `LatencyPilot.Platform.Windows`, except narrow Service-boundary lifecycle/security calls that belong to the Service.
- Keep Protocol standalone unless its wire contract genuinely requires another project dependency.
- Do not bypass the Service for privileged work.
- Avoid dead code, speculative projects and placeholder abstractions.
- Prefer cohesive, reviewable changes over file-count growth or framework introduction.
- Update architecture, roadmap/status or benchmark documentation when their contracts change.

## Contribution terms

By submitting a pull request, patch, issue attachment containing code, or other contribution intended for incorporation into LatencyPilot, you confirm that you have read and agree to [`CLA.md`](CLA.md).

If you do not agree to those terms, do not submit code or other copyrightable material for incorporation into the project.
