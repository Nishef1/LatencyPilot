# LatencyPilot

**Evidence-driven latency optimization for Windows 11.**

LatencyPilot is a Windows 11 performance-analysis and tuning tool built around one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, or one-click FPS booster. The product is intended to measure the effect of low-level changes on the actual machine, expose improvements and regressions, and preserve enough state to safely revert each supported experiment.

> [!IMPORTANT]
> LatencyPilot is in **pre-alpha**. Phase 2 is currently building the trustworthy read-only observation engine. System mutation remains disabled by design.

## Why LatencyPilot

Windows exposes powerful interrupt, CPU-topology, ETW, USB, networking and scheduling mechanisms, but manual tuning is hardware-specific and difficult to validate. LatencyPilot is designed to answer questions such as:

- Which drivers/devices contribute most to DPC/ISR load?
- Is one CPU handling disproportionate interrupt work?
- Does an interrupt-affinity change actually improve tail latency?
- Does a local improvement harm USB, audio, network or frame-time behavior?
- Is an apparent gain larger than normal baseline noise?
- Can every supported change be independently verified and reverted?

## Product contract

1. **No tweak without evidence.**
2. **One variable at a time before combination testing.**
3. **Tail latency matters.** p95/p99/p99.9/max and variability matter more than averages alone.
4. **Measure collateral effects.** A local win may still be a system-wide trade-off.
5. **Know the noise floor.** Small deltas inside baseline variability are not improvements.
6. **Rollback first.** Snapshot, journal, verification, recovery and revert are part of the feature.
7. **No universal magic settings.** Hardware, firmware, drivers, workloads and Windows builds differ.
8. **Raw evidence stays visible.** A composite score never hides underlying measurements.

## Current state

- Phase 0 — governance/architecture: **closed**
- Phase 1 — buildable foundation + comparison core: **closed**
- Phase 2 — read-only observation engine: **in progress**
- System mutation capability: **none by design**
- Permanent tests: **9 / hard maximum 10**

Phase 2 currently contains CPU topology, PnP stable identities, driver metadata, stored interrupt configuration, allocated IRQ/resource inspection, a privileged read-only Windows Service, typed local Named Pipe IPC, and DPC/ISR ETW observation with per-processor aggregation plus p50/p95/p99/p99.9/max summaries.

The major Phase 2 work still open is authoritative DPC/ISR module attribution, physical Windows 11 validation, repeated baseline/noise/drift quality analysis, and the final evidence UX. A short single capture is intentionally called an **observation**, not a trustworthy baseline.

See [`ROADMAP.md`](ROADMAP.md) for the 100% definition and phase transitions, and [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for the detailed current execution ladder.

## Architecture

Current baseline:

- **Language:** C# 14
- **Runtime:** .NET 10 LTS
- **Desktop UI:** WinUI 3
- **Windows UI/runtime:** Windows App SDK 2.4 Stable
- **Distribution:** unpackaged, self-contained Windows 11 x64
- **Privileged boundary:** narrow Windows Service already used for Phase 2 read-only kernel ETW observation; Phase 3 later extends it for validated/recoverable mutation
- **IPC:** typed/versioned local Named Pipes
- **Tracing:** ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`
- **Graphics telemetry:** PresentMon where applicable
- **Windows integration:** SetupAPI, Configuration Manager, CPU topology/CPU Sets, Raw Input and documented device-policy APIs
- **Persistence:** SQLite when durable experiment/recovery state is introduced
- **Tests:** MSTest + Microsoft.Testing.Platform, hard maximum 10 permanent automated tests

The desktop application remains non-elevated. Privileged observation and all future privileged mutation cross the narrow Service boundary. Mutation-specific commands are not present in Phase 2.

See [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md), [`AGENTS.md`](AGENTS.md) and [`docs/adr/`](docs/adr/).

## Evidence levels matter

LatencyPilot deliberately distinguishes:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

For example, a registry `MSISupported` value is not presented as proof that MSI/MSI-X is actively delivering interrupts at runtime. Naming and UI claims must match what the underlying Windows source actually proves.

Likewise, a raw DPC/ISR routine address is not presented as a driver name unless authoritative kernel image mapping resolves it. Already-loaded native modules require the appropriate image rundown/CAPTURE_STATE evidence rather than future ImageLoad events alone.

## Benchmark philosophy

Depending on subsystem, evidence may include:

- DPC/ISR duration distributions and per-CPU load;
- driver/module attribution;
- p50 / p95 / p99 / p99.9 / max;
- sample count and dispersion;
- baseline noise/drift;
- frame-time and PresentMon metrics;
- Raw Input report interval/jitter;
- USB ETW or NDIS/RSS evidence;
- audio/stability guardrails where measurable.

Results distinguish **Improved**, **Regressed**, **Tradeoff**, **NoMeasurableDifference**, and **Inconclusive** rather than forcing every run into one score.

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
  BENCHMARK_METHODOLOGY.md
  adr/
```

## Testing policy

The permanent automated suite is intentionally small and has a **hard repository-wide maximum of 10 tests**. Tests are reserved for high-blast-radius correctness/safety contracts. Temporary implementation/debug tests may be created and deleted before finalization.

Hardware validation is separate from CI and does not count toward the permanent-test cap.

## Building

The .NET SDK is pinned in `global.json`. GitHub Actions restores the toolchain, builds Release, runs the permanent critical suite, publishes self-contained Windows x64 App and Service outputs, and uploads them together as the Windows artifact.

Typical local commands, if the SDK is installed:

```powershell
dotnet restore LatencyPilot.slnx
dotnet build LatencyPilot.slnx -c Release
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj -c Release
dotnet publish src/LatencyPilot.App/LatencyPilot.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/LatencyPilot.Service/LatencyPilot.Service.csproj -c Release -r win-x64 --self-contained true
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

## Progress discipline

Every meaningful implementation stage must report what completed, the evidence, what remains open, the exact next stage with ordered substeps, and what follows that stage. `PROJECT_STATUS.md` keeps this execution ladder so progress cannot depend on chat memory.

## Contributing

Issues and upstream contributions are welcome under [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`CLA.md`](CLA.md). Changes must preserve measurement-first/rollback-first architecture, YAGNI, the permanent-test cap, and the mandatory step-back review in `AGENTS.md`.

## License

**LatencyPilot is source-available but is not open source.** Personal, non-commercial use and upstream contribution are permitted subject to [`LICENSE`](LICENSE). Commercial use, redistribution, mirrors and standalone derivative distributions require separate written permission.

## Security

Do not publicly disclose vulnerabilities that could enable privilege escalation, unsafe device-policy mutation, arbitrary service commands or recovery bypass. Follow [`SECURITY.md`](SECURITY.md).

---

Copyright © 2026 Nishef1. All rights reserved.
