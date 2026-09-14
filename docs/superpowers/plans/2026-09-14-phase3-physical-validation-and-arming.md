# Phase 3 Physical Validation and Arming Implementation Plan

> **For agentic workers:** This plan was executed with `superpowers:executing-plans`. The source tranche is complete; owner-local Gate A physical validation remains intentionally open.

**Status:** SOURCE TRANCHE COMPLETE — PHYSICAL GATE A NOT YET PASSED

**Goal:** Add the owner-only physical-validation/recovery path required to prove the GPU interrupt-affinity mutation substrate before mutation IPC or UI arming.

**Architecture:** Public product protocol stays v6/read-only. The Service owns the narrow journaled GPU-affinity mutation/recovery mechanism; `tools/LatencyPilot.PhysicalValidation` is an owner-only, non-shipping harness. Physical proof is staged as Gate A internal substrate → Gate B typed mutation IPC → Gate C physical IPC boundary → Gate D product arming.

**Tech Stack:** C# 14, .NET 10 LTS, Windows 11 x64, WinUI 3, Windows Service, SQLite/Microsoft.Data.Sqlite, SetupAPI/ConfigMgr, ETW, MSTest/Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-09-14-phase3-physical-validation-and-arming-design.md`

## Global constraints

- Work directly on `main`; no implementation branch.
- Protocol remains v6 and observation-only through Gate A.
- `ServiceBoundary.MutationAvailable` remains `false`.
- Validation harness is owner-only and absent from App launch, installer/release payloads and `run.ps1`.
- No arbitrary registry, shell, process, DLL/plugin or generic SYSTEM execution primitive.
- GPU affinity v1 stays limited to present display adapters, one processor group and documented affinity-policy values.
- Permanent automated tests remain **9/10**.
- Hosted CI remains test-only after temporary implementation evidence is removed.
- Hosted evidence never counts as physical mutation/restart/runtime proof.

---

### Task 1: Correct pre-write-abort semantics

- [x] Allow the narrow `Applying -> AbortedBeforeApply` transition for a proven no-owned-write abort.
- [x] Route the Service pre-write-abort path to that terminal state with bounded failure provenance.
- [x] Preserve unresolved `Applying` when the journal transition itself cannot be committed.
- [x] Prove the change RED→GREEN with a temporary test, then remove the temporary test.

Evidence: Tests run `34843429491` / #541 was RED for the missing transition; run `34843775832` / #543 was GREEN after the semantic fix.

### Task 2: Share recovery assessment and execute rollback-biased recovery

- [x] Add shared `MutationRecoveryAssessment` so startup inspection and explicit recovery use the same fresh actual-state classification.
- [x] Refactor startup inspection to remain read/diagnostic only; normal Service startup never performs recovery writes.
- [x] Add `MutationRecoveryExecutor` with `None`, `AbortPreparedWithoutApply`, `FinalizeVerifiedRollback`, `RestoreOriginalState` and fail-closed manual-intervention behavior.
- [x] Re-read actual state immediately before selecting an explicit recovery action.
- [x] Keep ambiguous unresolved `Applying` conservative rather than assuming whether a write happened.
- [x] Compile/prove through temporary hosted evidence, then remove the temporary Service test reference.

Evidence: run `34844029007` / #545 was RED because the executor did not exist; run `34844530778` / #549 was GREEN after implementation.

### Task 3: Add the permanent owner-only physical-validation harness

- [x] Add narrow Service friend access only for `LatencyPilot.PhysicalValidation`; mutation internals were not made public.
- [x] Add the non-shipping tool project under `tools/LatencyPilot.PhysicalValidation`.
- [x] Implement strict allowlisted parsing and elevation/confirmation guards for state-changing commands.
- [x] Implement `inspect`, `list-gpus`, `prepare-gpu-affinity`, `apply`, `rollback`, and `recover`.
- [x] Extend the harness with read-only `plan-gpu-affinity --evidence <baseline>` so candidate selection comes from a valid topology-matched Real-world baseline instead of a guessed CPU.
- [x] Extend the harness with read-only `verify-gpu-placement --experiment <guid>` to require exact-target allocated interrupt-affinity evidence plus clean ETW runtime observation; service-module ISR correlation remains supplementary and is never guessed.
- [x] Prove command guards/compilation with temporary smoke evidence, then remove the temporary smoke and CI step.

