# Physical Windows 11 Validation

This runbook closes the read-only Phase 2 measurement substrate on physical Windows 11 hardware. It does **not** authorize system mutation.

Current authoritative contracts:

```text
Product:               0.0.x pre-alpha
Observation protocol:  v6
Evidence schema:       latencypilot-evidence-v8
Quick snapshot:        1 × 5 s, diagnostic only
Decision baseline:     baseline-quality-v2
                       5 × 20 s authoritative windows
                       5 s LatencyPilot/service settle before window 1
                       750 ms inter-window settle
Permanent tests:       8 / 10 maximum slots currently used
```

## 1. Preconditions

Use a physical Windows 11 x64 machine and record the exact clean `main` revision before testing.

Required conditions:

- App runs as a normal/non-elevated user;
- elevation is used only for protected Service installation/update/removal;
- `LatencyPilot.Observation` runs under the Service Control Manager from the protected Program Files Service path;
- the exact source revision has a completed green hosted **Tests** run;
- App/Service compile and runtime evidence comes from the owner-local Windows machine because hosted CI intentionally remains test-only;
- mutation remains unavailable;
- the active local console session is used for the current Phase 2 authorization model.

Record:

```text
LatencyPilot version:
Source revision:
Hosted Tests run:
Local build command/result:
Windows edition/build:
CPU/topology:
GPU + driver:
Primary NIC + driver:
Primary xHCI controller + driver:
Power source / plan / configured mode:
Battery Saver state:
Foreground workload/version/scene where applicable:
Other overlays/monitoring tools:
```

## 2. Build, install Service, launch App

From a normal terminal at the exact clean source revision:

```powershell
.\run.ps1
```

The script builds locally, requests UAC only for protected Service installation/update, and launches the App non-elevated.

Verify the Service from elevated PowerShell:

```powershell
Get-Service LatencyPilot.Observation
sc.exe qc LatencyPilot.Observation
```

Expected Service payload:

```text
%ProgramFiles%\LatencyPilot\Service\LatencyPilot.Service.exe
```

The App must prove through the status contract that the host is the installed Windows Service with the expected kernel-capture privilege context and that mutation is disabled. A process merely answering the pipe is insufficient.

Verify the visible header carries the expected clean source revision rather than `dirty` or revision-unavailable provenance.

## 3. Keyboard and state sanity

Exercise once:

```text
Ctrl+R  refresh Service
Ctrl+O  quick diagnostic snapshot
Ctrl+B  repeated decision baseline
Ctrl+E  export latest completed evidence
```

`Ctrl+E` must remain unavailable when no completed evidence exists and while a measurement sequence is active.

Protocol-v6 capture responses carry unique `RequestId` values for App/Service/evidence correlation.

## 4. Scenario semantics

Select the scenario that actually describes the measurement.

### Controlled idle

Close unnecessary applications and avoid unrelated user work. Normal background Windows activity is part of the idle noise floor; do not fake an impossible zero-activity machine.

### Real-world workload

Keep the applications or game that reproduce the issue open. They are part of the evidence. Before starting a **decision baseline**, put the workload at a warmed and repeatable point, such as the same game scene/action loop or application operation.

Do not close relevant apps merely to make the numbers look better.

### Before / after comparison

Use the same warmed workload, foreground applications, power state and background conditions on both sides as closely as practical.

The scenario is evidence provenance. Changing it after a completed measurement must invalidate stale visible/export evidence rather than relabel an old run.

## 5. Quick diagnostic snapshot

A quick snapshot is exactly one five-second DPC/ISR capture. It is useful for proving the observation path and generating hypotheses, not for deciding whether the machine is globally healthy or whether a tweak should be kept.

Take at least one **Real-world workload** quick snapshot on the final candidate. A Controlled-idle quick snapshot is useful too, but decision-grade Phase 2 closure depends on the repeated baselines below.

Record:

