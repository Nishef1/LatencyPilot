# LatencyPilot

**Evidence-driven latency experimentation for Windows 11.**

LatencyPilot is built around one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, one-click FPS booster, or a list of settings assumed to be universally better. The product measures the actual machine, preserves the evidence behind a decision, and requires every supported system change to be attributable, verifiable and reversible.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Phase 2 physical closure is still open while ADR 0004 permits targeted Phase 3 safety/candidate source work. The public protocol remains read-only and user-reachable mutation is **unavailable/unarmed**.

## Current state

- Phase 0 — governance/architecture: **closed**
- Phase 1 — buildable foundation/comparison core: **closed**
- Phase 2 — trustworthy read-only observation + decision baseline: **physical closure in progress**
- Phase 3 — safe mutation substrate + GPU interrupt experiment: **source implementation in progress; mutation not armed**
- Public observation protocol — **v6**, `GetStatus` + `CaptureKernelLatency` only
- Evidence schema — **`latencypilot-evidence-v8`**
- Repeated baseline method — **`baseline-quality-v2`**
- Permanent automated tests — **9 / hard maximum 10**
- Hosted CI — **test-only**

[`PROJECT_STATUS.md`](PROJECT_STATUS.md) is the live execution ledger. [`ROADMAP.md`](ROADMAP.md) defines the product/phase exit gates. Do not infer completion from source presence alone.

## Why LatencyPilot

Windows exposes interrupt, CPU-topology, ETW, USB, networking and scheduling controls, but hardware-specific advice is easy to overgeneralize. LatencyPilot is intended to answer questions such as:

- Which drivers contribute most to DPC/ISR work?
- Is one CPU handling disproportionate interrupt work?
- Is that concentration repeatable under the workload that matters?
- Does a candidate actually improve tail/frame/input behavior?
- Does a local win cause a regression in another subsystem?
- Is an apparent gain larger than normal run-to-run variation?
- Can the exact original state be restored after interruption or failure?

## Product contract

1. **No tweak without evidence.**
2. **One variable at a time before combination testing.**
3. **Quick diagnosis is not benchmark proof.**
4. **Tail latency matters, but extreme percentiles require enough samples.**
5. **Measure collateral effects.** A local win may still be a trade-off.
6. **Know the noise floor.** A small delta inside normal variability is not an improvement.
7. **Rollback first.** Snapshot, durable journal, verification and recovery are part of the feature.
8. **No universal magic settings.** Hardware, firmware, drivers, workloads and Windows builds differ.
9. **Raw evidence remains visible.** A composite score never replaces the underlying metrics.
10. **Documented platform semantics define what a setting means; local measurement decides whether it helps.**

## Current architecture

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
```

Responsibilities are deliberately narrow:

- `Core` — stable domain concepts and invariants;
- `Benchmarking` — percentile/statistical, baseline-quality, comparison and verdict logic;
- `Protocol` — typed/versioned local IPC contracts only;
- `Platform.Windows` — ETW, SetupAPI/ConfigMgr, topology, device/resource and other Windows-specific mechanisms;
- `Persistence` — concrete SQLite mutation journal/recovery state;
- `Service` — privileged boundary for kernel observation and internal fail-closed mutation substrate;
- `App` — non-elevated WinUI 3 UX;
- `CriticalTests` — deliberately small high-blast-radius deterministic suite.

Current stack:

- C# 14 / .NET 10 LTS;
- WinUI 3 / Windows App SDK 2.4 Stable;
- Windows 11 x64;
- unpackaged self-contained App;
- narrow Windows Service;
- typed/versioned Named Pipes;
- ETW via `Microsoft.Diagnostics.Tracing.TraceEvent`;
- SetupAPI + Configuration Manager + documented processor topology/CPU-set APIs;
- SQLite via `Microsoft.Data.Sqlite` for the concrete Phase 3 journal;
- PresentMon API where graphics/frame telemetry is applicable;
- Serilog for bounded structured local diagnostics;
- MSTest + Microsoft.Testing.Platform for the capped permanent suite.

NuGet package versions are centrally managed in [`Directory.Packages.props`](Directory.Packages.props). Project files declare package usage without repeating version numbers.

The App remains non-elevated. The installed Service owns privileged operations. There is no generic privileged shell/process/registry execution surface.

## Measurement products

### Quick diagnostic snapshot

```text
1 × 5 seconds
purpose = quick-diagnostic-snapshot
```

Used for capture integrity, attribution, CPU concentration, local tail/reference context and hypothesis generation. It is **not** a system-health verdict, stability proof or optimization recommendation.

### Repeated decision baseline

```text
workload already warmed/repeatable where applicable
5 s LatencyPilot/service settle
5 × 20 s authoritative windows
750 ms inter-window settle
purpose = repeated-decision-baseline
method = baseline-quality-v2
```

A window is eligible only when duration, capture integrity and sample requirements pass. Across the five windows, the versioned noise/drift gates must also pass. `Valid` means repeatable enough for the current comparison method; it does **not** mean that the machine is globally healthy or optimal.

Current p99.9 policy withholds p99.9 until an individual distribution contains at least **10,000 samples**.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md).

## Evidence semantics

LatencyPilot deliberately keeps these evidence levels separate:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Examples:

- registry interrupt settings are stored configuration;
- ConfigMgr resources are assigned-resource evidence;
- ETW DPC/ISR events are runtime behavior.

A stored `MSISupported=1` value is therefore not presented as proof that MSI/MSI-X is actively delivering interrupts at runtime.

Evidence-v8 also separates:

```text
quick-diagnostic-snapshot
repeated-decision-baseline
```

Saved evidence carries source/protocol/scenario provenance and SHA-256 verification metadata.

Use the verifier on Windows:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-observation-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture
```

