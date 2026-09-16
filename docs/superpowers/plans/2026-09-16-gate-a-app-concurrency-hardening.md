# Gate A App Concurrency Hardening Plan

> **Execution:** This plan is being executed on the owner's `main` checkout. Keep the change scoped to the Gate A and measurement-control contract.

**Goal:** Make the App's one-click GPU Gate A orchestration mutually exclusive with observation and baseline capture, including service refresh callbacks, so the workflow remains fully automated after the deliberate UAC consent.

**Scope:** Reuse the existing App control-state mechanism. Do not add a second benchmark path, weaken UAC, bypass Defender, or claim physical Gate A closure from source/tests alone.

## Tasks

- [x] Extend the existing Gate A UI contract test with source-level invariants for the Gate A busy state and capture guards; run the focused test and confirm it fails before implementation.
- [x] Add a dedicated `_gateAValidationRunning` state in the App, set and clear it in the Gate A handler's `try/finally`, and guard both capture entry points before any asynchronous service preparation.
- [x] Make all measurement-control and readiness-state recalculation paths honor the Gate A busy state, including scenario selection, readiness checkboxes, baseline availability, and service refresh success.
- [x] Run the focused critical tests, the full critical test project, the App Release build, and inspect the final diff for scope and contract drift.
- [ ] Report the exact code evidence and the remaining owner-local physical Gate A evidence separately; do not mark the physical gate closed without rerunning the current revision on hardware.
