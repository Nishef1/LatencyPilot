# LatencyPilot Diagnostics and Logging

Status: **Active diagnostics contract**  
Last updated: 2026-09-14

LatencyPilot uses bounded structured local diagnostics so App, IPC, Service and ETW failures can be reconstructed without turning logging into a second telemetry system or contaminating the measurement hot path.

Current related contracts:

```text
Observation protocol:   v6
Evidence schema:        latencypilot-evidence-v8
Quick snapshot purpose: quick-diagnostic-snapshot
Decision baseline:      baseline-quality-v2
Public mutation:        unavailable / unarmed
```

## Log locations

Desktop App:

```text
%LOCALAPPDATA%\LatencyPilot\Logs\App\latencypilot-app-*.json
```

Privileged Service:

```text
%PROGRAMDATA%\LatencyPilot\Logs\Service\latencypilot-service-*.json
```

The App and Service intentionally write separate files. Correlation uses the protocol `RequestId`, not a shared multi-process file or timestamp guessing.

If WinUI fails before normal diagnostics are usable, the last-resort startup file remains:

```text
%LOCALAPPDATA%\LatencyPilot\startup-error.log
```

That fallback is not the primary diagnostics stream.

## Implementation contract

`Microsoft.Extensions.Logging` remains the Service logging abstraction. Serilog provides bounded structured file persistence. The App uses the same compact JSON/CLEF format directly because it does not host the .NET Generic Host.

Current sinks use:

- rendered Compact Log Event Format events, including a human-readable `@m` field;
- daily rolling with a 32 MiB per-file limit;
- at most 10 retained files per process;
- asynchronous bounded buffering;
- non-blocking behavior when the async buffer is saturated;
- periodic/shutdown flushing;
- stable `Component` and `ProcessId` context.

Failure to initialize file logging must not prevent App or Service startup. Diagnostics explain product behavior; they are not a prerequisite for measurement.

## Live development diagnostics

`live.ps1` keeps the WinUI App non-elevated, installs/runs the privileged observation host through the Service Control Manager, and streams the existing App/Service CLEF files into the same terminal with `[APP]` and `[SERVICE]` prefixes.

```powershell
.\live.ps1
```

The terminal viewer is only a presentation layer over persisted logs. It must not add a synchronous console sink or per-event ETW logging to the measured process.

Remote-code auto-pull is deliberately disabled by default because an incoming shared/Service change can rebuild and reinstall the privileged LocalSystem Service. Enable it only explicitly:

```powershell
.\live.ps1 -AutoPull
```

`-NoAutoPull` remains accepted for compatibility and cannot be combined with `-AutoPull`. `-NoLogs` disables terminal streaming.

The App must not be launched elevated. `live.ps1` requests UAC only for `scripts/Install-Service.ps1`, which copies the Service payload to `%ProgramFiles%\LatencyPilot\Service` and registers/starts it as LocalSystem.

## Correlation and event identity

Every observation request owns a protocol-v6 `RequestId`.

The same ID is used for:

```text
App request lifecycle
→ framed IPC request/response
→ Service capture lifecycle
→ protocol-v6 KernelLatencyCaptureResponse
→ evidence-v8 capture object
```

This lets a saved evidence artifact be correlated to App/Service logs without depending on wall-clock ordering alone.

Stable Service event IDs currently include:

| Event ID | Meaning |
| ---: | --- |
| 1000 | structured Service file logging unavailable; default providers remain active |
| 1001 | unexpected kernel-capture failure |
| 1002 | kernel capture started |
| 1003 | kernel capture completed |
| 1004 | active observation cancelled after client disconnect/protocol activity |
| 1005 | framed pipe request rejected before execution |
| 1006 | expected kernel-capture unavailability with bounded failure/native-error provenance |
| 1007 | pipe client rejected because its session is not the active console session, or session identity cannot be established |

New event IDs must represent durable operational concepts rather than individual code branches.

## Bounded post-capture summaries

After a capture response passes fail-closed protocol validation, the App may write bounded **post-capture** summaries. These happen after the authoritative ETW collection interval and therefore must not add work to the ETW callback path.

The aggregate summary may include:

- requested/actual capture duration;
- DPC/ISR event counts;
- p99 and maximum duration;
- p99.9 only when protocol-v6 sample adequacy permits it (`>=10,000` samples for that distribution);
- Microsoft driver-duration reference exceedance counts/rates (`>100 µs` DPC, `>25 µs` ISR);
- LatencyPilot local `>1 ms` / `>3 ms` diagnostic buckets;
- ETW loss, invalid latency/image events and event-limit state.

The concentration/attribution summary may include:

