# LatencyPilot

**Evidence-driven latency experimentation for Windows 11.**

LatencyPilot follows one rule:

> **Measure → Experiment → Verify → Compare → Keep or Revert**

It is not a registry-tweak pack, debloater, generic FPS booster or a list of settings assumed to be universally better. The product measures the actual machine, keeps raw evidence visible, and requires every supported system change to be attributable, verifiable and reversible.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. The repository has advanced through the read-only USB/NIC/profile/release source tranche, but true product 1.0 is **not** complete: Phase 2 owner-local closure and the physical mutation Gate A→B→C→D sequence are still open. Public protocol v6 remains observation-only and user-reachable mutation is unavailable.

## Current state

- Phase 0 — governance/architecture: **closed**
- Phase 1 — deterministic comparison/build foundation: **closed**
- Phase 2 — trustworthy read-only observation: **source complete; physical closure open**
- Phase 3 — GPU experiment/recovery: **internal source implemented; physical arming open**
- Phase 4 — USB/xHCI/input: **read-only/readiness source implemented; mutation/physical proof gated**
- Phase 5 — NIC/RSS: **read-only/readiness source implemented; mutation/physical proof gated**
- Phase 6 — profiles/Pareto/Restore Baseline: **policy/recovery source implemented; armed multi-subsystem flow gated**
- Phase 7 — release/recovery hardening: **source implemented; owner-local package/signing closure open**
- Observation protocol — **v6**, `GetStatus` + `CaptureKernelLatency` only
- Evidence schema — **`latencypilot-evidence-v8`**
- Repeated baseline — **`baseline-quality-v2`**
- Optimizer workload readiness — **`workload-stability-v1`**
- Permanent deterministic tests — **17**; target 10, owner-authorized maximum 20 only for materially safer durable separation/line-limit needs
- Hosted CI — **test-only**

[`PROJECT_STATUS.md`](PROJECT_STATUS.md) is the live execution ledger. [`ROADMAP.md`](ROADMAP.md) defines product and phase exit gates. Source presence never substitutes for physical evidence.

## What LatencyPilot answers

LatencyPilot is intended to answer questions such as:

- Which drivers contribute most to DPC/ISR work?
- Is one CPU handling disproportionate interrupt work?
- Is that behavior repeatable under the workload that matters?
- Is the workload itself stable enough to compare candidates?
- Which exact USB hub/port/controller backs an input path?
- What host-observable Raw Input report interval/jitter is present without pretending it is click-to-photon latency?
- What RSS state does Windows actually report for the network adapter?
- Does a candidate improve the target without regressing graphics/network/audio/input guardrails?
- Is an apparent gain larger than normal noise/drift?
- Can every retained managed change be restored after interruption or failure?

## Product contract

1. **No tweak without evidence.**
2. **Quick diagnosis is not benchmark proof.**
3. **One variable at a time before combination testing.**
4. **Tail latency requires adequate samples.**
5. **Measure collateral effects.** A local win can still be a trade-off.
6. **Know the noise floor and workload stability.** A changing workload is not candidate evidence.
7. **Rollback first.** Snapshot, durable journal, verification and recovery are part of the feature.
8. **No universal magic settings.** Hardware, firmware, drivers, workloads and Windows builds differ.
9. **Raw metrics remain visible.** There is no hidden weighted optimizer score.
10. **Documented platform semantics define what a setting/evidence source means; local measurement decides whether it helps.**

## Architecture

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

Responsibilities stay narrow:

- `Core` — stable domain concepts/invariants;
- `Benchmarking` — percentile, baseline/workload stability, comparison, profiles, Pareto/readiness logic;
- `Protocol` — typed/versioned local IPC only;
- `Platform.Windows` — ETW, SetupAPI/ConfigMgr, USB hub IOCTLs, Raw Input, StandardCimv2 RSS and Windows-specific mechanisms;
- `Persistence` — concrete SQLite mutation journal/recovery state;
- `Service` — privileged observation boundary and internal fail-closed experiment/recovery substrate;
- `App` — non-elevated WinUI 3 evidence/measurement UX;
- `CriticalTests` — focused durable high-blast-radius contracts.

Current stack:

- C# 14 / .NET 10 LTS;
- WinUI 3 / Windows App SDK 2.4 Stable;
- Windows 11 x64;
- unpackaged self-contained App;
- narrow Windows Service;
- typed/versioned Named Pipes;
- ETW via `Microsoft.Diagnostics.Tracing.TraceEvent`;
- SetupAPI + Configuration Manager;
- documented USB hub interfaces/IOCTLs and Raw Input;
- `Root\StandardCimv2` RSS provider through `System.Management`;
- SQLite via `Microsoft.Data.Sqlite`;
- PresentMon API for graphics/frame evidence where applicable;
- Serilog bounded local diagnostics;
- MSTest + Microsoft.Testing.Platform.

NuGet versions are centrally managed by [`Directory.Packages.props`](Directory.Packages.props). The App remains non-elevated. There is no generic privileged shell/process/registry execution surface.

## Measurement products

### Quick diagnostic snapshot

```text
1 × 5 seconds
purpose = quick-diagnostic-snapshot
```

Used for integrity, attribution, CPU concentration and hypothesis generation. It is **not** a health verdict or optimization recommendation.

### Repeated decision baseline

```text
workload already warmed/repeatable when applicable
5 s LatencyPilot/service settle
5 × 20 s authoritative windows
750 ms inter-window settle
purpose = repeated-decision-baseline
method = baseline-quality-v2
```

A window must pass duration, capture-integrity and sample requirements. Across all five windows, noise/drift/extreme-window gates must pass.

### Optimizer workload readiness

