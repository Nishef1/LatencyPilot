# Security Policy

LatencyPilot interacts with privileged Windows configuration and therefore treats security and recovery defects as first-class issues.

## Supported versions

LatencyPilot is currently pre-alpha and has no stable supported release line yet.

Once releases begin, this file will list supported versions and security-fix policy.

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
- a reliable denial of service that leaves the system in an unsafe or unrecoverable tuning state.

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

## Security architecture principles

LatencyPilot follows these baseline rules:

1. The WPF application is non-elevated during normal use.
2. Privileged work is isolated in a narrowly scoped Windows Service.
3. The service exposes typed allow-listed IPC commands rather than arbitrary execution primitives.
4. The service re-validates all privileged requests.
5. Original state is journaled before a supported mutation.
6. Post-apply state is independently verified.
7. Recovery state is durable across app/service restart and reboot.
8. Diagnostic bundles are local-first and must avoid unnecessary sensitive information.
9. A failed verification is not treated as success.
10. Security features must not be disabled merely to chase benchmark gains.

## Out of scope for security rewards

There is currently no bug bounty or paid vulnerability reward program.

Benchmark disagreement, unsupported hardware, or a tuning regression without a security boundary violation should be reported as a normal issue rather than a security vulnerability.

## Disclosure

Please allow reasonable time for triage, fix development, and release preparation before public disclosure of a valid security issue.

The project owner may publish a security advisory after remediation when appropriate.