Evidence: run `34844759935` / #551 was RED because the project did not exist; run `34845073158` / #554 exposed a real nullable `ProblemCode` compile defect; the defect was fixed and later smoke/test evidence was GREEN.

Current harness command surface:

```text
inspect
list-gpus
plan-gpu-affinity --evidence <baseline.json>
prepare-gpu-affinity --device <exact-instance-id> --processor <group-0-cpu> --confirm-physical-mutation
apply --experiment <guid> --confirm-physical-mutation
verify-gpu-placement --experiment <guid>
rollback --experiment <guid> --confirm-physical-mutation
recover --experiment <guid> --confirm-physical-mutation
```

### Task 4: Add the Gate A physical-validation runbook

- [x] Add `docs/PHASE3_PHYSICAL_VALIDATION.md`.
- [x] Require a clean exact revision and green hosted Tests run before owner-local work.
- [x] Require normal App + protected Service build/install/launch and journal readiness before mutation.
- [x] Require baseline-derived candidate planning, unresolved-state survival across Service restart, exact-target restart/reboot handling, allocated-affinity verification, clean ETW runtime observation, exact rollback and forced-failure recovery.
- [x] Require final exact-original state and zero unresolved experiments.
- [x] Explicitly separate stored policy, allocated interrupt resources and runtime DPC/ISR evidence.

### Task 5: Reconcile authoritative docs around the four arming gates

- [x] `ROADMAP.md`, `SYSTEM_DESIGN.md`, `docs/OPTIMIZER_TARGET_GRAPH.md` and `PROJECT_STATUS.md` use the same Gate A → B → C → D sequence.
- [x] Phase 2 physical closure remains independent and open; Phase 3 source progress does not silently close it.
- [x] Gate B mutation IPC stays blocked until Gate A physical evidence exists.
- [x] Gate C remains the physical proof of the real App/client → Service mutation boundary.
- [x] Gate D remains the only point where supported user-facing mutation may be armed.
- [x] Hosted CI evidence is described as source/test evidence only, never hardware proof.

### Task 6: Final verification and cleanup

- [x] Temporary Phase 3 tests/smokes and temporary Service test reference are absent.
- [x] Permanent suite is back to **9/10**.
- [x] Normal `.github/workflows/ci.yml` is test-only.
- [x] `ProtocolVersion.Current` remains 6 and `ObservationCommand` remains only `GetStatus` + `CaptureKernelLatency`.
- [x] `ServiceBoundary.MutationAvailable=false`.
- [x] `run.ps1` does not invoke the physical-validation harness.
- [x] `scripts/Publish-Release.ps1` publishes only App/Service plus selected docs/scripts; the harness is not packaged.
- [x] Latest pre-finalization code HEAD `f8ae62b996818b4534a55882d19c646c812e242a` has successful test-only Tests run `34883664698` / #631 with **9 passed, 0 failed**.
- [x] Unverified physical requirements remain explicitly open rather than being marked complete.

## Completion boundary

The **repository/source tranche described by this plan is complete**. This does not mean Gate A or Phase 3 is physically closed.

Still open and intentionally hardware-gated:

```text
owner-local current-main App + Service build/install/launch
→ clean journal readiness
→ baseline-derived candidate plan
→ prepared-entry survival/reclassification across Service restart
→ one bounded apply + exact-target restart/reboot evidence
→ exact-target allocated interrupt-affinity match + clean ETW observation
→ exact rollback
→ controlled forced-failure/recovery
→ final zero unresolved experiments
```

Only after that record passes may Gate B protocol-v7 mutation IPC work begin.