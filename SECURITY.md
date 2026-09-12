# Security Policy

LatencyPilot interacts with privileged Windows observation today and is designed to interact with narrowly supported Windows configuration later. Security, authorization and recovery defects are therefore first-class issues.

## Supported versions

LatencyPilot is currently pre-alpha and has no stable supported release line yet.

Once stable releases begin, this file will list supported versions and security-fix policy.

## Please report privately

Do **not** open a public issue for vulnerabilities that could enable or materially increase the risk of:

- local privilege escalation;
- arbitrary command or PowerShell execution through the service;
- arbitrary registry mutation through privileged IPC;
- bypass of command authorization or validation;
- malicious or unintended device-policy mutation;
- tampering with rollback/recovery state;
- DLL/path hijacking involving privileged components;
- unsafe update/install behavior;
- exposure of secrets or sensitive diagnostic information;
- a reliable denial of service against the privileged observation/mutation boundary;
- a failure that leaves a future system-tuning experiment in an unsafe or unrecoverable state.

Use GitHub's private vulnerability reporting feature if it is enabled for this repository. If private reporting is not available, contact the repository owner through a private GitHub-supported channel before disclosing exploit details publicly.

Do not include credentials, personal files, or unrelated sensitive system information in a report.

## What to include

A useful report should include:

- affected commit/release;
- Windows 11 build;
- concise impact statement;
- affected component;
- reproduction steps;
- whether administrator/SYSTEM privileges are required;
- whether user interaction is required;
- logs or sanitized traces if relevant;
- suggested mitigation if known.

## Current security architecture

LatencyPilot follows these baseline rules:

1. The WinUI 3 desktop application is non-elevated during normal use.
2. Privileged kernel observation is isolated in the narrowly scoped `LatencyPilot.Observation` Windows Service.
3. Phase 2 service authority is read-only; mutation commands do not exist and `MutationAvailable` remains false.
4. The service exposes typed, versioned, allow-listed IPC commands rather than arbitrary execution primitives.
5. Protocol framing is bounded and fails closed on incompatible/unknown fields.
6. Network identities are denied at the Named Pipe boundary; the observation surface is limited to interactive local identities plus required service identities rather than all authenticated users.
7. A disconnected/abandoned client must not leave its privileged capture intentionally running for the full requested window.
8. The LocalSystem service executable is installed under a protected Program Files path; the portable package does not register SYSTEM execution directly from an ordinary user-writable extraction folder.
9. App and Service diagnostics are local, structured and bounded; raw per-event ETW logging and unnecessary sensitive-data collection are prohibited by default.
10. A failed verification or unavailable source is not manufactured into success/evidence.
11. Security features must not be disabled merely to chase benchmark gains.

## Required before mutation ships

Phase 3 mutation is not authorized by the current Phase 2 observation boundary. Before any supported system mutation becomes available, LatencyPilot must add and validate:

- mutation-specific authorization and command allowlisting;
- durable original-state journaling before apply;
- independent post-apply verification;
- interruption/reboot recovery state;
- verified revert/rollback behavior;
- explicit recovery-required states when final machine state cannot be proven.

The current Phase 2 Named Pipe ACL must not be treated as sufficient mutation authorization.

## Diagnostics and disclosure data

Primary local diagnostics are documented in `docs/DIAGNOSTICS.md`. When attaching logs to a report, review and sanitize them for machine/user-specific information that is not necessary to reproduce the issue.

A future exported diagnostic bundle must apply explicit redaction before data leaves the local machine.

## Out of scope for security rewards

There is currently no bug bounty or paid vulnerability reward program.

Benchmark disagreement, unsupported hardware, or a tuning regression without a security boundary violation should be reported as a normal issue rather than a security vulnerability.

## Disclosure

Please allow reasonable time for triage, fix development, and release preparation before public disclosure of a valid security issue.

The project owner may publish a security advisory after remediation when appropriate.
