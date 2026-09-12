# LatencyPilot Portable

The portable package is a self-contained Windows 11 x64 bundle. It includes the LatencyPilot desktop app, the read-only observation service, .NET runtime files required by the published binaries, Windows App SDK files, service scripts, validation documentation, build metadata and license files.

No separate .NET or Windows App Runtime download is required.

## Use

1. Extract the entire portable ZIP to a writable folder.
2. Keep the extracted folder intact while LatencyPilot is in use.
3. Run `LatencyPilot.exe` directly from the root of the extracted folder.

The portable archive deliberately puts the desktop executable at the top level so the package behaves like an application distribution rather than a build tree. The privileged observation service remains under `Service\`.

## Kernel observation features

LatencyPilot deliberately keeps privileged ETW collection behind a narrow Windows Service boundary. To use DPC/ISR kernel observation and driver/module attribution, register the included read-only service from an elevated PowerShell window:

```powershell
.\Install-Service.ps1
```

The desktop UI itself should still be launched normally, not elevated.

When you want to move or delete the portable folder, unregister the service first:

```powershell
.\Uninstall-Service.ps1
```

The service path points into the extracted portable folder, so moving that folder while the service is registered will break the service path.

## Startup diagnostics

If the WinUI shell cannot be created, LatencyPilot shows a startup error dialog and writes the underlying exception to:

```text
%LOCALAPPDATA%\LatencyPilot\startup-error.log
```

## Safety boundary

Version 0.0.1 remains read-only. The service exposes observation commands only; interrupt affinity, MSI policy, CPU Sets, power policy, networking and device-policy mutation are not enabled.

A single five-second capture is an observation, not a validated baseline or optimization recommendation.
