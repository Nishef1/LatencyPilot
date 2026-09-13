# Physical Windows 11 Validation

This runbook is for **Stage B** physical validation of the read-only `0.0.x` LatencyPilot observation path and the companion Stage C repeated-baseline evidence. It does not authorize or test system mutation.

## Preconditions

- physical Windows 11 x64 machine;
- exact `main` revision or matching owner-local release candidate identified before testing;
- App runs as a normal/non-elevated user;
- elevation is used only for setup/protected Service installation/update/uninstall;
- `LatencyPilot.Observation` runs from the protected Program Files Service path under the Windows Service Control Manager;
- exact source revision has a completed green hosted **Tests** run for the eight permanent deterministic tests;
- App/Service compile and runtime evidence comes from the owner-local Windows machine, not hosted CI;
- `docs/DIAGNOSTICS.md` is available for App/Service log correlation.

The current Phase 2 privileged path supports the **active local console session**. RDP/multi-session support is not implied; broader authorization requires a deliberate design rather than weakening this rule.

Record before starting:

```text
LatencyPilot version:
Source revision:
Hosted Tests run:
Local build command/result:
Distribution (source/setup/portable):
Distribution SHA-256 when applicable:
App launch-smoke result when applicable:
Windows edition/build:
Interactive session notes:
CPU:
Motherboard/firmware:
GPU + driver:
Primary NIC + driver:
Primary xHCI/USB controller + driver:
Power source:
Active power plan:
Battery Saver state:
Other monitoring/overlay tools running:
```

## 1. Install/update the protected Service

For normal source validation from the repository, run from a non-elevated terminal:

```powershell
.\run.ps1
```

The script locally builds App/Service, requests UAC only for protected Service installation/update, and launches the App non-elevated.

For setup/portable validation, use the matching release tooling. The privileged Service payload must resolve to:

```text
%ProgramFiles%\LatencyPilot\Service\LatencyPilot.Service.exe
```

Verify from elevated PowerShell:

```powershell
Get-Service LatencyPilot.Observation
sc.exe qc LatencyPilot.Observation
```

Expected state: `Running`; the configured binary path must use the protected Program Files Service location.

The App must not enable kernel capture merely because something answered the Named Pipe. The status response must prove that the observation host is the installed Windows Service and that the expected kernel-capture privilege context is present while mutation remains disabled.

## 2. Start the App without elevation

Launch LatencyPilot as a normal user in the active local console session. Confirm:

- visible product version is expected;
- Service reports connected/read-only;
- mutation remains unavailable;
- stored configuration, allocated resource assignment and runtime DPC/ISR evidence are presented as separate evidence levels.

Exercise the keyboard paths once:

```text
Ctrl+R  refresh service
Ctrl+O  capture one observation
Ctrl+B  build repeated baseline
Ctrl+E  export completed aggregate evidence
```

`Ctrl+E` must be unavailable with no completed evidence and while a capture sequence is busy.

If a failure occurs, preserve the visible message plus matching App/Service structured log entries. Protocol v5 capture evidence carries the capture `RequestId` used for log correlation.

## 3. Measurement-scenario semantics

Before each observation or repeated baseline, select the scenario that actually describes the run.

### Controlled idle

Use for an idle/noise-floor run. Close unnecessary applications and avoid unrelated background work during the measurement.

### Real-world workload

Use when diagnosing gaming, browser/video, audio, Discord or another workload. Keep the applications that reproduce the behavior open; their activity is part of the evidence.

### Before / after comparison

Use for a future controlled A/B comparison. Keep apps, workload, power state and background activity consistent on both sides. Consistency matters more than closing everything.

The selected scenario is evidence provenance. Changing the selector after an existing result must invalidate stale visible/export evidence rather than relabeling an old capture under a new context.

## 4. Controlled-idle observation

Select **Controlled idle**, let the machine settle, then run one five-second observation.

Record:

```text
Scenario:
DPC count:
ISR count:
DPC p99 / p99.9 / max:
ISR p99 / p99.9 / max:
DPC >100 us count/rate:
ISR >25 us count/rate:
DPC/ISR >1 ms local-bucket count/rate:
DPC/ISR >3 ms local-bucket count/rate:
Observed processors:
Resolved module events:
Unresolved module events:
Resolved percentage:
Top resolved modules:
Top observed CPUs:
ETW events lost:
Invalid latency events:
Invalid image events:
Event limit reached:
Runtime system CPU busy %:
Runtime power source:
Runtime active power plan:
Runtime Battery Saver state:
Runtime power context changed during capture? :
```

