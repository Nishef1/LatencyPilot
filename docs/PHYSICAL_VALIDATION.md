# Physical Windows 11 Validation

This runbook is for **Stage B** validation of the read-only `0.0.x` LatencyPilot observation build and for collecting the companion physical evidence needed by the current Stage C repeated-baseline source. It does not authorize or test system mutation.

## Preconditions

- physical Windows 11 x64 machine;
- `LatencyPilot-<version>-win-x64-setup.exe` downloaded from the matching GitHub prerelease, or the matching portable ZIP when validating that distribution;
- SHA-256 verified against the published `.sha256` companion;
- App must run as a normal user after setup; elevation is used only by setup/service install/uninstall;
- `BUILD_INFO.txt` must identify the exact commit and matching green Tests workflow run;
- `BUILD_INFO.txt` from current owner-local release tooling must record `app_launch_smoke=passed`;
- `docs/DIAGNOSTICS.md` available for log correlation if a failure occurs.

The current Phase 2 privileged observation path supports the **active local console session**. RDP/multi-session access is intentionally not implied: the Service rejects a pipe client whose Windows session does not match the active console session. Broader session support requires a deliberate authorization design rather than weakening this validation rule.

For the current source line, `v0.0.1` is a retired/reserved historical identity and must not be reused. The next candidate prepared by `main` is `0.0.2`; validate the exact version and commit embedded in the package rather than assuming a version from the filename alone.

Record before starting:

```text
LatencyPilot version:
Release revision:
Distribution (setup/portable):
Distribution SHA-256:
Commit (from BUILD_INFO.txt):
Tests workflow run (from BUILD_INFO.txt):
App launch smoke (from BUILD_INFO.txt):
Windows edition/build:
Interactive session type / active console session notes:
CPU:
Motherboard/firmware:
GPU + driver:
Primary NIC + driver:
Primary xHCI/USB controller + driver:
Power source/profile notes:
Other monitoring/overlay tools running:
```

## 1. Install LatencyPilot

### Setup EXE

Run the setup EXE normally and accept the UAC prompt. Setup installs the application under Program Files, installs/starts the `LatencyPilot.Observation` service, and creates a Start menu shortcut.

### Portable ZIP

Extract the archive, launch the desktop App normally, then run the following only when privileged kernel observation is required:

```powershell
.\Install-Service.ps1
```

The portable installer must copy the Service payload to the protected location below before registering LocalSystem execution:

```text
%ProgramFiles%\LatencyPilot\Service\LatencyPilot.Service.exe
```

The Windows Service must not point at the ordinary extracted portable folder.

After either installation path, verify from an elevated PowerShell:

```powershell
Get-Service LatencyPilot.Observation
sc.exe qc LatencyPilot.Observation
```

Expected state: `Running`. The configured binary path must resolve to the protected Program Files Service path.

The distribution contains `VERSION.txt`, `BUILD_INFO.txt`, the validation runbook and diagnostics guide so every physical result can be tied to an exact release, Tests run, launch-smoke result and commit.

## 2. Start the App without elevation

Launch **LatencyPilot** as a normal user in the active local console session. The desktop App must show that the privileged observation service is connected while mutation remains disabled.

Confirm the visible product version matches `VERSION.txt`. Confirm the safety boundary distinguishes these evidence levels instead of presenting them as interchangeable:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Exercise the current keyboard paths at least once:

```text
Ctrl+R  refresh service
Ctrl+O  capture one observation
Ctrl+B  build repeated baseline
Ctrl+E  export the latest completed aggregate evidence as JSON
```

`Ctrl+E` must remain disabled while no completed exportable evidence exists and while a capture sequence is active.

If the UI reports a failure, record the visible message and preserve the relevant App/Service structured log entries. Protocol v5 capture evidence carries the same `RequestId` used by App/Service diagnostics, so exported JSON should correlate directly to those log entries without relying on timestamp matching alone. Do not attach unrelated sensitive system information.

## 3. Idle observation

Leave the machine otherwise idle for a short settling period, then run the five-second kernel observation.

Record:

```text
DPC count:
ISR count:
DPC p99 / p99.9:
ISR p99 / p99.9:
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
```