For a decision-grade baseline:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-baseline-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture `
  -RequireValidBaseline
```

See [`docs/PHYSICAL_VALIDATION.md`](docs/PHYSICAL_VALIDATION.md).

## Phase 3 mutation substrate

Phase 3 source work is intentionally ahead of public mutation reachability. Implemented source includes:

- concrete SQLite durable mutation journal;
- exact original-state capture before owned writes;
- compare-and-swap journal revisions and unresolved-entry blocking;
- fail-closed startup recovery inspection;
- GPU interrupt-affinity applicability/candidate generation;
- exact stored-state apply/revert verification;
- exact-target SetupAPI device refresh/restart checks;
- recovery behavior that refuses blind overwrite after divergence;
- runtime GPU ISR processor-placement evidence;
- PresentMon API discovery/correlation and workload-metric capture.

This does **not** mean mutation is enabled. The public protocol still exposes only read-only observation commands. Physical restart/apply/rollback/reboot-recovery evidence is required before mutation-specific IPC can be designed and armed.

The GPU experiment direction remains:

```text
baseline/control
→ topology-aware bounded candidates
→ journaled apply + stored/runtime verification
→ target + guardrail measurement
→ balanced finalist confirmation
→ Keep best or restore exact original state
```

Windows default affinity, CPU0 avoidance, community tweaks, or another machine's winner are never assumed to be universally optimal.

## Dependencies: reuse before custom code

The project intentionally uses maintained platform/packages where they reduce risk without hiding critical semantics:

- `Microsoft.Data.Sqlite` rather than a custom database layer;
- `Microsoft.Diagnostics.Tracing.TraceEvent` rather than a custom ETW decoder stack;
- Windows App SDK / WinUI 3 rather than a second desktop UI framework;
- `Microsoft.Extensions.Hosting.WindowsServices` for Service hosting;
- Serilog packages for structured file logging.

Raw Windows interop remains in `LatencyPilot.Platform.Windows` where it is small, API-specific and part of the behavior being verified. Do not add a second interop/UI/MVVM/DI framework merely to reduce visible P/Invoke lines. For PresentMon, use the installed PresentMon API/SDK contract rather than shipping a mismatched private service DLL.

Before introducing another dependency, compare reuse/adaptation against the current implementation for API coverage, support status, license, deployment/native payload, maintenance cost, observability and rollback implications.

## Testing and evidence policy

The permanent automated suite has a **hard repository-wide maximum of 10 tests** and currently uses nine broad tests. Add a permanent test only for a durable high-blast-radius correctness/safety contract; merge or replace lower-value tests rather than growing a test-per-bug suite.

GitHub Actions intentionally runs only the critical test project:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Hosted CI does **not** prove WinUI/Service runtime behavior or hardware optimization claims. App/Service compile/launch remains owner-local; hardware behavior requires physical Windows 11 evidence.

## Local workflows

One-shot owner-local App + protected Service validation:

```powershell
.\run.ps1
```

Combined protected Service + App hot-reload + structured-log development loop:

```powershell
.\live.ps1
```

App-only CLI watch/build/restore helpers remain available through:

```powershell
.\dev.ps1
```

For full WinUI XAML Hot Reload and Live Visual Tree work, Visual Studio `F5` remains the preferred inner loop.

App logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Service logs:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

See [`docs/DIAGNOSTICS.md`](docs/DIAGNOSTICS.md).

## Current owner-local gate

After the exact current `main` revision has a completed green **Tests** run, use `run.ps1` for compile/install/launch validation only. Confirm the exact source revision in the App, that the Service remains read-only, observation/evidence export still works, and startup recovery inspection reports no unresolved experiment on a clean state.

Then continue the physical gate in this order:

```text
startup/recovery inspection
→ controlled unresolved-journal recovery classification
→ exact-target GPU restart/reboot-required validation
→ forced apply/rollback failure exercise while IPC remains unarmed
→ runtime ISR placement reconciliation
→ only then mutation-specific typed authorization/IPC
→ bounded candidate screening
→ PresentMon + ETW target/guardrail integration
→ balanced finalist confirmation
→ Keep best or restore exact original state
```

The exact checklist and blockers live in [`PROJECT_STATUS.md`](PROJECT_STATUS.md).

## Safety model

A supported system-changing feature is incomplete until it follows:

```text
Detect applicability
→ Snapshot exact original state
→ Validate candidate
→ Journal pending experiment
→ Apply one narrow change
→ Verify actual state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
→ Close journal
```

Unknown/diverged state is a recovery condition, not success. A failed or incomplete rollback remains unresolved. Internal source implementation never by itself authorizes user-reachable mutation.

## Release discipline

Release identities use exactly `MAJOR.MINOR.PATCH`. Published versions are immutable. Current source is **0.0.2**.

Release/package creation remains an explicit owner-local action after the exact candidate has green deterministic tests plus required local App/Service/package and physical validation.

See [`docs/RELEASING.md`](docs/RELEASING.md).

## Contributing / license / security

Read [`AGENTS.md`](AGENTS.md) before making implementation changes. Also see [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md).

LatencyPilot is source-available under the repository license; commercial use, redistribution and standalone derivative distribution require the permissions described there.
