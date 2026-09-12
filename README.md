# LatencyPilot

**Evidence-driven latency optimization for Windows 11.**

LatencyPilot is a Windows 11 performance-analysis and tuning tool built around a simple rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, or "one-click FPS booster." LatencyPilot is intended to measure the effect of low-level changes on the actual machine, quantify improvements and regressions, and preserve enough state to safely revert each experiment.

> [!IMPORTANT]
> LatencyPilot is in **pre-alpha**. There is no stable release yet. Do not treat repository code, screenshots, plans, or benchmark methodology as a production-ready optimization recommendation.

## Why LatencyPilot

Windows exposes powerful mechanisms for interrupt affinity, MSI/MSI-X, CPU topology, ETW tracing, USB, networking, and scheduling, but tuning them manually is difficult and hardware-specific. A change that helps one machine can be neutral or harmful on another.

LatencyPilot is designed to answer questions such as:

- Which drivers and devices contribute most to DPC/ISR load?
- Is CPU 0 overloaded while other logical processors remain lightly used?
- Does moving a device's interrupt affinity actually improve tail latency?
- Does a GPU-affinity change improve frame-time consistency while harming USB, audio, or network latency?
- Is an apparent improvement larger than the machine's normal measurement noise?
- Can every applied change be verified and reverted safely?

## Core principles

1. **No tweak without evidence.** A change is not recommended because it is popular on forums or appears in a tweak pack.
2. **One variable at a time.** Experiments must isolate a change before combination testing is considered.
3. **Tail latency matters.** p95, p99, p99.9, max, variability, and sample counts matter more than averages alone.
4. **Measure collateral effects.** A GPU improvement is not automatically a system improvement if input, network, audio, stability, or power behavior regresses.
5. **Know the noise floor.** Baseline-to-baseline variance must be measured before interpreting small deltas.
6. **Rollback first.** Snapshot, validation, verification, crash recovery, and revert are part of the feature—not cleanup work.
7. **No universal magic settings.** Hardware topology, drivers, firmware, workloads, and Windows builds differ.
8. **Raw results remain visible.** A single score must never hide the underlying measurements or trade-offs.

## Planned V0.1 scope

The first usable milestone is deliberately narrow:

```text
System inventory
    ↓
CPU topology
    ↓
Device / interrupt inventory
    ↓
ETW baseline capture
    ↓
DPC / ISR analysis
    ↓
GPU interrupt-affinity candidates
    ↓
One-at-a-time experiments
    ↓
Repeated measurements
    ↓
Statistical comparison
    ↓
KEEP / REVERT / INCONCLUSIVE
    ↓
History + full rollback
```

USB/xHCI, NIC/RSS, audio, storage, and additional tuning domains are planned only after this path is reliable end-to-end.

## Benchmark philosophy

Every experiment should retain both the target metrics and system-wide guardrails. Depending on the subsystem, a report may include:

- DPC/ISR duration distributions and per-CPU load
- driver/module attribution
- p50 / p90 / p95 / p99 / p99.9 / max
- sample count, dispersion, and outliers
- baseline noise floor
- bootstrap confidence intervals
- CPU-core imbalance
- frame-time and PresentMon metrics
- Raw Input report interval/jitter
- USB ETW events
- NDIS/RSS/network guardrails
- audio/stability guardrails where measurable

Results are expected to distinguish **confirmed improvement**, **confirmed regression**, **trade-off**, **no measurable difference**, and **inconclusive** rather than forcing every run into a single score.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md) for the benchmark contract.

## Architecture

Planned stack:

- **Language:** C# 14
- **Runtime:** .NET 10 LTS
- **Desktop UI:** WPF
- **Privileged operations:** Windows Service
- **IPC:** Named Pipes with explicit command contracts
- **Tracing:** ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`
- **Graphics telemetry:** PresentMon integration
- **Windows integration:** SetupAPI, Configuration Manager, CPU Sets, Raw Input, registry/device policy APIs
- **Persistence:** SQLite
- **Tests:** MSTest + Microsoft.Testing.Platform
- **Release target:** Windows 11 x64, self-contained

The desktop application must not run permanently elevated. Privileged mutations belong in a narrowly scoped service with explicit validation, authorization, journaling, and recovery boundaries.

See [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md) for the authoritative architecture and [`AGENTS.md`](AGENTS.md) for implementation rules that apply to both humans and coding agents.

## Safety model

A tunable feature is incomplete until it supports the full lifecycle:

```text
Detect applicability
    ↓
Snapshot original state
    ↓
Validate candidate
    ↓
Apply
    ↓
Verify actual applied state
    ↓
Benchmark
    ↓
Keep or revert
```

If LatencyPilot, its service, the benchmark workload, or Windows terminates unexpectedly, the next startup must identify incomplete experiments and recover deterministically.

Low-level Windows tuning can cause instability, degraded performance, networking problems, device failures, or boot/recovery issues. LatencyPilot will therefore prefer documented Windows mechanisms and conservative defaults over undocumented tweak folklore.

## Repository layout

The planned repository structure is documented in [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md). Major boundaries are expected to include:

```text
src/
  LatencyPilot.Core
  LatencyPilot.Benchmarking
  LatencyPilot.Protocol
  LatencyPilot.Platform.Windows
  LatencyPilot.Persistence
  LatencyPilot.Service
  LatencyPilot.App

tests/
  unit, integration, fixture/golden, Windows, UI, and hardware test projects

perf/
  microbenchmarks

test-assets/
  deterministic ETW / PresentMon / topology fixtures
```

## Building

The project is designed so contributors do not need to rely on a maintainer's local toolchain. CI will restore a pinned .NET 10 SDK and build/test on GitHub-hosted Windows runners.

Local development will eventually use the SDK pinned in `global.json`. Release artifacts will be self-contained so end users do not need to install the .NET runtime separately.

## Contributing

Issues and upstream contributions are welcome under the rules in [`CONTRIBUTING.md`](CONTRIBUTING.md).

Important points:

- Open an issue for bugs, benchmark anomalies, unsafe behavior, or proposed tuning domains.
- Changes must preserve the measurement-first and rollback-first architecture.
- Every new mutation requires applicability checks, snapshot/apply/verify/revert behavior, benchmark coverage, and failure-path tests.
- By submitting a contribution, you agree to the contribution terms in [`CLA.md`](CLA.md).

## License

**LatencyPilot is source-available but is not open source.**

The source is provided for personal, non-commercial use and for contributing improvements back to this repository. Commercial use, redistribution, mirrors, standalone derivative distributions, sublicensing, or selling the software or substantial portions of it are not permitted without separate written permission.

Because this repository is public on GitHub, GitHub's Terms of Service may allow platform-level viewing and forking within GitHub. Such platform rights do **not** grant a broader right to commercially use, redistribute, publish mirrors, or independently distribute modified versions of LatencyPilot.

See [`LICENSE`](LICENSE) for the complete terms.

## Security

Please do not publicly disclose vulnerabilities that could enable privilege escalation, unsafe device-policy mutation, arbitrary service commands, or recovery bypass. Follow [`SECURITY.md`](SECURITY.md).

---

Copyright © 2026 Nishef1. All rights reserved.