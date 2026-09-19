# LatencyPilot audit-closure design — 2026-09-19

## Authority and goal

This design closes the owner-supplied 2026-09-19 audit against `main @ 9c6ca684bbd36d5a736a97e7ea54b2f1497fd19f`. The product remains measurement-first: no setting is kept merely because it is the best forced candidate. A successful run may legitimately keep the exact original state.

## Scope

Close F1–F11 and the audit's connected scope gaps needed for the first useful automatic workflow: GPU interrupt affinity, conservative MSI enablement, primary-input/xHCI selection, xHCI affinity mutation/recovery substrate, reboot-aware continuation state, retained-change restore, and before/after reporting. NIC/RSS, audio tuning, BIOS, power plans, HPET and language rewrites remain out of scope.

## Architecture decisions

1. **Benchmark HWND ownership.** The benchmark renderer/window is created, pumped, recreated and disposed on one owner thread. Pipe I/O may use async APIs internally but renderer-facing continuations return to the owner thread before touching Win32/D3D12 state. `BenchmarkWindow` fail-closes on cross-thread use.
2. **Deterministic ranking.** Practical tolerances are applied against a fixed best reference at each ranking stage, never through a pairwise fuzzy comparator. This makes ordering deterministic and transitive.
3. **Original is scored.** The exact original state is measured with the same scored workload/duration. A forced finalist is eligible for Keep only if its primary result is measurably better than Original outside repeatability/noise bounds and no hard guardrail regresses. No measurable difference is a successful RestoreOriginal result.
4. **Current-state candidate.** If a search candidate already matches the captured original stored policy, it is measured as a no-write activation owned by the session, not rejected or silently removed. It never receives rollback ownership because no mutation occurred.
5. **Mutation serialization.** All mutation/recovery/retained-restore entry points share a crash-released named OS mutex. Registry/PnP preflight through write/restart/verification is serialized across processes; SQLite CAS remains as a second layer.
6. **Collector semantics.** PresentMon is optional cross-check evidence during screening. Expected absence/startup failure becomes an explicit unavailable snapshot; cancellation, trust/integrity failures and final required ETW proof remain hard failures.
7. **Placement proof.** Runtime placement has `Verified`, `Contradicted`, and `Unknown`. Screening may rank Unknown with context; Contradicted is rejected. Final Keep requires Verified with attributed ISR samples.
8. **Candidate coverage.** Single-group x64 search covers every eligible physical-core representative. Any explicit caller cap must be reported as a cap; the default automatic path is full search.
9. **Primary input and USB routing.** Automatic USB selection requires an explicit/observed primary Raw Input device. Unresolved or ambiguous primary identity is NotReady. Composite USB ancestry is walked until an exact driver-key/host-controller port match is found; another mouse is never substituted silently.
10. **Capture quality and USB attribution.** USB headroom selection requires a substantially complete capture with observed interrupt activity. Existing module-name evidence is labelled driver-wide; it is never accepted as controller-specific proof when multiple controllers share that service.
11. **MSI.** Read and snapshot `MSISupported`; already-enabled is a successful no-op. Only supported PCI/display/xHCI targets with authoritative stored state may be changed. The change is journaled, device-reactivated, verified and rollback-capable. `MessageNumberLimit` and priority are observed/preserved, not automatically tuned.
12. **xHCI mutation.** Reuse the same generic interrupt-affinity storage semantics and transaction rules for the selected xHCI controller. Apply only after primary route and recommendation are unambiguous. GPU and xHCI retained changes are independently journaled and unwound newest-first.
13. **Reboot continuation.** A mutation that Windows reports as system-restart-required remains non-terminal with an explicit resume marker. On resume, actual stored/device state is re-read before continuing or rolling back; redirect/intent is never treated as proof.
14. **UX language.** User-facing flow is Optimize → diagnose → Original measurement → GPU → MSI → primary-input/xHCI → verify → result. `Gate A` remains developer terminology. `Restore original settings` replaces `Restore Windows Defaults` unless the captured baseline is demonstrably Windows default.

## Safety and evidence

CI proves software contracts only. Hardware claims require a physical Windows run of the exact revision. Public/user-reachable mutation stays fail-closed until the corresponding physical gate is recorded. The permanent test budget must not grow beyond the repository limit; new cases are folded into existing high-blast-radius contract methods and redundant methods are consolidated.

## Completion criteria

On supported hardware, a run can measure Original, evaluate all eligible GPU candidates, handle an already-active candidate, avoid Keep when improvement is not measurable, conservatively handle MSI, identify the intended input controller, apply/recover xHCI affinity, survive Stop/failure/reboot-required states, restore retained changes, and emit comparable before/after evidence. Unsupported/ambiguous systems finish safely without guessed writes.
