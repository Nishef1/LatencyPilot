# ADR 0001: Project foundation

- **Status:** Accepted
- **Date:** 2026-09-12

## Context

LatencyPilot needs low-level Windows observability and controlled privileged mutation while remaining practical to develop, test, package, and recover safely.

The project also needs a benchmark model that can distinguish real effects from normal run-to-run variation rather than shipping universal tweak presets.

## Decision

### Platform

V0.1 targets **Windows 11 x64** only.

### Language/runtime

Use **C# 14 on .NET 10 LTS** as the primary implementation stack.

Managed code is the default even for ETW analysis. A native C++ component may be introduced later only if profiling proves a concrete requirement that managed implementations cannot satisfy acceptably.

### Desktop UI

Use **WPF** for the desktop application.

The desktop application remains non-elevated during normal operation.

### Privileged boundary

Use a **Windows Service** for privileged mutations and recovery-sensitive operations.

The desktop application and service communicate through **versioned Named-Pipe IPC** with a narrow typed command allow-list.

The service must not expose arbitrary PowerShell, shell, process, or registry execution primitives.

### Observation

Use **ETW** with `Microsoft.Diagnostics.Tracing.TraceEvent` as the primary DPC/ISR and supported subsystem tracing mechanism.

Use **PresentMon** where applicable for graphics/frame telemetry.

Use documented Windows APIs such as SetupAPI, Configuration Manager, CPU Sets, processor topology APIs, and Raw Input instead of parsing UI output or relying on broad shell scripts.

### Persistence

Use **SQLite** for experiment history, snapshots, benchmark results, migration state, and durable recovery journals.

### Testing

Use **MSTest + Microsoft.Testing.Platform** as the baseline test stack.

Correctness tests, golden telemetry fixtures, statistical/property tests, Windows integration tests, and physical-hardware tests are distinct categories.

GitHub-hosted runner timing is not treated as evidence of physical latency improvement.

### Release model

Produce **self-contained Windows x64** release artifacts so end users do not need to install a separate .NET runtime.

Local development is optional; GitHub Actions may restore the pinned SDK and build/test/release the project.

### Optimization contract

LatencyPilot is not a tweak pack. Every supported mutation must satisfy:

```text
Detect
→ Snapshot
→ Validate
→ Journal
→ Apply
→ Verify
→ Benchmark
→ Compare
→ Keep/Revert
→ Verify final state
→ Close journal
```

### Result semantics

The benchmark system must preserve raw/underlying metrics and support explicit outcomes such as:

```text
ConfirmedImprovement
ConfirmedRegression
TradeOff
NoMeasurableDifference
Inconclusive
InvalidExperiment
```

A single aggregate score is never the sole authoritative result.

## Consequences

### Benefits

- Fast development while retaining deep Windows API access.
- Mature ETW tooling through the .NET ecosystem.
- Clear separation between non-elevated UX and privileged mutations.
- Reproducible, auditable experiment history.
- Safe path to introduce native code only where measured need exists.
- Benchmark methodology remains testable independently of hardware where possible.

### Costs

- Windows-only product architecture.
- Windows Service installation/recovery complexity.
- WPF is intentionally chosen over newer cross-platform UI frameworks.
- Hardware-specific claims still require physical test machines; CI cannot prove them.
- Conservative mutation/recovery requirements make features slower to implement than ordinary tweak utilities.

## Rejected alternatives

### Rust/Tauri as the primary stack

Technically viable, but it would provide less direct leverage from the existing TraceEvent/PerfView ecosystem and adds a second frontend/runtime model without a demonstrated benefit for V0.1.

### C++ for the entire application

Offers maximum low-level control but increases implementation complexity and memory-safety risk without evidence that managed ETW/Windows interop is insufficient.

### WinUI 3

Provides a newer Windows UI stack but adds Windows App SDK deployment complexity that is not justified for this specialized Windows utility at project start.

### Always-elevated desktop process

Rejected because it unnecessarily expands the privileged attack surface and makes every UI/library component part of the administrative trust boundary.

### One-click bulk tuning

Rejected because it prevents causal attribution and makes regressions difficult to identify or revert safely.

## Superseding this ADR

Any change to the primary language/runtime, UI framework, privileged-process model, IPC trust model, persistence engine, or measurement-first mutation contract requires a new ADR that explicitly supersedes the relevant part of this decision.
