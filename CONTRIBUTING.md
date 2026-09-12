# Contributing to LatencyPilot

LatencyPilot welcomes issues, benchmark reports, documentation improvements, tests, and focused pull requests that improve the official project.

This repository is **source-available, not open source**. Contributions are accepted only for inclusion in the official repository and are governed by [`LICENSE`](LICENSE) and [`CLA.md`](CLA.md).

## Before contributing

Read:

1. [`AGENTS.md`](AGENTS.md) — engineering rules and non-negotiable safety constraints.
2. [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md) — architecture and dependency boundaries.
3. [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md) — measurement and experiment requirements.
4. [`CLA.md`](CLA.md) — contribution grant.

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
- which architecture boundary it touches;
- what measurements or authoritative Windows documentation support it;
- what tests were added or changed;
- whether any mutation, privilege boundary, persistence format, IPC contract, or rollback behavior changes;
- any known limitations or hardware-specific assumptions.

Large features should normally begin with an issue or ADR before implementation.

## Forks and local copies

GitHub's public-repository functionality may permit users to fork this repository. The project license does not grant permission to maintain or distribute an independent LatencyPilot fork or derivative product.

A GitHub fork or local modified copy may be used only as reasonably necessary to prepare and submit a contribution back to the official repository, subject to the terms of [`LICENSE`](LICENSE) and [`CLA.md`](CLA.md).

Do not publish releases, packages, mirrors, modified binaries, or standalone derivative distributions from a contribution fork.

## Engineering requirements

### No tweak without evidence

Do not add a setting because it is popular in optimization guides, scripts, videos, forum posts, or tweak packs.

A new mutation must have:

1. an applicability detector;
2. an original-state snapshot;
3. candidate validation;
4. an apply operation;
5. independent post-apply verification;
6. benchmark coverage for the target subsystem;
7. collateral/guardrail metrics;
8. deterministic revert behavior;
9. interrupted-run recovery behavior;
10. tests for improvement, regression, no-change/inconclusive, and failure paths.

### One variable at a time

Experiments should isolate a single change. Combination experiments may be added only after the individual effects can be measured and attributed.

### Prefer authoritative interfaces

Prefer documented Windows APIs, ETW providers, device policies, SetupAPI/Configuration Manager, CPU Sets, Raw Input, and other supported mechanisms.

Undocumented registry values, timer folklore, blanket service disabling, security-feature disabling, or broad "gaming tweak" collections require extraordinary justification and must not enter the automatic optimization path by default.

### Preserve raw measurements

Do not replace raw measurements with a single score. Reports must preserve enough data to understand trade-offs and reproduce the verdict.

### Privilege separation

The WPF application must not become an always-elevated process. Privileged actions belong in the narrowly scoped Windows Service and must cross a versioned IPC contract.

### Rollback is part of the feature

A mutation without verified rollback and recovery is incomplete and must not be exposed as a supported optimization.

## Testing

Changes must add tests at the appropriate level. Expected categories include:

- pure unit tests;
- statistical/property tests;
- protocol compatibility tests;
- ETW/PresentMon golden-fixture tests;
- persistence/recovery tests;
- Windows read-only integration tests;
- privileged integration tests where safe;
- physical hardware tests for claims that cannot be validated in CI.

Tests must not depend on execution order or on a developer's machine-specific state.

Performance microbenchmarks are separate from correctness tests. GitHub-hosted runner timing is not considered a trustworthy hardware-performance benchmark.

## Benchmark evidence

When a change claims a latency or performance improvement, include enough information to distinguish the effect from normal baseline variance. Where relevant, include:

- workload and duration;
- hardware and Windows build;
- driver version;
- sample counts;
- repeated baseline/candidate runs;
- p50/p95/p99/p99.9/max or other applicable tail metrics;
- noise-floor or baseline drift information;
- confidence interval or equivalent uncertainty measure;
- guardrail regressions.

Do not describe a numerically smaller value as an improvement if the difference is inside the measured noise floor.

## Code quality

- Target the SDK pinned by `global.json` once present.
- Treat compiler/analyzer warnings as errors in CI unless explicitly justified.
- Keep Core and Benchmarking independent of WPF and direct Windows mutation APIs.
- Keep interop and platform-specific code inside `LatencyPilot.Platform.Windows`.
- Do not bypass the service for convenience.
- Avoid dead code and speculative abstractions.
- Prefer small, reviewable commits with tests.
- Update architecture or benchmark documentation when behavior changes.

## Contribution terms

By submitting a pull request, patch, issue attachment containing code, or other contribution intended for incorporation into LatencyPilot, you confirm that you have read and agree to [`CLA.md`](CLA.md).

If you do not agree to those terms, do not submit code or other copyrightable material for incorporation into the project.