`p99.9` must be visibly withheld when the corresponding distribution contains fewer than 1,000 samples. The UI should explain that more samples are required while leaving p99/max available.

`DPC >100 µs` and `ISR >25 µs` are driver-guidance context. `>1 ms` and `>3 ms` are LatencyPilot local diagnostic buckets only; they are not Windows pass/fail/user-impact severity boundaries.

The runtime CPU/power values are **measurement provenance**, not a new pass/fail gate. They are sampled immediately before and after the authoritative ETW capture. A missing optional runtime-context sample must be shown/exported as unavailable rather than converted into a zero value, and it must not invalidate otherwise clean DPC/ISR evidence.

A non-zero unresolved count is not automatically a failure. Unknown routine addresses remain unknown rather than being guessed.

Export the JSON and record its SHA-256:

```powershell
Get-FileHash .\LatencyPilot-observation-*.json -Algorithm SHA256
```

The export should use `latencypilot-evidence-v5` and include at least:

- product/protocol version;
- source revision when build metadata provides it;
- export timestamp;
- selected measurement scenario/context;
- bounded non-personal environment/topology context;
- best-effort runtime context (system CPU busy delta plus power source/Battery Saver/active power-plan state before and after capture);
- capture `RequestId`;
- full bounded processor/module/unresolved-routine aggregates;
- capture-integrity metadata.

Unresolved 64-bit routine addresses must remain hexadecimal strings in JSON.

## 5. Real-world controlled-load observation

Select **Real-world workload**. Run one repeatable workload that exercises a representative path, such as a GPU workload, controlled network transfer, known USB activity or the actual application/game that reproduces the problem.

Keep that workload active during the five-second observation. Record the same fields as section 4 plus the exact workload and relevant application state.

The visible runtime-context line should reflect the expected higher background/system load and the actual power source/plan in use. If the plan/source/Battery Saver state changes during the capture, preserve that fact with the evidence rather than treating the run as equivalent to one with stable power context.

Export separately and record its SHA-256. Do not overwrite the controlled-idle evidence artifact.

A single observation remains an observation, not a baseline and not proof of an optimization.

## 6. Repeated baseline quality — Stage C companion evidence

### 6.1 Controlled-idle baseline

Select **Controlled idle**, return the machine to the intended idle condition and let it settle. Press **Build baseline**.

The current quiet baseline flow intentionally minimizes UI activity between authoritative windows:

- it waits for a settle interval before the first capture;
- it does not redraw the full health card, module list, CPU list, tail chart or per-window list between capture windows;
- between windows it updates only lightweight progress/status text;
- runtime CPU/power snapshots are taken immediately around each capture without rendering full context between windows;
- the full observation cards are rendered only after the fifth capture is complete;
- the final observation cards show the final window snapshot, while the baseline verdict uses all five windows.

If detailed contributor lists/charts visibly redraw between windows, treat that as a regression in the quiet-measurement contract.

Record:

```text
Scenario:
Method version:
Window count:
Window 1 integrity / DPC count+p99 / ISR count+p99:
Window 2 integrity / DPC count+p99 / ISR count+p99:
Window 3 integrity / DPC count+p99 / ISR count+p99:
Window 4 integrity / DPC count+p99 / ISR count+p99:
Window 5 integrity / DPC count+p99 / ISR count+p99:
DPC median / spread / drift:
ISR median / spread / drift:
Extreme windows reported:
Runtime CPU busy average/range across sampled windows:
Runtime power source/plan/Battery Saver summary:
Did power context change during any window or across the sequence? :
Overall verdict (Valid/Inconclusive):
Reasons shown:
```

Window numbers `1..N` are the authoritative in-process order. `StartedAtUtc` is provenance, not a monotonic sequencing clock; wall time can move backwards.

For `baseline-quality-v1`, a clean five-window run can still be `Inconclusive` because of insufficient samples, >30% relative P10-P90 noise, >20% early/late drift, or >50% extreme-window deviation. Do not discard inconvenient windows.

Any non-zero ETW loss, invalid latency/image event or event-limit hit makes the affected window ineligible. A lossy/incomplete capture must not receive a healthy interpretation simply because measured values look low.

Runtime CPU/power context is not part of the `baseline-quality-v1` validity formula. It exists so a human/future comparison layer can identify obviously mismatched test conditions without silently changing the versioned baseline methodology.

### 6.2 Real-world repeated baseline

Repeat the five-window flow under one repeatable **Real-world workload**. Keep workload state as consistent as practical through all five windows.