```text
Scenario:
Requested / actual duration:
DPC count / p99 / p99.9 when available / max:
ISR count / p99 / p99.9 when available / max:
DPC >100 us reference count/rate:
ISR >25 us reference count/rate:
DPC/ISR >1 ms local-bucket count:
DPC/ISR >3 ms local-bucket count:
Top CPUs / concentration:
Resolved / unresolved attribution counts:
Top modules:
ETW events lost:
Invalid latency events:
Invalid image events:
Event limit reached:
Runtime CPU busy %:
Power source / plan / configured mode / Battery Saver:
Power context changed during capture? :
```

Interpretation rules:

- `DPC >100 µs` and `ISR >25 µs` are Microsoft driver-duration guidance references, not LatencyPilot pass/fail thresholds;
- `>1 ms` and `>3 ms` are LatencyPilot local diagnostic buckets, not official Windows severity categories;
- CPU0 concentration is an observation/hypothesis, not an automatic fault;
- no reference exceedance in one five-second snapshot is not proof of a consistently clean machine;
- p99.9 is exposed only when that distribution has at least **10,000 samples** under protocol v6.

Microsoft's ETW guidance explicitly requires monitoring lost events because real-time consumers can lose events when they do not consume fast enough. Therefore unavailable/non-zero ETW loss, invalid latency/image evidence, or an event-limit hit makes the capture unsuitable for decision-grade use. See `docs/BENCHMARK_METHODOLOGY.md` for the maintained methodology rationale.

### Export and verify snapshot

Export JSON and record its SHA-256:

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

The verifier must explicitly identify the artifact as a quick diagnostic snapshot; passing clean-capture verification does not turn it into a benchmark verdict.

Evidence should contain source/product/protocol provenance, scenario, bounded environment/topology context, best-effort runtime CPU/power context, unique RequestId, processor/module/unresolved aggregates and capture-integrity metadata.

User-defined power-plan friendly names may remain local UI text; persist stable identifiers rather than unnecessary personal labels.

## 6. Repeated decision baseline — `baseline-quality-v2`

The decision-grade sequence is:

