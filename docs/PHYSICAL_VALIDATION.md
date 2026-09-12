# Physical Windows 11 Validation

This runbook is for **Stage B** validation of the read-only `0.0.x` LatencyPilot observation build. It does not authorize or test system mutation.

## Preconditions

- physical Windows 11 x64 machine;
- extracted release directory kept in place for the duration of validation;
- `VERSION.txt` and `BUILD_INFO.txt` present in the bundle;
- release ZIP SHA-256 verified against the published `.sha256` file;
- App must be run as a normal user; only service installation/removal requires elevation.

Record before starting:

```text
LatencyPilot version:
Commit:
Windows edition/build:
CPU:
Motherboard/firmware:
GPU + driver:
Primary NIC + driver:
Primary xHCI/USB controller + driver:
Power source/profile notes:
Other monitoring/overlay tools running:
```

## 1. Install the observation service

Open PowerShell **as Administrator** in the extracted release directory and run:

```powershell
.\Install-Service.ps1
Get-Service LatencyPilot.Observation
```

Expected state: `LatencyPilot.Observation` is `Running` and its binary path points into the extracted release directory.

Do not move or delete the extracted release directory while the service is installed.

## 2. Start the App without elevation

Launch:

```text
App\LatencyPilot.exe
```

The desktop App should remain non-elevated and report that the privileged observation service is connected while mutation remains disabled.

Record any service-contract, Named Pipe, timeout or protocol error exactly as shown.

## 3. Idle observation

Leave the machine otherwise idle for at least a short settling period, then run the five-second kernel observation.

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
Top resolved module:
ETW events lost:
Invalid latency events:
Invalid image events:
Event limit reached:
```

A non-zero unresolved count is not automatically a failure. LatencyPilot intentionally preserves unknown routine addresses instead of guessing a driver identity.

## 4. Controlled-load observation

Run one repeatable workload that exercises a representative device path, then capture another five-second observation. Examples include a GPU workload, controlled network transfer, or known USB activity.

Record the same fields as the idle observation plus the exact workload used. Do not interpret a single observation as a baseline or as proof of an optimization.

## 5. External plausibility comparison

Where practical, compare the same machine/workload with a trusted external observer such as PerfView or LatencyMon.

The comparison is for plausibility of:

- active DPC/ISR behavior;
- dominant driver/module identities;
- broad processor concentration;
- absence of obviously impossible attribution.

Exact counts or percentile values are not expected to be identical across tools because capture windows, aggregation and overhead differ.

If LatencyPilot resolves a routine address to a module that does not contain that address in authoritative image mapping, treat it as a blocker.

## 6. Failure-path cleanup

Exercise at least these cases one at a time:

1. close/disconnect the App around an observation;
2. stop the service during or immediately after an observation;
3. restart the service and reconnect the normal-user App;
4. perform another observation after recovery.

After each case verify there is no lingering `LatencyPilot-Kernel-*` ETW session and that a subsequent observation can start normally.

If cleanup cannot be proven, Stage B remains open.

## 7. Zero-mutation boundary

Stage B is read-only. During these observations LatencyPilot must not alter interrupt affinity, MSI settings, CPU Sets, power settings, network configuration, device policy, timer settings or unrelated services.

The only expected persistent machine change from this bundle is creation/removal of the `LatencyPilot.Observation` Windows Service by the explicit install/uninstall scripts.

Any other persistent configuration change is a release blocker.

## 8. Remove the service

When validation is complete, open elevated PowerShell in the extracted release directory and run:

```powershell
.\Uninstall-Service.ps1
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
```

Expected result: the service no longer exists. Release files are intentionally left untouched.

## Stage B evidence record

A Stage B result is acceptable only when the validation record includes:

- `VERSION.txt` value;
- `BUILD_INFO.txt` commit SHA;
- physical-machine context;
- idle observation;
- controlled-load observation;
- attribution plausibility comparison;
- cleanup/failure-path result;
- zero-mutation result;
- any unresolved blockers.

Do not close Stage B from VM/CI evidence alone.