- resolved/unresolved module counts and coverage percentage;
- highest-count DPC CPU and share;
- highest-count ISR CPU and share;
- module contributing the most DPC reference exceedances;
- module contributing the most ISR reference exceedances.

Only bounded module names belong in this concise operational summary. Full bounded evidence belongs in the explicit JSON export. Raw DPC/ISR events must not be logged.

## Evidence verification

`scripts/Verify-Evidence.ps1` is the independent local verifier for saved evidence-v8 artifacts. It validates envelope/provenance, purpose, source revision, RequestId uniqueness, capture shape, p99.9 sample semantics and SHA-256.

Example:

```powershell
.\scripts\Verify-Evidence.ps1 .\capture.json `
  -ExpectedCommit <exact-clean-source-revision> `
  -ExpectedSha256 <64-hex-digest>
```

`-RequireCleanCapture` adds a measurement-integrity gate. It fails when any capture has unavailable/non-zero ETW loss, invalid latency/image events, or an event-limit hit.

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-observation-*.json `
  -ExpectedCommit <exact-clean-source-revision> `
  -RequireCleanCapture
```

`-RequireValidBaseline` is stronger and applies only to `purpose=repeated-decision-baseline`. It requires the current `baseline-quality-v2` closure contract, including:

```text
exactly 5 aligned captures/windows/runtime windows
requested duration >= 20,000 ms per window
actual duration >= 95% of request per window
clean capture integrity
DPC event count >= 1,000 per window
ISR event count >= 1,000 per window
Status = Valid
IsValidForComparison = true
valid capture windows = 5/5
```

Use both strict switches for Phase 2 decision-baseline closure:

```powershell
.\scripts\Verify-Evidence.ps1 .\LatencyPilot-baseline-*.json `
  -ExpectedCommit <exact-clean-source-revision> `
  -RequireCleanCapture `
  -RequireValidBaseline
```

A partial/noisy/undersampled/short baseline remains exportable diagnostic evidence, but it cannot pass the closure-ready gate.

A five-second `quick-diagnostic-snapshot` can pass envelope and clean-capture verification while still remaining explicitly **non-decision-grade**. Clean integrity is necessary, not sufficient, for a benchmark claim.

## What should be logged

Log bounded lifecycle/failure evidence such as:

- App initialization and main-window activation;
- Service connection attempts/results;
- request ID, command, deadline and elapsed duration;
- malformed/oversized/incompatible IPC frames;
- capture start/completion/cancellation/failure;
- bounded post-capture aggregate/concentration summaries after response validation;
- expected ETW-start/capture failures with exception type and native error where available;
- ETW event-loss/invalid/event-limit summaries;
- rejected local client-session access at the privileged boundary;
- mutation-journal startup/recovery readiness outcomes while the public mutation surface remains unarmed;
- unexpected App/Service boundary exceptions;
- partial inventory failures where user-visible diagnostics are required.

Prefer structured properties over concatenated diagnostic text.

## What must not be logged by default

Do not log:

- raw DPC/ISR event streams;
- one log entry per ETW sample;
- arbitrary registry dumps;
- environment-variable dumps;
- credentials, tokens or secrets;
- user names or personal file contents merely for convenience;
- arbitrary shell/process command lines;
- complete device/driver inventories on every refresh when a bounded summary is enough.

Evidence export is separate from diagnostics. Current evidence is local and bounded; it does not upload data or turn logs into telemetry. Any future support/export path that sends data off-machine requires explicit redaction and user consent.

## Measurement-safety rule

Diagnostics must not become part of the workload being measured.

Never perform synchronous disk I/O from ETW callbacks. Never log one record per DPC/ISR event. Under pathological log pressure, dropping low-value asynchronous diagnostic records is preferable to blocking the observation path and changing the result being measured.

Raw/auditable benchmark evidence is a separate product concern from operational logs. Logs do not replace evidence persistence or the durable experiment journal.

## Failure handling

Expected transport/protocol failures should produce bounded diagnostics and leave the long-lived Service able to accept the next client. Expected kernel-capture failures should preserve enough root-cause provenance to distinguish access/Win32/state failures without leaking unnecessary implementation detail into the UI.

Unexpected App-boundary failures should be logged and converted into a safe unavailable/error state where possible rather than exposing raw exceptions to the user.

When investigating a capture problem:

1. start from the evidence/request `RequestId` when available;
2. locate the matching App entry;
3. locate the matching Service entries;
4. if no Service capture entry exists, investigate connection/ACL/session/service-lifecycle failure before the ETW layer;
5. if capture completed, reconcile the logged bounded summary against the saved evidence-v8 artifact rather than trusting either source in isolation.