`p99.9` is intentionally absent when the corresponding DPC/ISR distribution contains fewer than 1,000 samples. That is not a capture failure; it is the current sample-adequacy rule for exposing that extreme percentile. p99/max remain available according to their normal contracts.

The `>100 µs` DPC and `>25 µs` ISR rows are Microsoft driver-guidance evidence. The `>1 ms` and `>3 ms` rows are LatencyPilot local diagnostic tail buckets only; do not treat them as official Windows pass/fail or user-impact severity thresholds.

A non-zero unresolved count is not automatically a failure. LatencyPilot intentionally preserves unknown routine addresses instead of guessing a driver identity.

After the observation completes, export the evidence JSON. The export must contain the full bounded processor/module/unresolved-routine aggregates and capture-integrity metadata returned by the protocol, not only the compact values visible in the dashboard. It must also contain the capture `RequestId`, protocol version, product version, export timestamp and bounded non-personal environment provenance. Unresolved 64-bit routine addresses are exported as hexadecimal strings to avoid precision loss in JSON/JavaScript tooling.

Record the evidence artifact:

```powershell
Get-FileHash .\LatencyPilot-observation-*.json -Algorithm SHA256
```

Keep the exported filename and SHA-256 with the validation record.

## 4. Controlled-load observation

Run one repeatable workload that exercises a representative device path, then capture another five-second observation. Examples include a GPU workload, controlled network transfer, or known USB activity.

Record the same fields as the idle observation plus the exact workload used. Do not interpret a single observation as a baseline or as proof of an optimization.

Export this observation separately and record its SHA-256 as well. Do not overwrite the idle evidence artifact.

## 5. Repeated baseline quality — Stage C companion evidence

This section collects physical evidence for the current `baseline-quality-v1` source. It is useful during the same machine session as Stage B but does not replace the single-observation checks above.

First return the machine to the intended baseline condition and allow it to settle. Use **Build baseline** to capture the five authoritative windows. Do not manually discard an inconvenient window.

Record:

```text
Method version:
Window count:
Window 1 integrity / DPC count+p99 / ISR count+p99:
Window 2 integrity / DPC count+p99 / ISR count+p99:
Window 3 integrity / DPC count+p99 / ISR count+p99:
Window 4 integrity / DPC count+p99 / ISR count+p99:
Window 5 integrity / DPC count+p99 / ISR count+p99:
DPC median / P10 / P90 / relative noise / drift:
ISR median / P10 / P90 / relative noise / drift:
Extreme windows reported:
Overall verdict (Valid/Inconclusive):
Reasons shown:
```

The source contract requires a contiguous capture sequence. Window numbers must be `1..N`; they are the authoritative ordering for the in-process sequence. `StartedAtUtc` remains provenance and is not treated as a monotonic clock because Windows/NTP/VM time synchronization can move wall time backwards. Missing/gapped window numbers remain invalid. Future persisted evidence that requires stronger timing guarantees should record an explicit monotonic/sequence field rather than inferring monotonicity from UTC.

For current `baseline-quality-v1`, a clean five-window run can still be `Inconclusive` because of insufficient event counts, >30% relative P10–P90 noise, >20% early/late drift, or >50% extreme-window deviation. That is an expected quality result, not a reason to suppress evidence.

Repeat the baseline flow under one controlled, repeatable workload when practical. The purpose is to confirm that the quality UI and reasons behave sensibly under a different but intentionally controlled operating condition, not to prove an optimization.

A non-zero ETW loss count, invalid latency/image events or event-limit hit must make the affected baseline evidence ineligible. If the UI reports `Valid` despite any of those conditions, treat it as a blocker. Likewise, an integrity-warning capture must not receive a healthy/within-guidance classification merely because its observed threshold counts are low.

After a complete or partially completed baseline sequence, export the evidence JSON and record its SHA-256. The baseline export must contain each completed bounded aggregate capture (including its `RequestId`), the derived window evidence, bounded environment provenance, `baseline-quality-v1` method identity, metric quality and all verdict reasons. Export preparation occurs only after the capture sequence stops or completes; it must not add file I/O between authoritative baseline windows.

