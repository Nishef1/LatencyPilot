# Physical Windows 11 Validation

This runbook closes the read-only Phase 2 measurement substrate on physical Windows 11 hardware. It does **not** authorize mutation. ADR 0004 permits targeted Phase 3 source implementation to overlap this remaining record, but user-reachable mutation stays unavailable until its separate arming gate also passes.

## Current contract

```text
Protocol:              v6
Evidence:              latencypilot-evidence-v8
Quick snapshot:        1 × 5 s, diagnostic only
Decision baseline:     baseline-quality-v2
                       5 × 20 s authoritative windows
                       5 s LatencyPilot/service settle before window 1
                       750 ms inter-window settle
p99.9 display floor:   10,000 samples per distribution
Permanent tests:       9 / 10
```

## 1. Preconditions and privilege boundary

Use one exact clean `main` revision on physical Windows 11 x64. The same revision must have a completed green hosted **Tests** run. Hosted CI is test-only; App/Service compile and runtime evidence must come from the owner-local Windows machine.

From a normal terminal:

```powershell
.\run.ps1
```

Verify the Service separately from elevated PowerShell:

```powershell
Get-Service LatencyPilot.Observation
sc.exe qc LatencyPilot.Observation
```

Expected privileged payload:

```text
%ProgramFiles%\LatencyPilot\Service\LatencyPilot.Service.exe
```

The App remains non-elevated. UAC is only for protected Service install/update/removal. The protocol status response must prove the peer is the installed Windows Service with expected kernel-capture privilege and `MutationAvailable=false`. The header must show the exact clean source revision, not `dirty` or revision-unavailable provenance.

Record source revision, Tests run, local build result, Windows build, CPU/topology, GPU/driver, primary NIC/driver, primary xHCI/driver, power context, workload/scene and other monitoring/overlay tools.

## 2. UX and scenario sanity

Exercise:

```text
Ctrl+R  refresh Service
Ctrl+O  quick diagnostic snapshot
Ctrl+B  repeated decision baseline
Ctrl+E  export latest completed evidence
```

`Ctrl+E` must be unavailable without completed evidence and while measurement is active. Every successful protocol-v6 capture must carry a unique `RequestId` correlating App logs, Service logs and evidence-v8.

Scenario semantics:

- **Controlled idle:** close unnecessary apps and avoid unrelated user activity. Normal Windows background activity remains part of the idle noise floor.
- **Real-world workload:** keep the game/apps that reproduce the issue open and place the workload at a warmed/repeatable scene or operation before a decision baseline.
- **Before / after:** keep workload, foreground apps, power state and background conditions as consistent as practical on both sides.

Changing scenario after a completed run must invalidate stale visible/export evidence rather than relabeling it.

## 3. Quick diagnostic snapshot

A quick snapshot is one five-second DPC/ISR capture for integrity, attribution, CPU concentration and hypothesis generation. It is **not** a health verdict, stability proof or optimization decision.

Record requested/actual duration, DPC/ISR count/p99/p99.9-if-available/max, reference/bucket counts, top CPUs/modules, attribution coverage, ETW loss, invalid-event/image counts, event-limit state and runtime CPU/power context.

Interpretation rules:

- DPC `>100 µs` and ISR `>25 µs` are Microsoft driver-duration guidance references, not LatencyPilot pass/fail thresholds.
- `>1 ms` and `>3 ms` are LatencyPilot local diagnostic buckets, not official Windows severity categories.
- CPU0 concentration is a hypothesis, not an automatic fault.
- No exceedance in five seconds does not prove stable latency.
- p99.9 is exposed only with at least **10,000 samples** in that distribution.
- unavailable/non-zero ETW loss, invalid latency/image evidence, or an event-limit hit prevents decision-grade use.

Export and verify:

```powershell
Get-FileHash .\LatencyPilot-observation-*.json -Algorithm SHA256
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-observation-*.json `
  -ExpectedCommit <exact-clean-source-revision> `
  -ExpectedSha256 <64-hex-digest> `
  -RequireCleanCapture
