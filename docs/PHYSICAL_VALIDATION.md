# Physical Windows 11 Validation

This runbook closes the read-only Phase 2 measurement substrate on physical Windows 11 hardware. It does **not** authorize system mutation.

Current contract:

```text
Observation protocol:  v6
Evidence schema:       latencypilot-evidence-v8
Quick snapshot:        1 × 5 s, diagnostic only
Decision baseline:     baseline-quality-v2
                       5 × 20 s authoritative windows
                       5 s LatencyPilot/service settle before window 1
                       750 ms inter-window settle
p99.9 display floor:   10,000 samples per distribution
Permanent tests:       8 / 10
```

## 1. Preconditions

Use a physical Windows 11 x64 machine and the exact clean `main` revision being evaluated.

Required:

- App runs non-elevated;
- UAC is used only for protected Service install/update/removal;
- `LatencyPilot.Observation` runs through the Service Control Manager from `%ProgramFiles%\LatencyPilot\Service`;
- the exact source revision has a completed green hosted **Tests** run;
- owner-local Windows provides App/Service compile/runtime evidence because hosted CI is test-only;
- mutation remains unavailable;
- current authorization uses the active local console session.

Record before testing:

```text
LatencyPilot version:
Source revision:
Hosted Tests run:
Local build result:
Windows edition/build:
CPU/topology:
GPU + driver:
Primary NIC + driver:
Primary xHCI controller + driver:
Power source / active plan / configured mode:
Battery Saver:
Foreground workload/version/scene:
Other overlays/monitoring tools:
```

## 2. Build, Service and launch boundary

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

The App must prove through protocol status that the peer is the installed Windows Service with expected kernel-capture privilege and `MutationAvailable=false`. A random process answering the pipe is insufficient.

The App header must show the exact clean source revision, not `dirty` or revision-unavailable provenance.

## 3. Basic UX/keyboard sanity

Exercise:

```text
Ctrl+R  refresh Service
Ctrl+O  quick diagnostic snapshot
Ctrl+B  repeated decision baseline
Ctrl+E  export latest completed evidence
```

`Ctrl+E` must remain disabled when no completed evidence exists and while measurement is active.

Every successful protocol-v6 capture must carry a unique `RequestId` that correlates App logs, Service logs and evidence-v8.

## 4. Scenario semantics

### Controlled idle

Close unnecessary applications and avoid unrelated user work. Normal background Windows activity remains part of the idle noise floor.

### Real-world workload

Keep the game/applications that reproduce the issue open. Before starting a **decision baseline**, place the workload at the same warmed/repeatable point you intend to compare.

Do not close relevant apps merely to improve the numbers.

### Before / after

Keep workload, foreground applications, power state and background conditions as consistent as practical on both sides.

Changing the scenario after a completed run must invalidate stale visible/export evidence instead of relabeling the old capture.

## 5. Quick diagnostic snapshot

A quick snapshot is one five-second DPC/ISR capture. Its job is integrity, attribution, concentration and hypothesis generation. It is **not** a health verdict or optimization decision.

On the final candidate, take at least one Real-world quick snapshot and record:

```text
Scenario:
Requested / actual duration:
DPC count / p99 / p99.9 if available / max:
ISR count / p99 / p99.9 if available / max:
DPC >100 us reference count/rate:
ISR >25 us reference count/rate:
DPC/ISR >1 ms local-bucket count:
DPC/ISR >3 ms local-bucket count:
Top CPUs / concentration:
Resolved / unresolved attribution:
Top modules:
ETW events lost:
Invalid latency/image events:
Event limit reached:
Runtime CPU busy %:
Power context and whether it changed:
```

Interpretation:

- DPC `>100 µs` and ISR `>25 µs` are Microsoft driver-duration guidance references, not LatencyPilot pass/fail thresholds;
- `>1 ms` and `>3 ms` are LatencyPilot diagnostic buckets, not official Windows severity categories;
- CPU0 concentration is a hypothesis, not an automatic fault;
- no exceedance in five seconds does not prove stable latency;
- p99.9 is shown only with at least **10,000 samples** in that distribution.