```text
workload already warmed/repeatable by the user
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

The initial five seconds are **not workload warm-up**. Do not start the sequence while a game is still loading or shader-compiling unless startup behavior itself is the intended workload.

Detailed module/CPU/tail UI should not be fully redrawn between authoritative windows; lightweight progress text is acceptable. The full cards are rendered after the sequence to reduce observer activity.

### 6.1 Real-world decision baseline

Select **Real-world workload**. Put the workload in the repeatable state confirmed by the two preparation checks and start **Build baseline**.

For every window record:

```text
Window number:
Requested / actual duration:
Capture integrity:
DPC event count / p99:
ISR event count / p99:
Runtime CPU busy %:
Power context:
```

Then record:

```text
DPC p99 median / P10-P90 relative spread / early-late drift:
ISR p99 median / P10-P90 relative spread / early-late drift:
Extreme windows:
Overall status:
All reasons:
```

A v2 window is eligible only when:

```text
requested duration >= 20,000 ms
actual duration >= 95% of request
capture integrity clean
DPC count >= 1,000
ISR count >= 1,000
finite positive DPC/ISR p99
```

The complete baseline is `Valid` only when all five windows are eligible and both DPC and ISR p99 stability screens pass:

```text
P10-P90 relative spread <= 30%
early/late typo prohibited; authoritative rule is early/late relative drift <= 20%
no >50% extreme-window deviation
```

Do not delete an inconvenient window to make the result pass. `Valid` means repeatable enough for the current comparison method; it does not mean the machine is globally healthy.

### 6.2 Controlled-idle decision baseline

Return the machine to the intended Controlled-idle state, confirm both preparation checks again and run the same five × 20-second sequence.

This establishes a second context: repeatable idle behavior rather than real-world workload behavior. A difference between idle and real-world concentration is useful evidence; neither context should be substituted for the other.

### 6.3 Export and verify each baseline

Export each scenario separately. Do not overwrite artifacts.

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

The verifier independently enforces five clean windows, duration adequacy, >=1,000 DPC and ISR events per window and `Status=Valid` / `IsValidForComparison=true`.

A partial, short, lossy, undersampled, noisy or drifted baseline remains useful diagnostic evidence but must not pass the closure gate.

## 7. Plausibility against an independent observer

Where practical, compare a representative workload with WPA/PerfView or LatencyMon for broad plausibility of:

- DPC/ISR activity;
- dominant module identities;
- broad processor concentration;
- absence of impossible attribution.

Exact counts/percentiles need not match because capture windows, aggregation and observer overhead differ. If LatencyPilot attributes an address to an image that cannot authoritatively contain it, treat that as a blocker.

## 8. Representative device evidence

Inspect representative GPU/display, NIC and actual `USBXHCI` entries.

Keep these evidence levels separate:

```text
stored interrupt configuration
allocated IRQ/resource assignment
runtime DPC/ISR behavior
```

A read failure is not equivalent to “no configuration”. Stored MSI/affinity policy is not proof of the resource assignment Windows actually allocated.

## 9. Failure, disconnect and cleanup

Exercise at least:

1. close the App during a quick snapshot;
2. relaunch and verify a new status/capture can start promptly;
3. stop the Service during or immediately after capture;
4. restart the Service and reconnect the normal-user App;
5. run another capture after recovery;
6. close the App during a repeated-baseline window and verify a partial sequence cannot become `Valid`;
7. verify no stale `LatencyPilot-Kernel-*` ETW session remains after interrupted captures;
8. where practical, verify incompatible/malformed local protocol input fails bounded/closed;
9. where a second interactive session exists, verify a client outside the active console session is rejected without weakening authorization.

If cleanup or authorization cannot be proven, Phase 2 remains open.

## 10. Accessibility and responsive sanity

Check on physical WinUI:

- Light, Dark and Windows High Contrast;
- narrow and wide window widths;
- representative enlarged Windows text scaling;
- keyboard-only access and logical focus order;
- screen-reader/UI Automation reading of Service state, scenario, measurement purpose, exact values, runtime context and baseline verdict.

Color must never be the only state cue.

A quick snapshot must not be announced or visually framed as a health verdict. Exact data and diagnostic purpose should remain clear to assistive technology.

## 11. Zero-mutation boundary

Phase 2 observation must not alter:

- interrupt affinity;
- MSI settings;
- CPU Sets;
- power settings;
- network configuration;
- device policy;
- timer settings;
- unrelated services.

Expected persistent changes are limited to LatencyPilot installation/Service files and documented diagnostic artifacts.

Any unrelated persistent configuration change is a blocker.

## 12. Removal check when applicable

For installer/portable validation, verify Service removal semantics:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

Do this for a release candidate; it is not necessary to repeatedly uninstall during ordinary source-development captures.

## Phase 2 closure record

Do not close Phase 2 until all required outcomes are evidenced for the **same final clean source candidate**:

- exact revision + green hosted Tests run;
- owner-local App and Service compile/run;
- protected Service path and normal-user App boundary;
- protocol-v6 connection/capture;
- at least one evidence-v8 quick snapshot proving integrity/provenance/attribution behavior;
- one **Real-world** evidence-v8 / baseline-v2 repeated decision baseline;
- one **Controlled-idle** evidence-v8 / baseline-v2 repeated decision baseline;
- representative GPU/NIC/xHCI evidence;
- attribution plausibility;
- failure/disconnect/stale-ETW cleanup;
- active-console authorization sanity;
- accessibility/responsive sanity;
- zero-mutation confirmation;
- JSON/SHA-256/source-revision reconciliation;
- unresolved blockers explicitly listed.

Historical five-second observations collected under earlier protocol/schema revisions remain useful **diagnostic evidence** for ETW integrity, attribution and hypothesis formation. They do not satisfy the v2 decision-baseline closure requirement.

After Phase 2 closes, Phase 3 may implement reversible mutations. The first candidate must still be measured against a control; no Microsoft default, community tweak or prior-project assumption is automatically accepted as optimal.
