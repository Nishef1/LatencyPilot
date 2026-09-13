## Summary

Describe the problem and the change. Keep the scope focused.

## Why this belongs in LatencyPilot

Explain how this supports measurement, attribution, safety, rollback, diagnostics, or the supported product scope.

## Change type

- [ ] Bug fix
- [ ] Benchmark/statistics change
- [ ] Windows/platform integration
- [ ] System mutation/tuning behavior
- [ ] Recovery/rollback
- [ ] UI/UX
- [ ] Tests/fixtures
- [ ] Documentation
- [ ] Build/CI/tooling

## Architecture impact

- [ ] No architecture contract changes
- [ ] `SYSTEM_DESIGN.md` updated
- [ ] ADR added/updated

Affected current boundaries:

<!-- Core / Benchmarking / Protocol / Platform.Windows / Service / App / CriticalTests -->

If this introduces the Phase 3 persistence boundary, explain the concrete SQLite schema/journal/recovery contract; do not add an empty placeholder project.

## Evidence

For changes involving Windows behavior or tuning, link authoritative documentation and describe the supported mechanism.

For performance/latency claims, include benchmark evidence or explain why the PR is observational/infrastructure-only.

## Mutation safety

If this PR changes system state, confirm all applicable items:

- [ ] Applicability is detected explicitly
- [ ] Original state is durably journaled before mutation
- [ ] Candidate is validated
- [ ] Apply is narrowly scoped
- [ ] Applied state is independently verified
- [ ] Revert is implemented and verified
- [ ] Interrupted-run/reboot recovery is covered
- [ ] Target metrics are defined
- [ ] Guardrail metrics are defined
- [ ] Mutation-specific authorization is defined

Permanent-test coverage must protect the highest-blast-radius safety contract while respecting the repository-wide maximum of 10. Prefer extending/replacing an existing durable test instead of adding one test per failure path.

If not applicable, explain why:

## Benchmark integrity

If benchmark logic changes:

- [ ] Baseline noise handling remains valid
- [ ] Tail metrics remain available
- [ ] Sample adequacy is checked
- [ ] Drift/invalid experiment behavior is covered
- [ ] A composite score does not hide raw trade-offs
- [ ] Any permanent-test change respects the hard cap

## Testing

Permanent automated tests currently have a hard repository-wide maximum of 10. Describe the existing test updated/replaced, any justified new permanent slot, or why no permanent-test change is needed. Temporary investigative tests should not remain unless promoted to a justified permanent contract.

```text
Tests / contract evidence:
```

Hardware validation, if applicable:

```text
Hardware / Windows build / driver / workload:
```

Hosted GitHub Actions is intentionally test-only; do not present it as App/Service build, package, GUI-smoke, release, or hardware-performance evidence.

## Security / privilege impact

- [ ] No new privileged capability
- [ ] Privileged change remains behind typed service IPC
- [ ] No arbitrary command/PowerShell/registry primitive was introduced
- [ ] Logging/diagnostics do not add unnecessary sensitive data

Explain any privilege-boundary change:

## Compatibility / migration

Describe protocol, future persistence-schema, migration, reboot, or backward-compatibility impact.

## Contribution agreement

- [ ] I have read and agree to [`CLA.md`](../CLA.md), and I have the right to submit this contribution under those terms.
- [ ] I have disclosed any third-party code/assets and their licenses.

## Checklist

- [ ] I read `AGENTS.md`
- [ ] I read the relevant parts of `SYSTEM_DESIGN.md`
- [ ] I did not weaken safety checks to make tests pass
- [ ] Documentation reflects changed behavior
- [ ] The PR contains no unrelated formatting/refactor churn
- [ ] No dead/speculative project or placeholder abstraction was added
