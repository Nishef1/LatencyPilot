# LatencyPilot

**Evidence-driven latency optimization for Windows 11.**

LatencyPilot is built around one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, one-click FPS booster, or a list of settings assumed to be universally better. The product measures the actual machine, preserves the evidence behind a recommendation, and is designed so every supported mutation can later be verified and reverted.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Phase 2 is read-only. System mutation is deliberately disabled until the observation/baseline substrate is physically validated.

## Why LatencyPilot

Windows exposes interrupt, CPU-topology, ETW, USB, networking and scheduling controls, but hardware-specific advice is easy to overgeneralize. LatencyPilot is intended to answer questions such as:

- Which drivers contribute most to DPC/ISR work?
- Is one CPU handling disproportionate interrupt work?
- Is that concentration repeatable under the workload that matters?
- Does an interrupt-affinity candidate actually improve tail/frame/input behavior?
- Does a local win cause a regression in another subsystem?
- Is an apparent gain larger than normal run-to-run variation?
- Can the original state be restored exactly?

## Product contract

1. **No tweak without evidence.**
2. **One variable at a time before combination testing.**
3. **Quick diagnosis is not benchmark proof.**
4. **Tail latency matters, but extreme percentiles require enough samples.**
5. **Measure collateral effects.** A local win may still be a trade-off.
6. **Know the noise floor.** A small delta inside normal variability is not an improvement.
7. **Rollback first.** Snapshot, journal, verification and recovery are part of the feature.
8. **No universal magic settings.** Hardware, firmware, drivers, workloads and Windows builds differ.
9. **Raw evidence remains visible.** A composite score never replaces the underlying vector of metrics.
10. **Microsoft/API guidance defines semantics; the local experiment decides whether a candidate helps.**

## Current state

- Version — **0.0.2 pre-alpha**
- Phase 0 — governance/architecture: **closed**
- Phase 1 — buildable foundation/comparison core: **closed**
- Phase 2 — trustworthy read-only observation + decision baseline: **in progress**
- Mutation capability — **none by design**
- Permanent tests — **8 / hard maximum 10**
- Hosted CI — **test-only**
- Observation protocol — **v6**
- Evidence schema — **`latencypilot-evidence-v8`**
- Repeated baseline method — **`baseline-quality-v2`**

`PROJECT_STATUS.md` is the live execution ledger and contains the exact current closure state.

## Two measurement modes, two different jobs

### Quick diagnostic snapshot

```text
1 × 5 seconds
```

Used for:

- ETW integrity;
- attribution;
- CPU concentration;
- obvious tail buckets;
- hypothesis generation.

A quick snapshot is **not** a health verdict, stability proof, or optimization recommendation.

### Repeated decision baseline

```text
workload already warmed/repeatable when applicable
5 s LatencyPilot/service settle
5 windows × 20 s
750 ms inter-window settle
= 100 s authoritative measurement
```

`baseline-quality-v2` requires exactly five aligned windows. Each window must request at least 20 seconds, complete at least 95% of the request, remain capture-integrity clean, and contain at least 1,000 DPC and 1,000 ISR events for the current p99 stability screen.

The baseline is `Valid` only when both DPC p99 and ISR p99 also stay within the versioned noise/drift contract:

- P10–P90 relative spread <=30%;
- early/late relative drift <=20%;
- no >50% extreme-window deviation;
- no silent window deletion.

`Valid` means repeatable enough for the comparison method. It does not mean “the PC is healthy”.

## p99.9 policy

Observation protocol v6 withholds p99.9 until a distribution contains at least **10,000 samples**.

The older 1,000-sample floor was deliberately rejected during methodology review because a nominal p99.9 based on roughly one expected top-0.1% sample was too fragile to emphasize as decision evidence.

p50/p95/p99/max remain available according to their existing contracts.

## What Microsoft guidance means here

LatencyPilot may show:

- DPC `>100 µs` — Microsoft driver-duration guidance reference;
- ISR `>25 µs` — Microsoft driver-duration guidance reference;
- DPC/ISR `>1 ms` — LatencyPilot local diagnostic bucket;
- DPC/ISR `>3 ms` — LatencyPilot local diagnostic bucket.

These values are context, not a system-health score. A single exceedance does not prove user-visible impact, and seeing none in a five-second snapshot does not prove the system is consistently clean.

Likewise, CPU0 concentration is evidence worth investigating, not a universal rule that CPU0 is bad. Windows/default affinity remains a control candidate until local A/B evidence proves another candidate is better for the selected workload.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md).

## Phase 2 architecture

Current projects:

```text
src/
  LatencyPilot.Core/
  LatencyPilot.Benchmarking/
  LatencyPilot.Protocol/
  LatencyPilot.Platform.Windows/
  LatencyPilot.Service/
  LatencyPilot.App/

tests/
  LatencyPilot.CriticalTests/
```

Current stack:

