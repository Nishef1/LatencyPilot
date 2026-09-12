# ADR 0003: Introduce the privileged service during Phase 2

- **Status:** Accepted
- **Date:** 2026-09-12
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
- ETW sessions must use collision-resistant LatencyPilot-owned names and must be stopped/cleaned on cancellation, client disconnect, service stop or capture failure.
- A stale LatencyPilot session must be detected and handled explicitly rather than silently attaching to an unrelated session.
- Raw kernel addresses are diagnostic evidence; module attribution must be derived from authoritative image/module mapping rather than guessed names.

## Testing impact

This architecture change does not increase the repository's permanent-test cap. The existing suite remains at 9 permanent tests. The final tenth slot is reserved for a future high-blast-radius service/protocol/recovery invariant if it proves necessary. Implementation-specific service/ETW probes may be temporary and removed after validation.

## Consequences

### Benefits

- Keeps WinUI non-elevated.
- Creates the privilege boundary before kernel ETW is implemented incorrectly in the UI process.
- The service introduced for observation is reused for later mutation instead of creating duplicate privileged mechanisms.

### Costs

- Service lifecycle/IPC work moves earlier in the roadmap.
- Phase 2 packaging and clean-machine validation must account for the service executable even though mutation is disabled.

## Phase boundary change

Phase 2 now owns the privileged observation host and read-only IPC needed for ETW. Phase 3 no longer owns creation of the service itself; it owns **mutation authority**, durable journal/recovery, mutation-specific authorization and rollback.