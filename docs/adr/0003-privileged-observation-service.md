# ADR 0003: Introduce the privileged service during Phase 2

- **Status:** Accepted
- **Date:** 2026-09-12
- **Last reviewed:** 2026-09-13
- **Amends:** ADR 0001 service timing; mutation remains deferred to Phase 3

## Context

The original roadmap deferred the Windows Service until Phase 3 because Phase 2 was intended to be read-only. That conflated **read-only** with **unprivileged**.

DPC/ISR observation uses Windows kernel/SystemTraceProvider ETW sessions. Windows restricts control of ETW sessions to elevated administrators, members of Performance Log Users, or service identities such as LocalSystem/LocalService/NetworkService unless additional access-control privileges are configured.

Running the WinUI application elevated would violate LatencyPilot's normal-user UI boundary. Permanently changing the user's group membership or ETW ACL solely to avoid a service would also broaden machine permissions outside LatencyPilot's own boundary.

## Decision

Introduce `LatencyPilot.Service` as the privileged **observation host in Phase 2**.

Phase 2 service authority is strictly read-only:

- start/stop the supported kernel ETW observation session;
- capture/normalize supported DPC/ISR evidence;
- report service/capture health and structured errors;
- no device policy, registry, affinity, MSI, power, RSS or other system mutation commands.

`ServiceBoundary.MutationAvailable` remains `false` throughout Phase 2.

The WinUI application remains non-elevated. Privileged observation crosses the typed local IPC boundary.

Phase 3 reuses this same service and extends the protocol only after durable journaling, authorization and rollback semantics exist. A second privileged helper is not introduced.

## Why not elevate the UI

- It expands the privileged attack surface to XAML/UI/navigation code.
- It makes every normal app action privileged rather than only the required operation.
- It creates unnecessary UAC/admin coupling for a measurement application.

## Why not require Performance Log Users membership

That would persistently broaden the user's ability to control ETW sessions system-wide. LatencyPilot should keep privileged authority inside its own narrow service boundary instead of changing the user's account privileges as a prerequisite.

## Service design constraints

- Windows Service hosting uses the supported .NET hosting integration.
- IPC remains local and command-specific; no generic shell/registry/process primitive is permitted.
- Phase 2 protocol commands are observation-only and explicitly allowlisted.
- request/response frames are bounded and unknown JSON members fail closed;
- the Named Pipe denies network identities and its ACL admits interactive local identities plus the required Windows service identities rather than all authenticated users;
- after connection, Phase 2 resolves the named-pipe client's Windows session and fail-closes unless it matches the active console session; inability to resolve that client session is also a rejection;
- active-console authorization deliberately narrows the current Phase 2 supported path and does not imply RDP/multi-session support;
- the Phase 2 pipe ACL/session rule is **not** sufficient authorization for future mutation; Phase 3 must add mutation-specific authorization/allowlisting;
- a client disconnect, unexpected extra client data, operation deadline, service stop or capture failure must cancel/stop the active observation rather than leave a detached privileged ETW capture running;
- ETW sessions must use collision-resistant LatencyPilot-owned names and must be stopped/cleaned on cancellation, client disconnect, service stop or capture failure;
- a stale LatencyPilot session must be detected and handled explicitly rather than silently attaching to an unrelated session;
- raw kernel addresses are diagnostic evidence; module attribution must be derived from authoritative image/module mapping rather than guessed names;
- successful protocol-v5 capture evidence preserves the request correlation ID so exported evidence can be tied back to App/Service diagnostics;
- expected capture-start/unavailable paths retain bounded structured failure kind/native-error provenance rather than losing the root cause;
- App/Service operational diagnostics are structured and bounded; raw per-event ETW logging is prohibited in the capture hot path;
- a LocalSystem service binary must execute from a protected machine-wide location. Portable registration copies the Service payload to `%ProgramFiles%\LatencyPilot\Service` before registration instead of executing SYSTEM code from a normal user-writable extraction directory.

## Testing impact

This architecture change does not increase the repository's permanent-test cap.

The critical suite was consolidated on 2026-09-13 so high-blast-radius behavior is covered with fewer, broader contract tests rather than one test method per historical branch. The suite currently uses eight permanent test methods, including protocol framing/correlation fail-closed behavior, baseline validity scenarios and an explicit observation-only command-surface bound. The remaining capacity is not “reserved” for a specific feature; later parser/recovery/mutation risks may replace or merge lower-value tests while the repository remains at or below 10.

Implementation-specific Service/ETW probes may be temporary and removed after validation. Physical Windows ETW and active-session authorization validation remain separate from the permanent automated-test count.

## Consequences

### Benefits

- Keeps WinUI non-elevated.
- Creates the privilege boundary before kernel ETW is implemented incorrectly in the UI process.
- The service introduced for observation is reused for later mutation instead of creating duplicate privileged mechanisms.
- Current observation IPC is narrower than a machine-wide authenticated-user surface, is restricted to the active console session, and abandoned clients no longer intentionally own a full capture window.
- Portable distribution no longer turns its extraction directory into the executable location for a LocalSystem binary.

### Costs

- Service lifecycle/IPC work moves earlier in the roadmap.
- Phase 2 packaging and clean-machine validation must account for the service executable even though mutation is disabled.
- Portable kernel observation requires an elevated install/remove step for the protected Service copy.
- RDP/multi-session observation is not automatically supported by the current active-console authorization rule and requires a deliberate future design if needed.

## Phase boundary change

Phase 2 now owns the privileged observation host and read-only IPC needed for ETW. Phase 3 no longer owns creation of the service itself; it owns **mutation authority**, durable journal/recovery, mutation-specific authorization and rollback.