The purpose is to verify that quality/noise/drift reasons behave sensibly under a controlled non-idle condition, not to claim an optimization.

### 6.3 Export baseline evidence

After complete or partial baseline termination, export JSON and record SHA-256. The baseline export must include:

- selected scenario/context;
- every completed bounded aggregate capture and `RequestId`;
- derived window evidence;
- best-effort per-window runtime CPU/power context;
- environment/topology provenance;
- `baseline-quality-v1` identity;
- metric quality;
- all verdict reasons.

Export serialization/file I/O must occur only after the capture sequence stops or completes; it must not add file I/O between authoritative windows.

## 7. External plausibility comparison

Where practical, compare the same machine/workload with an independent observer such as WPA/PerfView or LatencyMon.

The comparison is for broad plausibility of:

- DPC/ISR activity;
- dominant module identities;
- broad processor concentration;
- absence of impossible attribution.

Exact counts/percentiles are not required to match because capture windows, aggregation and observer overhead differ.

If LatencyPilot maps an address to an image that cannot authoritatively contain it, treat that as a blocker.

## 8. Failure-path and authorization cleanup

Exercise at least:

1. close the App during an active observation;
2. relaunch and verify a new status request is accepted promptly;
3. stop the Service during or immediately after an observation;
4. start the Service again and reconnect the normal-user App;
5. run another observation after recovery;
6. close the App during a repeated-baseline window and confirm a partial sequence cannot become `Valid`;
7. where practical, exercise malformed/incompatible local protocol input in a controlled developer environment and verify bounded rejection;
8. where a second interactive session exists, verify a client outside the active console session is rejected without weakening the authorization rule.

After capture-related failure cases, verify no stale `LatencyPilot-Kernel-*` ETW session remains and that a subsequent authorized capture can start normally.

If cleanup or authorization cannot be proven, Stage B remains open.

## 9. Inventory partial-evidence behavior

Inspect representative GPU, NIC and xHCI/USB entries. An unavailable optional property or unreadable interrupt/resource field must not make the entire present-device inventory disappear.

Keep these evidence levels separate:

```text
stored interrupt configuration
allocated IRQ/resource assignment
runtime DPC/ISR behavior
```

A read failure is not equivalent to “no configuration”.

## 10. Accessibility/responsive sanity

On physical WinUI, check at least:

- Light, Dark and Windows High Contrast;
- narrow and wide window widths;
- Windows text scaling at representative enlarged settings;
- keyboard-only access and logical focus order;
- screen-reader/UI Automation reading of status, scenario selector, exact metric values, runtime-context summary and baseline verdict.

The tail-rate bars are visual `Grid`/`Border` data bars, not progress controls. Screen readers should rely on the exact count/denominator/percentage text rather than receive misleading operation-progress semantics.

Color must never be the only state cue; text labels such as capture warning, valid/inconclusive and scenario names must remain understandable without color.

## 11. Zero-mutation boundary

Stage B is read-only. Observation must not alter interrupt affinity, MSI settings, CPU Sets, power settings, network configuration, device policy, timer settings or unrelated services.

Expected persistent changes are limited to LatencyPilot installation/service files and documented diagnostics/log artifacts.

Any unrelated persistent configuration change is a blocker.

## 12. Uninstall / portable Service removal

For installed builds, uninstall normally. For portable validation, use the bundled Service removal path.

Verify afterward:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

Expected result: no LatencyPilot observation Service remains and the protected managed Service payload is removed when uninstall semantics require it.

## Stage B evidence record

Stage B requires:

- exact source/release revision;
- completed green **test-only** Tests workflow run for that deterministic source contract;
- owner-local Windows App/Service build result;
- package/distribution hash and launch smoke when validating a release candidate;
- physical-machine and active-console-session context;
- Controlled-idle observation + JSON/SHA-256/RequestId;
- Real-world workload observation + JSON/SHA-256/RequestId;
- p99.9 adequacy behavior where naturally observable;
- scenario provenance and runtime CPU/power-plan context in exported evidence;
- attribution plausibility comparison;
- cleanup/disconnect/failure-path result;
- representative inventory/resource evidence;
- protected Service-path verification;
- zero-mutation result;
- accessibility/responsive sanity result;
- uninstall/removal result when applicable;
- all unresolved blockers.

For Stage C closure, additionally preserve both controlled-idle and one repeatable real-world repeated-baseline result, including all window evidence, runtime-context windows, verdict/reasons, exported JSON and hashes.

Do not close Stage B or Stage C from VM/CI evidence alone.