## 6. External plausibility comparison

Where practical, compare the same machine/workload with a trusted external observer such as PerfView or LatencyMon.

The comparison is for plausibility of:

- active DPC/ISR behavior;
- dominant driver/module identities;
- broad processor concentration;
- absence of obviously impossible attribution.

Exact counts or percentile values are not expected to be identical across tools because capture windows, aggregation and overhead differ.

If LatencyPilot resolves a routine address to a module that does not contain that address in authoritative image mapping, treat it as a blocker.

## 7. Failure-path and authorization cleanup

Exercise at least these cases one at a time:

1. close the App during an active observation;
2. reconnect/relaunch and verify a new status request is accepted promptly;
3. stop the Service during or immediately after an observation;
4. start the Service again and reconnect the normal-user App;
5. perform another observation after recovery;
6. close the App during a repeated-baseline window and confirm the baseline cannot become `Valid` from the partial sequence;
7. where practical, send/trigger a malformed or incompatible local protocol request in a controlled developer environment and verify the Service rejects it without terminating;
8. where a second Windows interactive session is available, verify a client outside the active console session is rejected and Service log EventId `1007` records the bounded session rejection; do not broaden the ACL/session policy merely to make this case connect.

After each capture-related case verify there is no lingering `LatencyPilot-Kernel-*` ETW session and that a subsequent authorized observation can start normally. The App/Service logs should show bounded cancellation/rejection evidence rather than an orphaned capture continuing for the full requested window. Expected ETW/capture-start failures should preserve EventId `1006` failure-kind/native-error provenance when applicable.

If cleanup or the active-session authorization boundary cannot be proven, Stage B remains open.

## 8. Inventory partial-evidence behavior

Inspect representative GPU, NIC and xHCI/USB entries. A device with an unavailable optional property or unreadable interrupt/resource metadata must not cause the entire present-device inventory to disappear.

Record where evidence is partial and distinguish:

```text
stored interrupt configuration
allocated IRQ/resource assignment
runtime DPC/ISR behavior
```

A read failure is not equivalent to “no configuration”.

## 9. Zero-mutation boundary

Stage B is read-only. During these observations LatencyPilot must not alter interrupt affinity, MSI settings, CPU Sets, power settings, network configuration, device policy, timer settings or unrelated services.

The only expected persistent machine changes from setup/service installation are LatencyPilot application/service files, shortcuts/uninstall metadata and the `LatencyPilot.Observation` Windows Service. Structured diagnostic logs under the documented local App/Service log directories are also expected runtime artifacts.

Any unrelated persistent configuration change is a release blocker.

## 10. Uninstall / portable service removal

For setup installation, use **Settings → Apps → Installed apps → LatencyPilot → Uninstall**. Setup removal must stop/delete `LatencyPilot.Observation` before removing application files.

For portable validation, run from the extracted bundle:

```powershell
.\Uninstall-Service.ps1
```

Verify afterward:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

Expected result: no service is returned and the protected Service payload directory is absent.

## Stage B evidence record

A Stage B result is acceptable only when the validation record includes:

- release version/revision;
- distribution type and SHA-256;
- `BUILD_INFO.txt` commit SHA;
- matching green Tests workflow run (critical suite + Windows-host compile gate);
- owner-local App launch-smoke result;
- physical-machine context and active-console session context;
- idle observation plus exported JSON filename/SHA-256 and RequestId correlation;
- controlled-load observation plus exported JSON filename/SHA-256 and RequestId correlation;
- p99.9 adequacy behavior when sample counts are below/above the 1,000-sample rule where naturally observable;
- attribution plausibility comparison;
- cleanup/disconnect/failure-path result;
- active-session authorization result where a second session is practical;
- representative partial-inventory evidence;
- protected Service-path verification;
- zero-mutation result;
- uninstall/removal result;
- any unresolved blockers.

For Stage C closure, additionally preserve the repeated-baseline evidence from section 5, including all window rows, verdict/reasons and exported JSON filename/SHA-256.

Do not close Stage B or Stage C from VM/CI evidence alone.
