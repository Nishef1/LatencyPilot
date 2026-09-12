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

- compact JSON events;
- daily rolling plus a 32 MiB per-file size limit;
- at most 10 retained files per process;
- asynchronous bounded buffering;
- non-blocking behavior when the async buffer is saturated;
- periodic flushing and shutdown flushing;
- `Component` and `ProcessId` context.

Failure to initialize file logging must not prevent the App or Service from starting. Diagnostics are evidence about the product; they are not a prerequisite for the product to run.

## Correlation and event identity

Every observation request already owns a protocol `RequestId`. The App logs request start/completion/failure with that ID and the Service logs capture lifecycle events with the same ID. This is the primary correlation key for App → Named Pipe → Service → ETW investigation.

Stable Service event IDs currently include:

| Event ID | Meaning |
| ---: | --- |
| 1001 | unexpected kernel-capture failure |
| 1002 | kernel capture started |
| 1003 | kernel capture completed |
| 1004 | client disconnected or violated framing while an operation was active |
| 1005 | framed pipe request rejected before execution |

New event IDs should represent durable operational concepts rather than individual code branches.

## What should be logged

Log bounded lifecycle and failure evidence such as:

- App initialization and main-window activation;
- Service connection attempts/results;
- protocol request ID, command, deadline and elapsed duration;
- malformed/oversized/incompatible IPC frames;
- capture start/completion/cancellation/failure;
- ETW event-loss/invalid/event-limit summaries;
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

A future diagnostics-export feature must apply explicit redaction before collection leaves the local machine.

## Measurement-safety rule

Logging must not become part of the latency measurement workload. Do not emit a log entry for every ETW event or perform synchronous disk I/O from an ETW callback. Under pathological log pressure, dropping low-value asynchronous diagnostic events is preferable to blocking the observation path and contaminating the measurement.

Raw/auditable benchmark evidence is a separate product concern from operational logging. Do not replace benchmark persistence with logs.

## Failure handling

Expected transport/protocol failures should produce bounded warnings/errors and leave the long-lived Service able to accept the next client. Unexpected App boundary failures should be logged and converted into a safe unavailable/error state where possible rather than leaking full exception details into the UI.

When investigating a capture problem, start with the App log entry for the relevant `RequestId`, then find the matching Service entries. If no Service entry exists, investigate connection/ACL/service-lifecycle failures before the ETW layer.