ETW loss/invalid/event-limit state is decision-critical. Microsoft documents that real-time ETW events can be lost when the consumer does not consume quickly enough, so loss cannot be silently treated as zero or ignored.

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

A clean snapshot is still diagnostic-only.

## 6. Repeated decision baseline — `baseline-quality-v2`

Decision sequence:

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

The initial five seconds are **not** workload warm-up. Do not start while a game is still loading/shader-compiling unless startup itself is the intended workload.

Heavy module/CPU/tail UI redraw and evidence file I/O must stay outside authoritative windows. Lightweight progress text between windows is acceptable.

### Window eligibility

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

### Five-window stability gate

Both DPC p99 and ISR p99 must satisfy:

```text
P10-P90 relative spread <= 30%
early/late wording is not used; authoritative early/late relative drift <= 20%
no >50% extreme-window deviation
```

Do not delete inconvenient windows. `Valid` means repeatable enough for the comparison method, not globally healthy.

### Required physical baselines

Run and export separately:

1. **Real-world workload** — same warmed/repeatable workload through all five windows.
2. **Controlled idle** — same controlled-idle condition through all five windows.

For each window record duration, integrity, DPC/ISR count+p99 and runtime power/CPU context. Record aggregate median/noise/drift/extreme-window reasons and overall status.

Verify each baseline:

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

A partial, short, lossy, undersampled, noisy or drifted baseline may be useful diagnostic evidence but cannot pass the closure gate.

## 7. Independent plausibility

Where practical compare a representative workload with WPA/PerfView or LatencyMon for broad plausibility of:

- DPC/ISR activity;
- dominant module identities;
- processor concentration;
- absence of impossible attribution.

Exact counts/percentiles need not match because capture windows, aggregation and observer overhead differ. Attribution to an image that cannot contain the routine address is a blocker.

## 8. Representative device evidence

Inspect representative GPU/display, NIC and actual `USBXHCI` entries and keep these layers separate:

```text
stored interrupt configuration
allocated IRQ/resource assignment
runtime DPC/ISR behavior
```

A read failure is not “no configuration”. Stored MSI/affinity policy is not proof of assigned delivery state.

## 9. Failure, disconnect and cleanup

Exercise at least:

1. close the App during a quick snapshot;
2. relaunch and start a new status/capture promptly;
3. stop/restart the Service around capture and reconnect the normal-user App;
4. run another capture after recovery;
5. close the App during a repeated-baseline window and confirm partial evidence cannot become `Valid`;
6. verify no stale `LatencyPilot-Kernel-*` ETW session remains after interruption;
7. where practical, malformed/incompatible protocol input fails bounded/closed;
8. where practical, a client outside the active console session is rejected without weakening authorization.

Unproven cleanup or authorization keeps Phase 2 open.

## 10. Accessibility/responsive sanity

Check:

- Light, Dark and Windows High Contrast;
- narrow/wide widths;
- representative enlarged Windows text scaling;
- keyboard-only focus order;
- screen-reader/UI Automation for Service state, scenario, measurement purpose, exact values, runtime context and baseline verdict.

Color cannot be the only state cue. A quick snapshot must not be announced as a health verdict.

## 11. Zero-mutation boundary

Phase 2 must not alter interrupt affinity, MSI settings, CPU Sets, power settings, network configuration, device policy, timer settings or unrelated services.

Only LatencyPilot installation/Service files and documented diagnostic/evidence artifacts may persist. Any unrelated persistent configuration change is a blocker.

## 12. Removal check for release candidates

When validating installer/portable lifecycle:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

Repeated uninstall is not required for ordinary source-development captures.

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

After Phase 2 closes, Phase 3 may introduce reversible mutations. No Windows default, community tweak or prior-project assumption is automatically accepted as optimal; control/candidate measurement remains mandatory.
