# LatencyPilot

**Evidence-driven latency optimization for Windows 11.**

LatencyPilot is a Windows 11 performance-analysis and tuning tool built around one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, or one-click FPS booster. The product is intended to measure the effect of low-level changes on the actual machine, expose improvements and regressions, and preserve enough state to safely revert each supported experiment.

> [!IMPORTANT]
> LatencyPilot is in **pre-alpha**. Current Phase 1 builds are observation-only foundations. They do not apply interrupt-affinity, MSI, registry, power, network, or other tuning changes.

## Why LatencyPilot

Windows exposes powerful mechanisms for interrupt affinity, MSI/MSI-X, CPU topology, ETW tracing, USB, networking, and scheduling, but manual tuning is difficult and hardware-specific. A change that helps one machine can be neutral or harmful on another.

LatencyPilot is designed to answer questions such as:

- Which drivers and devices contribute most to DPC/ISR load?
- Is CPU 0 overloaded while other logical processors remain lightly used?
- Does moving a device's interrupt affinity actually improve tail latency?
- Does a GPU-affinity change improve frame-time consistency while harming USB, audio, or network latency?
- Is an apparent improvement larger than the machine's normal measurement noise?
- Can every applied change be verified and reverted safely?

## Product contract

1. **No tweak without evidence.** Popular forum advice is not evidence.
2. **One variable at a time.** Combination testing comes only after isolated effects are understood.
3. **Tail latency matters.** p95/p99/p99.9/max and variability matter more than averages alone.
4. **Measure collateral effects.** A target-subsystem win may still be a system-wide trade-off.
5. **Know the noise floor.** Small deltas inside baseline variability are not improvements.
6. **Rollback first.** Snapshot, journaling, verification, recovery, and revert are part of the feature.
7. **No universal magic settings.** Hardware topology, drivers, firmware, workloads, and Windows builds differ.
8. **Raw evidence stays visible.** A composite score must never hide the underlying measurements.

## Roadmap and current status

[`ROADMAP.md`](ROADMAP.md) is the authoritative definition of what **100% / 1.0** means and what must be complete before each phase can close.

[`PROJECT_STATUS.md`](PROJECT_STATUS.md) is the live execution ledger. A phase is not complete because code merely exists; build-dependent items require CI evidence and hardware-dependent items require physical-machine evidence.

Current state:

- Phase 0 — governance/architecture: **closed**
- Phase 1 — buildable foundation + trustworthy comparison core: **in progress**
- System mutation capability: **none by design**

The major path to 1.0 is:

```text
Foundation / comparison core
        ↓
Read-only ETW baseline + attribution
        ↓
Safe mutation platform + GPU affinity
        ↓
USB/xHCI + input analysis
        ↓
NIC/RSS optimization
        ↓
Cross-subsystem bounded Auto mode
        ↓
Production hardening / 1.0
```

## Benchmark philosophy

Every real experiment must retain both target metrics and system-wide guardrails. Depending on the subsystem, evidence may include:

- DPC/ISR duration distributions and per-CPU load;
- driver/module attribution;
- p50 / p95 / p99 / p99.9 / max;
- sample count and dispersion;
- baseline noise/drift;
- frame-time and PresentMon metrics;
- Raw Input report interval/jitter;
- USB ETW or NDIS/RSS data;
- audio/stability guardrails where measurable.

Results distinguish **Improved**, **Regressed**, **Tradeoff**, **NoMeasurableDifference**, and **Inconclusive** rather than forcing every run into a single score.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md).

## Architecture

Frozen baseline:

- **Language:** C# 14
- **Runtime:** .NET 10 LTS
- **Desktop UI:** WPF
- **Privileged operations:** narrowly scoped Windows Service when mutation begins
- **IPC:** versioned Named Pipes
- **Tracing:** ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`
- **Graphics telemetry:** PresentMon where applicable
- **Windows integration:** SetupAPI, Configuration Manager, CPU Sets, Raw Input, documented device-policy APIs
- **Persistence:** SQLite when durable experiment state is introduced
- **Tests:** MSTest + Microsoft.Testing.Platform
- **Release target:** Windows 11 x64, self-contained

The desktop application remains non-elevated during normal operation. Privileged mutations must cross a narrow service boundary with explicit validation, journaling, verification and recovery.

See [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md) and [`AGENTS.md`](AGENTS.md).

## Repository layout

```text
src/
  LatencyPilot.Core/
  LatencyPilot.Benchmarking/
  LatencyPilot.Protocol/
  LatencyPilot.Platform.Windows/
  LatencyPilot.Persistence/
  LatencyPilot.Service/
  LatencyPilot.App/

tests/
  LatencyPilot.CriticalTests/

docs/
  benchmark and architecture decisions

.github/
  CI and contribution templates
```

The automated test suite is intentionally small: normally **5–10 high-value critical tests total**. The project does not pursue coverage percentages or a regression test for every implementation detail. Hardware validation is separate from automated CI tests.

## Building

The SDK is pinned in `global.json`. Contributors may build locally with that .NET 10 SDK, but local .NET installation is not required just to consume CI artifacts.

GitHub Actions builds on Windows, runs the focused critical suite, and publishes a self-contained Windows x64 artifact.

Typical local commands, if the SDK is installed:

```powershell
dotnet restore LatencyPilot.slnx
dotnet build LatencyPilot.slnx -c Release
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj -c Release
dotnet publish src/LatencyPilot.App/LatencyPilot.App.csproj -c Release -r win-x64 --self-contained true
```

## Safety model

A system-changing feature is incomplete until it supports:

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

If LatencyPilot, its service, a workload, or Windows terminates unexpectedly, recovery must not silently assume success.

## Contributing

Issues and upstream contributions are welcome under [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`CLA.md`](CLA.md).

Changes must preserve the measurement-first and rollback-first architecture. New tests are expected only for high-blast-radius safety/correctness behavior under the focused-test policy in `AGENTS.md`.

## License

**LatencyPilot is source-available but is not open source.**

The source is provided for personal, non-commercial use and for contributing improvements back to this repository. Commercial use, redistribution, mirrors, standalone derivative distributions, sublicensing, or selling the software or substantial portions of it are not permitted without separate written permission.

Because this repository is public on GitHub, GitHub's Terms of Service may allow platform-level viewing and forking within GitHub. Those platform rights do not grant broader commercial-use or redistribution rights.

See [`LICENSE`](LICENSE) for the complete terms.

## Security

Do not publicly disclose vulnerabilities that could enable privilege escalation, unsafe device-policy mutation, arbitrary service commands, or recovery bypass. Follow [`SECURITY.md`](SECURITY.md).

---

Copyright © 2026 Nishef1. All rights reserved.
