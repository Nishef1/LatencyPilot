# LatencyPilot

**Evidence-driven latency optimization for Windows 11.**

LatencyPilot is a Windows 11 performance-analysis and tuning tool built around one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, or one-click FPS booster. The product is intended to measure the effect of low-level changes on the actual machine, expose improvements and regressions, and preserve enough state to safely revert each supported experiment.

> [!IMPORTANT]
> LatencyPilot is in **pre-alpha**. Current product version: **0.0.1**. Phase 2 is building the trustworthy read-only observation and baseline-quality engine. System mutation remains disabled by design.

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

- Version — **0.0.1 pre-alpha**
- Phase 0 — governance/architecture: **closed**
- Phase 1 — buildable foundation + comparison core: **closed**
- Phase 2 — read-only observation engine: **in progress**
- Stage A — authoritative DPC/ISR module attribution: **closed historically**
- Stage B — physical Windows 11 validation: **open**
- Stage C — repeated baseline quality engine: **implemented in source, owner-local WinUI validation pending**
- System mutation capability: **none by design**
- Permanent tests: **8 / hard maximum 10**

Phase 2 contains CPU topology, PnP stable identities, driver metadata, stored interrupt configuration, allocated IRQ/resource inspection, a privileged read-only Windows Service, typed local Named Pipe IPC, DPC/ISR ETW observation with per-processor aggregation, p50/p95/p99/p99.9/max summaries, authoritative routine-address attribution against kernel image ranges, and the first repeated-baseline quality engine. Already-loaded images are recovered through kernel image rundown at session stop. Ambiguous or missing mappings remain explicitly unresolved instead of being guessed.

The observation boundary is fail-closed and bounded: unknown protocol fields are rejected, network identities are denied, the Phase 2 pipe surface is limited to interactive local identities plus required service identities, and abandoned clients cancel active capture work. App/Service failures are recorded in bounded structured local logs correlated by protocol `RequestId`.

The Stage C source adds a five-window `Build baseline` flow and deterministic `baseline-quality-v1` interpretation in `LatencyPilot.Benchmarking`. It checks capture integrity, minimum metric evidence, inter-window noise, drift and extreme windows; a lossy/noisy/drifted baseline is `Inconclusive` rather than silently accepted. The WinUI source still requires owner-local Windows compile/run evidence before that UI work is considered closed.