`workload-stability-v1` uses the same five-window sequence and rejects candidate planning when DPC/ISR event-rate activity (and CPU-busy evidence where available) changes materially or contains an isolated extreme activity window. A statistically clean latency baseline is therefore not enough by itself; the workload must also be comparable.

Current p99.9 policy withholds p99.9 until an individual distribution contains at least **10,000 samples**.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md).

## Evidence semantics

LatencyPilot keeps these levels separate:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Examples:

- registry interrupt settings are stored configuration;
- Configuration Manager resources are assigned-resource evidence;
- ETW DPC/ISR events are runtime behavior.

A stored `MSISupported=1` value is not presented as proof that MSI/MSI-X is active at runtime.

Evidence-v8 also keeps quick snapshots separate from repeated baselines. Saved evidence carries source/protocol/scenario provenance and SHA-256 verification metadata.

Verify on Windows:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-observation-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture
```

For a decision baseline:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-baseline-*.json `
  -ExpectedCommit <exact-clean-sha> `
  -RequireCleanCapture `
  -RequireValidBaseline
```

## Read-only USB/input and NIC/RSS inspection

The existing App device-evidence inspector now remains one read-only surface for:

- representative GPU/NIC/xHCI driver/interrupt evidence;
- Raw Input → PnP → xHCI route identity;
- exact documented USB hub/port correlation when one unique driver-key match exists;
- StandardCimv2 RSS provider state and conservative provider→PnP correlation;
- optional five-second host-observable Raw Input timing capture with median/p95/p99 interval, observed report rate, jitter, gaps and burst/coalescing evidence.

The timing capture is explicitly host Raw Input dispatch timing. It is **not** physical device latency or click-to-photon measurement.

## Internal GPU experiment source

The internal source path is:

```text
valid baseline + stable workload
→ topology-aware bounded candidates from measured pressure
→ exact original snapshot + durable journal
→ apply / verify / exact-target activation
→ synchronized ETW + raw PresentMon evidence
→ runtime GPU ISR-placement verification
→ exact rollback between screening candidates
→ finalist only
→ fixed ABBA + BAAB confirmation
→ verified Keep or exact RestoreOriginal / RecoveryRequired
```

Screening cannot Keep directly. Missing/incompatible evidence is Inconclusive. A guardrail regression prevents an automatic win. Public protocol v6 exposes none of the mutation operations.

## USB/NIC optimization frontier

Repository source contains authoritative read-only/readiness contracts for xHCI/input and RSS/network. Their system-changing mutation paths are deliberately deferred until the shared mutation substrate passes physical Gate A. That sequencing prevents copying a source-level GPU safety design into other controllers before restart/rollback/recovery have actually been proven on hardware.

## Profiles, Pareto and Restore Baseline

Versioned profiles include Competitive/Gaming, General and Audio-sensitive policies with explicit subsystem opt-out. Candidate relations are represented as Dominates, Dominated, Equivalent, Tradeoff or Inconclusive from named metric outcomes; unlike metrics are never collapsed into an arbitrary weighted score.

Retained managed changes are discoverable newest-first. Global Restore Baseline planning fails closed on unresolved state or unknown mutation kinds and delegates restoration to supported subsystem-specific recovery executors rather than a generic privileged writer.

## Testing and evidence policy

The durable suite currently contains **17 tests**. The default target remains 10; the owner-authorized maximum is 20 only when separate subsystem contracts materially improve failure isolation or are needed to keep each test file `<=1200` lines. Current USB, input, NIC/RSS, profile/Pareto, restore, workload-readiness and GPU runtime-placement contracts use that authorization intentionally.

GitHub Actions runs only:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Hosted CI does **not** prove WinUI/Service launch, hardware behavior, installer/package behavior, signing or accessibility. Those are owner-local requirements.

## Local workflows

One-shot owner-local App + protected Service validation:

```powershell
.\run.ps1
```

Combined Service + App development/log loop:

```powershell
.\live.ps1
```

App-only helpers:

```powershell
.\dev.ps1
```

For full XAML Hot Reload/Live Visual Tree, Visual Studio `F5` remains the preferred UI loop.

Logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

## Physical arming sequence

True product mutation remains gated:

```text
finish Phase 2 read-only physical closure
→ Gate A: internal GPU restart/apply/runtime-placement/rollback/forced-failure proof
→ Gate B: mutation-specific typed/allowlisted IPC
→ Gate C: physical App/client → Service mutation-boundary proof
→ Gate D: user-facing GPU arming
→ supported USB/xHCI mutation implementation + physical proof
→ supported NIC/RSS mutation implementation + physical proof
→ bounded multi-subsystem validation
```

See [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for the exact live checklist.

## Release discipline

Hosted CI remains test-only. Production release creation is owner-local and fail-closed:

```text
clean exact main + exact green Tests
→ Release build/publish
→ WinUI .pri + launch smoke
→ explicit payload + SHA-256 manifest
→ installer + portable package hashes
→ optional/required Authenticode signing + RFC 3161 timestamp
→ GitHub release publication
```

Upgrade and uninstall refuse to replace/remove recovery tools while the journal is unreadable or LatencyPilot still owns an unresolved or retained (`Kept`) change. Restore/recovery must complete first.

Final releases require signing configuration; source hooks alone are not signing evidence. See [`docs/RELEASING.md`](docs/RELEASING.md).

## What “100%” means

Repository/source completion and true 1.0 are intentionally different claims. Source may be complete up to documented safety prerequisites while physical gates remain open. **LatencyPilot is only 100%/1.0 when the remaining physical read-only audit, Gate A→B→C→D, USB/NIC experiment evidence, App accessibility/runtime validation, signed package/upgrade/uninstall/recovery validation and representative supported-hardware audit all pass on exact recorded artifacts.**

## Contributing / license / security

Read [`AGENTS.md`](AGENTS.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md) before making changes.