```

Expected envelope:

```text
schema:   latencypilot-evidence-v8
purpose:  quick-diagnostic-snapshot
protocol: 6
```

Passing clean-capture verification still leaves the artifact diagnostic-only.

## 4. Repeated decision baseline

`baseline-quality-v2` uses:

```text
workload already warmed/repeatable when applicable
↓
5 s LatencyPilot/service settle
↓
20 s window 1
750 ms settle
20 s window 2
750 ms settle
20 s window 3
750 ms settle
20 s window 4
750 ms settle
20 s window 5
```

The initial five seconds are **not workload warm-up**. Heavy module/CPU/tail redraw and evidence file I/O stay outside authoritative windows; lightweight progress text is acceptable.

Every window must satisfy:

```text
requested duration >= 20,000 ms
actual duration >= 95% of request
capture integrity clean
DPC count >= 1,000
ISR count >= 1,000
finite positive DPC p99
finite positive ISR p99
```

Both DPC p99 and ISR p99 must satisfy:

```text
P10-P90 relative spread <= 30%
relative drift between early and late windows <= 20%
no >50% extreme-window deviation
```

Do not delete inconvenient windows. `Valid` means repeatable enough for the current comparison method; it does not mean globally healthy.

Run and export two separate baselines on the final candidate:

1. **Real-world workload** — the same warmed/repeatable workload through all five windows.
2. **Controlled idle** — the same controlled-idle condition through all five windows.

Verify each artifact:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-baseline-*.json `
  -ExpectedCommit <exact-clean-source-revision> `
  -ExpectedSha256 <64-hex-digest> `
  -RequireCleanCapture `
  -RequireValidBaseline
```

Expected envelope:

```text
schema:                latencypilot-evidence-v8
purpose:               repeated-decision-baseline
baselineMethodVersion: baseline-quality-v2
protocol:              6
captures/windows:      exactly 5 aligned entries
```

A partial, short, lossy, undersampled, noisy or drifted baseline remains diagnostic evidence but cannot pass the closure gate.

## 5. Plausibility and device evidence

Where practical compare a representative workload with WPA/PerfView or LatencyMon for broad plausibility of DPC/ISR activity, dominant modules and processor concentration. Exact counts need not match because windows, aggregation and observer overhead differ. Attribution to an image that cannot contain the routine address is a blocker.

Inspect representative GPU/display, NIC and actual `USBXHCI` entries while preserving:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

A read failure is not “no configuration”. Stored MSI/affinity policy is not proof of assigned delivery state.

## 6. Failure, authorization and cleanup

Exercise at least:

1. close the App during a quick snapshot, relaunch and verify a new request starts promptly;
2. stop/restart the Service around capture and reconnect the normal-user App;
3. run another capture after recovery;
4. close the App during a repeated-baseline window and confirm partial evidence cannot become `Valid`;
5. verify no stale `LatencyPilot-Kernel-*` ETW session remains after interruption;
6. where practical, malformed/incompatible protocol input fails bounded/closed;
7. where practical, a client outside the active console session is rejected without weakening authorization.

Unproven cleanup or authorization keeps Phase 2 open.

## 7. Accessibility and zero-mutation checks

Check Light, Dark, Windows High Contrast, narrow/wide widths, enlarged text scaling, keyboard-only focus order and screen-reader/UI Automation for Service state, scenario, measurement purpose, exact values, runtime context and baseline verdict. Color must not be the only state cue, and a quick snapshot must not be announced as a health verdict.

Phase 2 validation must not alter interrupt affinity, MSI settings, CPU Sets, power settings, network configuration, device policy, timer settings or unrelated services. Internal Phase 3 source may exist in the same revision, but it must remain unreachable/unarmed during this runbook. Only LatencyPilot installation/Service files and documented diagnostics/evidence artifacts may persist.

For an installer/portable release candidate, also verify documented Service removal:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

## Phase 2 closure record

Close Phase 2 only when the **same final clean source candidate** has:

- exact revision + green hosted Tests run;
- owner-local App/Service compile/run;
- protected Service path + normal-user App boundary;
- protocol-v6 connection/capture;
- clean evidence-v8 quick snapshot;
- valid Real-world evidence-v8/baseline-v2 decision baseline;
- valid Controlled-idle evidence-v8/baseline-v2 decision baseline;
- representative GPU/NIC/xHCI evidence;
- attribution plausibility;
- failure/disconnect/stale-ETW cleanup;
- active-console authorization sanity;
- accessibility/responsive sanity;
- zero-mutation confirmation;
- JSON/SHA-256/source-revision reconciliation;
- unresolved blockers explicitly listed.

Historical short captures under older protocol/schema revisions remain diagnostic history only. They do not satisfy the v2 decision-baseline gate.

Phase 2 closure and Phase 3 mutation arming are separate gates. Phase 3 source may continue under ADR 0004, but no user-reachable mutation authority is introduced until the mutation arming gate in `PROJECT_STATUS.md` has its required physical evidence.