See [`ROADMAP.md`](ROADMAP.md) for the 100% definition, [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for the live execution ladder, [`docs/PHYSICAL_VALIDATION.md`](docs/PHYSICAL_VALIDATION.md) for Stage B, [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md) for baseline/statistics semantics, and [`docs/DIAGNOSTICS.md`](docs/DIAGNOSTICS.md) for local logging/correlation rules.

## Pre-alpha releases

Release versions use exactly three numeric components: `MAJOR.MINOR.PATCH`. `Directory.Build.props` is the product-version source of truth and `RELEASE_VERSION` is the explicit release request. Published versions are immutable; once a tag exists, later source changes require a new product version before publication.

The historical public `v0.0.1` release contains:

- `LatencyPilot-0.0.1-win-x64-setup.exe` — installer for the app and read-only observation service.
- `LatencyPilot-0.0.1-win-x64-portable.zip` — extractable portable bundle with app, service payload, runtime dependencies, service scripts, validation/diagnostics guides and build metadata.

Current `main` has advanced beyond those published `v0.0.1` artifacts. Do not treat the old binaries as evidence for the current source. The next published candidate must use a new semantic version and be built locally by the repository owner from the exact tested `main` revision.

The App runs as a normal, non-elevated user. Kernel ETW observation remains behind the privileged Windows Service. In the portable bundle, `Install-Service.ps1` copies the Service payload into `%ProgramFiles%\LatencyPilot\Service` before LocalSystem registration; the privileged binary is therefore not executed from an ordinary user-writable extraction folder. `Uninstall-Service.ps1` removes the registration and protected Service copy. See [`docs/PORTABLE.md`](docs/PORTABLE.md).

## Architecture

Current baseline:

- **Language:** C# 14
- **Runtime:** .NET 10 LTS
- **Desktop UI:** WinUI 3
- **Windows UI/runtime:** Windows App SDK 2.4 Stable
- **Distribution:** unpackaged, self-contained Windows 11 x64; installer EXE plus portable ZIP
- **Privileged boundary:** narrow Windows Service used for Phase 2 read-only kernel ETW observation; Phase 3 later extends it only after recovery/journaling safety exists
- **IPC:** typed/versioned local Named Pipes
- **Tracing:** ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`
- **Graphics telemetry:** PresentMon where applicable
- **Windows integration:** SetupAPI, Configuration Manager, CPU topology/CPU Sets, Raw Input and documented device-policy APIs
- **Persistence:** SQLite when durable experiment/recovery state is introduced
- **Diagnostics:** `Microsoft.Extensions.Logging` Service abstraction plus bounded Serilog compact-JSON rolling files
- **Tests:** MSTest + Microsoft.Testing.Platform, hard maximum 10 permanent automated tests

The desktop application remains non-elevated. Privileged observation and all future privileged mutation cross the narrow Service boundary. Mutation-specific commands are not present in Phase 2, and the current observation ACL is not considered sufficient future mutation authorization.

See [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md), [`AGENTS.md`](AGENTS.md) and [`docs/adr/`](docs/adr/).

## Evidence levels matter

LatencyPilot deliberately distinguishes:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

For example, a registry `MSISupported` value is not presented as proof that MSI/MSI-X is actively delivering interrupts at runtime. Naming and UI claims must match what the underlying Windows source actually proves.

Likewise, a raw DPC/ISR routine address is not presented as a driver name unless authoritative kernel image mapping resolves it. The current observation engine tracks image load/unload lifetime, consumes stop-time image rundown for modules that predate the capture, rejects invalid ranges, and leaves overlapping/missing mappings unresolved.

Partial device metadata is preserved where possible. An unreadable optional property/resource no longer implies the entire present-device inventory is invalid.

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

All current percentile reporting uses the same documented linear interpolation estimator from `LatencyPilot.Benchmarking.Statistics.Percentiles`; the Service and baseline engine do not define a second percentile rule.

`baseline-quality-v1` currently requires five sequential windows, at least 20 events per DPC/ISR metric per clean window, <=30% relative P10-P90 spread, <=20% early/late drift and no >50% extreme-window deviation. These are conservative versioned quality-policy thresholds, not statistical-significance claims.

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
  DIAGNOSTICS.md
  PHYSICAL_VALIDATION.md
  PORTABLE.md
  RELEASING.md
  adr/
```

## Testing policy

The permanent automated suite is intentionally small and has a **hard repository-wide maximum of 10 tests**. The current suite uses eight broader permanent test methods covering state-machine safety, benchmark verdict semantics, repeated-baseline quality, invalid metrics, canonical percentiles, fail-closed protocol framing, the read-only protocol surface and a real Windows inventory/topology invariant.

Test count is not a quality goal. When a newer parser/recovery/mutation risk has greater blast radius, merge or remove a lower-value permanent test and reuse that slot. Scenario matrices should be consolidated inside a durable contract test when practical. Temporary implementation/debug tests may be created and deleted before finalization.

Hardware validation is separate from automated tests and does not count toward the permanent-test cap.

## Diagnostics

Operational diagnostics are local-first and intentionally separate from benchmark evidence.

Desktop App logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Service logs:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

They use compact JSON, bounded rolling/retention and async file writes. The protocol `RequestId` connects App-side request entries to Service-side capture entries. LatencyPilot does not log one event per raw DPC/ISR sample because diagnostic I/O must not become part of the latency measurement workload. See [`docs/DIAGNOSTICS.md`](docs/DIAGNOSTICS.md).

## Building and automation

The .NET SDK is pinned in `global.json`.

GitHub Actions is intentionally **test-only**. It runs:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Hosted Actions does not publish the App/Service, build Setup/portable distributions, run production release packaging or publish GitHub releases.

### Fast local development

Daily WinUI work should use **Debug**, not `publish` or the installer pipeline.

Visual Studio is the recommended inner loop for UI work:

1. Open `LatencyPilot.slnx` and make `LatencyPilot.App` the startup project.
2. Select the `LatencyPilot.App (Hot Reload)` launch profile.
3. Start with `F5` so the managed debugger is attached.
4. Save supported XAML/C# edits to apply Hot Reload.

`HotReloadAutoRestart` is enabled for Debug builds. For a command-line loop:

```powershell
.\dev.ps1
.\dev.ps1 -Mode run
.\dev.ps1 -Mode build
.\dev.ps1 -ForceRestore
```

The script keeps the normal NuGet global package cache and uses `--no-restore` after a valid restore state exists.

### Owner-local release build

Release/package creation is an explicit owner action from a clean, current `main` checkout after the exact commit has green Tests evidence:

```powershell
.\scripts\Publish-Release.ps1
```

That script performs the Release build, App/Service publish, PRI validation, Setup/portable creation, checksums and GitHub prerelease publication locally. See [`docs/RELEASING.md`](docs/RELEASING.md).

For manual local build/debug without publishing:

```powershell
dotnet restore LatencyPilot.slnx
dotnet build LatencyPilot.slnx -c Release
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

None of those future mutation steps are implied merely by the existence of the Phase 2 read-only Service.

## Progress discipline

Every meaningful implementation stage must report what completed, the evidence, what remains open, the exact next stage with ordered substeps, and what follows that stage. `PROJECT_STATUS.md` keeps this execution ladder so progress cannot depend on chat memory.

## Contributing

Issues and upstream contributions are welcome under [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`CLA.md`](CLA.md). Changes must preserve measurement-first/rollback-first architecture, YAGNI, the permanent-test cap, and the mandatory step-back review in `AGENTS.md`.

## License

**LatencyPilot is source-available but is not open source.** Personal, non-commercial use and upstream contribution are permitted subject to [`LICENSE`](LICENSE). Commercial use, redistribution, mirrors and standalone derivative distributions require separate written permission.

## Security

Do not publicly disclose vulnerabilities that could enable privilege escalation, unsafe device-policy mutation, arbitrary service commands, diagnostics-data exposure or recovery bypass. Follow [`SECURITY.md`](SECURITY.md).

---

Copyright © 2026 Nishef1. All rights reserved.