- **Language:** C# 14
- **Runtime:** .NET 10 LTS
- **Desktop UI:** WinUI 3
- **Windows App SDK:** 2.4 Stable
- **Target:** Windows 11 x64
- **Distribution:** unpackaged/self-contained
- **Privileged boundary:** narrow Windows Service
- **IPC:** typed/versioned local Named Pipes, protocol v6
- **Tracing:** ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`
- **Windows integration:** SetupAPI, Configuration Manager, processor topology and documented device/resource APIs
- **Graphics telemetry:** PresentMon when Phase 3 GPU experiments need frame/CPU/GPU guardrails
- **Persistence:** SQLite begins with the real Phase 3 journal/recovery schema; Phase 2 does not keep an empty persistence project

The desktop App remains non-elevated. Privileged observation crosses the narrow Service boundary. Mutation-specific commands do not exist in Phase 2.

## Read-only evidence implemented

Phase 2 currently includes:

- processor-group-aware CPU topology;
- stable present PnP identities;
- driver provider/version/INF metadata;
- stored interrupt-configuration evidence;
- allocated ConfigMgr IRQ/resource evidence;
- representative GPU/display, NIC and actual `USBXHCI` device inspection;
- kernel DPC/ISR ETW collection;
- per-processor aggregation;
- image lifetime/rundown-aware module attribution;
- explicit unresolved routine evidence rather than guessed driver names;
- p50/p95/p99/max plus sample-gated p99.9;
- capture-integrity provenance;
- runtime CPU/power provenance;
- JSON evidence with source revision, RequestIds and SHA-256 after save.

LatencyPilot deliberately keeps these evidence layers separate:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

For example, a stored `MSISupported=1` value is not presented as proof that MSI/MSI-X is actively delivering interrupts at runtime.

## Evidence v8

Evidence schema v8 explicitly labels its purpose:

```text
quick-diagnostic-snapshot
repeated-decision-baseline
```

This prevents a five-second diagnostic artifact from being silently reused as a closure-ready baseline.

Use the verifier on Windows:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-observation-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture
```

For a closure-ready baseline:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-baseline-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture `
  -RequireValidBaseline
```

See [`docs/PHYSICAL_VALIDATION.md`](docs/PHYSICAL_VALIDATION.md).

## Future GPU experiments

Phase 3 will not assume that Windows default affinity, CPU0 avoidance, Reddit advice, or another tuning project's winner is universally correct.

The intended GPU path is:

1. preserve default/current state as a control;
2. generate topology-aware physical-core candidates;
3. perform bounded screening;
4. confirm a small finalist set with longer balanced/interleaved A/B sequences such as ABBA/BAAB;
5. combine DPC/ISR evidence with applicable PresentMon frame-time, CPU/GPU busy/wait, GPU/display latency and dropped-frame metrics;
6. evaluate USB/network/audio/stability guardrails where the profile requires them;
7. Keep only a measured improvement; otherwise Revert.

Every mutation must first have exact original-state snapshotting, journaling, applied-state verification and recovery.

## Testing policy

The permanent automated suite has a **hard repository-wide maximum of 10 tests**. The current suite uses eight broader test methods.

Scenario matrices are consolidated inside durable tests where practical. Temporary implementation/debug tests may be created, run and deleted before finalization.

Hardware validation is separate from the permanent-test cap.

## CI and local development

GitHub Actions intentionally runs only:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Hosted CI does **not** establish WinUI App/Service compile or physical ETW correctness.

For owner-local source validation:

```powershell
.\run.ps1
```

For the combined App + protected Service + structured-log development loop:

```powershell
.\live.ps1
```

For daily WinUI work, Visual Studio `F5` with the App Hot Reload profile remains the preferred inner loop.

## Diagnostics

App logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Service logs:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

Logging is bounded and avoids raw per-event DPC/ISR writes in the measured hot path. Capture `RequestId` connects App request logs, Service capture logs and exported evidence.

See [`docs/DIAGNOSTICS.md`](docs/DIAGNOSTICS.md).

## Release discipline

Release identities use exactly `MAJOR.MINOR.PATCH`.

Published versions are immutable. Historical `v0.0.0` exists and `v0.0.1` remains reserved from a prior publication. Current source is **0.0.2** and must not reuse a historical published identity.

Release/package creation remains an explicit owner-local action after the exact candidate has green deterministic tests plus local App/Service/package validation.

See [`docs/RELEASING.md`](docs/RELEASING.md).

## Current next step

The current source methodology must first reach one final clean revision with green eight-test CI. Then on physical Windows 11:

1. `git pull` exact final `main`;
2. run `\.\run.ps1`;
3. confirm header source SHA + Service connected/read-only;
4. take one v8 five-second Real-world quick diagnostic snapshot;
5. run a warmed/repeatable Real-world five × 20-second decision baseline;
6. run a Controlled-idle five × 20-second decision baseline;
7. verify both with `-RequireCleanCapture -RequireValidBaseline`;
8. close GPU/NIC/xHCI, recovery/session and accessibility checks;
9. only then close Phase 2 and begin reversible mutation work.

See [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for the exact execution ledger.

## Safety model for future mutations

A system-changing feature is incomplete until it supports:

```text
Detect applicability
→ Snapshot original state
→ Validate candidate
→ Journal pending experiment
→ Apply
→ Verify actual state
→ Measure
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
→ Close journal
```

None of those mutation capabilities are implied by the current read-only Service.

## Contributing / license / security

See [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md).

LatencyPilot is source-available under the repository license; commercial use, redistribution and standalone derivative distribution require the permissions described there.
