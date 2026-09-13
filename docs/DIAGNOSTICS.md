# LatencyPilot Diagnostics and Logging

Status: **Active diagnostics contract**  
Last updated: 2026-09-13

LatencyPilot uses bounded structured local logs to make App, IPC, Service and ETW failures diagnosable without turning logging into a second telemetry system or adding measurable work to the latency-capture hot path.

## Log locations

Desktop App:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Privileged Service:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

The App and Service intentionally write separate files. They are correlated by the protocol `RequestId` rather than by sharing one multi-process log file.

If WinUI fails before normal diagnostics are usable, the existing last-resort startup file remains:

```text
%LOCALAPPDATA%\LatencyPilot\startup-error.log
```

That fallback file is not the primary diagnostics stream.

## Implementation

`Microsoft.Extensions.Logging` remains the Service logging abstraction. Serilog provides structured file persistence. The App uses the same Serilog file format directly because it does not host the .NET Generic Host.

Current sinks use:

- rendered Compact Log Event Format (CLEF) JSON events so a human-readable `@m` message is available without reimplementing message-template rendering;
- daily rolling plus a 32 MiB per-file size limit;
- at most 10 retained files per process;
- asynchronous bounded buffering;
- non-blocking behavior when the async buffer is saturated;
- periodic flushing and shutdown flushing;
- `Component` and `ProcessId` context.

Failure to initialize file logging must not prevent the App or Service from starting. Diagnostics are evidence about the product; they are not a prerequisite for the product to run.

## Live development diagnostics

`live.ps1` keeps the WinUI App non-elevated, installs/runs the privileged observation host through the Windows Service Control Manager, and streams the existing App and Service CLEF files into the same terminal with `[APP]` and `[SERVICE]` prefixes.

This is deliberately a presentation layer over the authoritative structured files rather than a second logging pipeline. The live viewer uses PowerShell file-following only; it does not add synchronous console sinks to the App or Service and does not emit per-event ETW diagnostics.

```powershell
.\live.ps1
```

Remote-code auto-pull is deliberately **disabled by default** because incoming shared/Service changes can require rebuilding and reinstalling the privileged LocalSystem Service. Enable it only as an explicit development choice:

```powershell
.\live.ps1 -AutoPull
```

`-NoAutoPull` remains accepted for compatibility and cannot be combined with `-AutoPull`. Use `-NoLogs` when terminal streaming is not wanted. Live terminal output is a development aid, not benchmark evidence and not a replacement for the persisted structured logs.

The App must not be launched from an elevated terminal. When the development Service needs to be refreshed, `live.ps1` requests elevation only for `scripts/Install-Service.ps1`, which copies the built Service payload to the protected `%ProgramFiles%\LatencyPilot\Service` path and registers/starts it as LocalSystem. This preserves the normal-user WinUI boundary while retaining the privilege required for kernel ETW observation.

## Correlation and event identity

Every observation request owns a protocol `RequestId`. The App logs request start/completion/failure with that ID and the Service logs capture lifecycle events with the same ID. Protocol v5 also carries the capture `RequestId` inside exported capture evidence, so a saved evidence JSON can be correlated back to App and Service diagnostics without depending on timestamps alone.

Stable Service event IDs currently include:

| Event ID | Meaning |
| ---: | --- |
| 1000 | structured Service file logging unavailable; default providers remain active |
| 1001 | unexpected kernel-capture failure |
| 1002 | kernel capture started |
| 1003 | kernel capture completed |
| 1004 | active observation cancelled after client disconnect/protocol activity while the operation was running |
| 1005 | framed pipe request rejected before execution |
| 1006 | expected kernel-capture unavailability with bounded failure kind/native error provenance |
| 1007 | pipe client rejected because its Windows session is not the active console session, or its session identity cannot be established |

New event IDs should represent durable operational concepts rather than individual code branches.

## What should be logged

Log bounded lifecycle and failure evidence such as:

- App initialization and main-window activation;
- Service connection attempts/results;
- protocol request ID, command, deadline and elapsed duration;
- malformed/oversized/incompatible IPC frames;
- capture start/completion/cancellation/failure;
- expected ETW-start/capture failures with exception type and native error where available;
- ETW event-loss/invalid/event-limit summaries;
- rejected local client-session access at the privileged IPC boundary;
- unexpected exceptions at App/Service boundaries;
- partial inventory failures where a user-facing diagnostic is required.

Prefer structured properties over concatenated diagnostic text.

## What must not be logged by default

Do not log:

- the raw DPC/ISR event stream;
- hundreds of thousands of per-event records;
- arbitrary registry dumps;
- environment-variable dumps;
- credentials, tokens or secrets;
- user names or personal file contents merely for convenience;
- arbitrary command lines;
- complete device/driver inventories on every refresh when a bounded summary is enough.

Evidence export is separate from diagnostics. Current local evidence JSON contains the bounded observation/baseline aggregates, protocol correlation ID and a small non-personal environment summary; it does not upload data or turn operational logs into telemetry. Any future support/export path that sends data off-machine must define explicit redaction and user consent first.

## Measurement-safety rule

Logging must not become part of the latency measurement workload. Do not emit a log entry for every ETW event or perform synchronous disk I/O from an ETW callback. Under pathological log pressure, dropping low-value asynchronous diagnostic events is preferable to blocking the observation path and contaminating the measurement.

Raw/auditable benchmark evidence is a separate product concern from operational logging. Do not replace benchmark persistence with logs.

## Failure handling

Expected transport/protocol failures should produce bounded warnings/errors and leave the long-lived Service able to accept the next client. Expected kernel-capture failures should preserve enough failure provenance in the Service log to distinguish access/Win32/state failures without leaking implementation details into the UI. Unexpected App boundary failures should be logged and converted into a safe unavailable/error state where possible rather than leaking full exception details into the UI.

When investigating a capture problem, start with the App log entry for the relevant `RequestId`, then find the matching Service entries. Exported protocol-v5 evidence carries the same capture `RequestId`. If no Service entry exists, investigate connection/ACL/session-authorization/service-lifecycle failures before the ETW layer.
