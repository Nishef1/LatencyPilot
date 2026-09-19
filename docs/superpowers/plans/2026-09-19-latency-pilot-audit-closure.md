# LatencyPilot audit-closure implementation plan

**Spec:** `docs/superpowers/specs/2026-09-19-latency-pilot-audit-closure-design.md`

**Starting revision:** `9c6ca684bbd36d5a736a97e7ea54b2f1497fd19f`

## Global constraints

- Work directly on `main` per repository owner policy.
- Use RED→GREEN contract coverage in the existing CriticalTests suite; consolidate rather than exceed the permanent test budget.
- Do not claim physical GPU/MSI/xHCI performance from CI.
- Keep public mutation fail-closed until exact-revision physical validation exists.
- Preserve exact prior registry values and driver identity for rollback.

## Task 1 — benchmark ownership + deterministic GPU decision

- Add regression assertions for fixed-reference ranking, Unknown-vs-Contradicted placement, Original-vs-finalist decision, full >16-core candidate coverage, and current-state candidate activation.
- Make benchmark renderer/window operations owner-thread-only and remove async continuation ownership drift.
- Replace pairwise fuzzy comparator with fixed-reference staged ranking.
- Score Original under the same workload/duration and require measurable finalist improvement before Keep.
- Represent no-write current candidate explicitly.

Verification: `dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release`

## Task 2 — mutation/recovery serialization + restore

- Add one cross-process crash-released mutation lock shared by prepare/apply/measurement state changes/keep/rollback/recovery/global restore.
- Connect retained-change restore through an explicit internal command path and keep naming truthful: restore original settings.
- Verify concurrency state-machine behavior in existing safety/restore tests.

Verification: CriticalTests Release.

## Task 3 — collector/proof semantics + schedule

- Make PresentMon expected startup unavailability soft for screening without swallowing cancellation/security/integrity failures.
- Stop/flush the collector at benchmark completion instead of waiting for oversized timed slack where supported.
- Use tri-state runtime placement; final verification still requires Verified.

Verification: CriticalTests Release.

## Task 4 — USB identity/evidence fixes

- Require explicit/observed primary Raw Input identity; never substitute a secondary resolved mouse.
- Walk composite USB ancestors for exact driver-key + host-controller port correlation.
- Reject empty/too-short ETW capture for USB CPU selection.
- Label current USBXHCI evidence driver-wide and refuse controller-specific readiness when ownership is ambiguous.

Verification: CriticalTests Release.

## Task 5 — conservative MSI + xHCI reversible mutation

- Add generic device interrupt configuration snapshot/store for MSI + affinity while preserving unknown values exactly.
- MSI enablement: no-op if already enabled; reject unsupported/ambiguous targets; never auto-tune priority or MessageNumberLimit.
- Add xHCI affinity transaction using the selected controller and the same journal/restart/rollback contract.
- Extend retained restore and recovery inspection for the new mutation kinds.
- Add reboot-required resume state/inspection rather than pretending activation succeeded.

Verification: CriticalTests Release; physical proof remains a separate gate.

## Task 6 — workflow/docs/status reconciliation

- Wire the internal Optimize workflow to Original → GPU → MSI → USB/xHCI → final verification/report while keeping public arming gated by physical evidence.
- Update ADR/ROADMAP/SYSTEM_DESIGN/BENCHMARK_METHODOLOGY/PROJECT_STATUS/README to the implemented semantics and current CI evidence.
- Remove stale `Restore Windows Defaults` wording where behavior is snapshot restore.
- Reconcile permanent test count to the authorized maximum.

Verification: CriticalTests Release + hosted CI on exact `main` head + final compare review.

## Review focus

- Cross-process race between Apply and Recovery/Restore.
- Any path that can Keep without scored Original or final verified ISR placement.
- MSI/xHCI writes on unsupported or ambiguous devices.
- Rollback after reboot-required or driver-version change.
- Composite USB devices and multiple mice/controllers.
- Any pairwise fuzzy sort reintroduced into final ranking.
