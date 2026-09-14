# LatencyPilot Portable

The portable package is a self-contained Windows 11 x64 bundle. It includes the LatencyPilot desktop App, the privileged Service payload, required .NET and Windows App SDK runtime files, Service scripts, validation/diagnostics documentation, build metadata and license files.

No separate .NET or Windows App Runtime installation is required for the packaged payload.

## Use

1. Extract the entire portable ZIP to a folder you control.
2. Run `LatencyPilot.exe` from the root of the extracted folder for the normal-user, non-elevated UI and local read-only inventory.
3. Install the optional privileged Service only when kernel DPC/ISR observation is needed.

The archive deliberately puts the desktop executable at the top level so it behaves like an application distribution rather than a build tree. The bundled Service payload remains under `Service\`, but it is **not** registered to run as LocalSystem from that user-writable extraction folder.

## Privileged Service

LatencyPilot keeps kernel ETW collection and future supported privileged mutations behind a narrow Windows Service boundary. The current public protocol remains observation-only and reports `MutationAvailable=false`; internal Phase 3 mutation/recovery source is not user-reachable or armed.

To enable DPC/ISR observation and module attribution, register the bundled Service from elevated PowerShell:

```powershell
.\Install-Service.ps1
```

The install script copies the Service payload to the protected machine-wide location:

```text
%ProgramFiles%\LatencyPilot\Service
```

and registers the Windows Service against that protected copy. The LocalSystem executable is therefore not loaded from the ordinary portable extraction directory.

Launch the desktop App normally, not elevated.

To unregister the Service and remove the protected payload:

```powershell
.\Uninstall-Service.ps1
```

The App remains usable for non-privileged read-only features after the Service is removed.

## Current measurement contract

Current `0.0.2` public App/Service protocol remains read-only. Targeted Phase 3 safety/candidate source exists behind the Service boundary, but no mutation command is exposed and no mutation is armed.

The portable App exposes two different measurement products:

```text
Quick snapshot:      1 × 5 s, diagnostic only
Decision baseline:   baseline-quality-v2
                     5 × 20 s authoritative windows
                     5 s LatencyPilot/service settle before window 1
                     750 ms inter-window settle
Protocol:            v6
Evidence:            latencypilot-evidence-v8
```

A quick snapshot is useful for integrity, module attribution, CPU concentration and hypothesis generation. It is not a validated baseline, health verdict or optimization recommendation.

A decision baseline requires the workload to be warmed/repeatable before the sequence starts where applicable. The initial five-second delay is LatencyPilot/service settling, not workload warm-up.

Evidence intended for Phase 2 closure should be exported and independently checked with `scripts/Verify-Evidence.ps1` from the source tree or the equivalent validation tooling bundled with the matching candidate.

## Diagnostics

Primary structured App logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Primary structured Service logs:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

If WinUI fails before normal diagnostics are available, LatencyPilot also writes the underlying startup exception to:

```text
%LOCALAPPDATA%\LatencyPilot\startup-error.log
```

See `DIAGNOSTICS.md` for correlation, retention and privacy rules.

## Safety boundary

The current public Service surface is observation-only. Interrupt-affinity candidate/apply/revert and recovery code may exist internally for Phase 3 development, but it is not exposed through protocol v6 and must remain unavailable until the physical mutation arming gate in `PROJECT_STATUS.md` passes.

The Named Pipe is local, denies network identities, and admits the required local interactive/service identities before an additional active-console-session authorization check. Failure to establish or match the active client session rejects access.

This observation authorization is **not** mutation authorization. Future public mutation IPC must be mutation-specific, typed and allowlisted, must preserve the durable journal/recovery contract, and must not be introduced before the arming gate is physically proven.

## Removal sanity

After uninstalling the portable Service integration, verify when applicable:

```powershell
Get-Service LatencyPilot.Observation -ErrorAction SilentlyContinue
Test-Path "$env:ProgramFiles\LatencyPilot\Service"
```

A release candidate should not leave an unexplained privileged Service registration or protected payload behind after its documented removal path completes.
