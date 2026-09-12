# LatencyPilot Portable

The portable package is a self-contained Windows 11 x64 bundle. It includes the LatencyPilot desktop app, the read-only observation service payload, .NET runtime files required by the published binaries, Windows App SDK files, service scripts, validation/diagnostics documentation, build metadata and license files.

No separate .NET or Windows App Runtime download is required.

## Use

1. Extract the entire portable ZIP to a folder you control.
2. Run `LatencyPilot.exe` directly from the root of the extracted folder for normal-user, non-elevated UI and local read-only inventory.
3. Install the optional privileged observation service only if you need kernel DPC/ISR observation.

The portable archive deliberately puts the desktop executable at the top level so the package behaves like an application distribution rather than a build tree. The bundled Service payload remains under `Service\`, but it is **not** registered to run as LocalSystem from that extracted folder.

## Kernel observation features

LatencyPilot deliberately keeps privileged ETW collection behind a narrow Windows Service boundary. To use DPC/ISR kernel observation and driver/module attribution, register the included read-only service from an elevated PowerShell window:

```powershell
.\Install-Service.ps1
```

For security, the script copies the Service payload into the protected machine-wide location:

```text
%ProgramFiles%\LatencyPilot\Service
```

and registers the Windows Service against that protected copy. The extracted portable folder therefore remains movable after installation; the LocalSystem service executable is not loaded from a normal user-writable extraction path.

The desktop UI itself should still be launched normally, not elevated.

To unregister the observation service and remove the protected Service payload:

```powershell
.\Uninstall-Service.ps1
```

The App remains usable for non-privileged read-only features after the Service is removed.

## Diagnostics

Primary structured App logs:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Primary structured Service logs:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

If the WinUI shell cannot be created before normal diagnostics are available, LatencyPilot also writes the underlying startup exception to the last-resort file:

```text
%LOCALAPPDATA%\LatencyPilot\startup-error.log
```

See `DIAGNOSTICS.md` for correlation, retention and privacy rules.

## Safety boundary

Version 0.0.1 remains read-only. The service exposes observation commands only; interrupt affinity, MSI policy, CPU Sets, power policy, networking and device-policy mutation are not enabled.

The Named Pipe is local, denies network identities, and grants the Phase 2 observation surface to interactive local sessions plus the required Windows service identities. Future mutation commands require a separate mutation-specific authorization design; this Phase 2 access rule must not be reused as mutation authorization.

A single five-second capture is an observation, not a validated baseline or optimization recommendation.
