# Phase 3 Physical Validation and Arming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the missing owner-only physical-validation/recovery path required to prove the existing GPU affinity mutation substrate before mutation IPC or UI arming.

**Architecture:** Keep the product protocol v6 read-only. Correct the known pre-write-abort journal semantic, factor actual-state recovery assessment so startup inspection and explicit recovery share one source of truth, add a rollback-biased recovery executor, and expose those internals only to a non-shipping owner validation console through a friend assembly. Reconcile docs around Gate A internal physical validation -> Gate B typed mutation IPC -> Gate C IPC physical validation -> Gate D product arming.

**Tech Stack:** C# 14, .NET 10 LTS, Windows 11 x64, WinUI 3, Windows Service, SQLite/Microsoft.Data.Sqlite, SetupAPI/ConfigMgr, ETW, MSTest/Microsoft.Testing.Platform, PowerShell/GitHub Actions for temporary smoke evidence.

**Spec:** `docs/superpowers/specs/2026-09-14-phase3-physical-validation-and-arming-design.md`

## Global Constraints

- Work directly on `main`; do not create a branch.
- Public product protocol remains v6 and observation-only in this tranche.
- `ServiceBoundary.MutationAvailable` remains `false`.
- Validation harness is owner-only and must never be added to installer/release payloads or `run.ps1`.
- No generic registry, PowerShell, arbitrary process, plugin/DLL or run-as-SYSTEM primitive.
- GPU affinity mutation v1 remains present display adapters, one processor group, documented affinity-policy values only.
- Permanent automated tests remain 9/10; temporary tests/smokes must be removed before final state.
- Hosted CI returns to the normal test-only workflow after temporary evidence.
- No physical mutation is claimed from hosted CI.

---

### Task 1: Correct the pre-write-abort journal semantic

**Files:**
- Modify: `src/LatencyPilot.Persistence/MutationJournal.cs`
- Modify: `src/LatencyPilot.Service/GpuInterruptAffinityMutationTransaction.cs`
- Temporary test: `tests/LatencyPilot.CriticalTests/Phase3PreWriteAbortTemporaryTests.cs`

**Interfaces:**
- Consumes: `MutationJournalStateMachine.CanTransition`, `MutationJournal.Transition`
- Produces: legal `Applying -> AbortedBeforeApply` transition used only by `GpuInterruptAffinityMutationTransaction.MarkPreWriteAbort`

- [ ] **Step 1: Add a temporary failing test**

```csharp
[TestClass]
public sealed class Phase3PreWriteAbortTemporaryTests
{
    [TestMethod]
    public void Applying_can_terminalize_as_aborted_before_owned_write()
    {
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(
            MutationJournalState.Applying,
            MutationJournalState.AbortedBeforeApply));
    }
}
```

- [ ] **Step 2: Run the test-only workflow and verify RED**

Expected failure: `CanTransition(Applying, AbortedBeforeApply)` is currently false.

- [ ] **Step 3: Add the narrow state transition**

In `MutationJournalStateMachine.CanTransition` add:

```csharp
(MutationJournalState.Applying, MutationJournalState.AbortedBeforeApply) => true,
```

- [ ] **Step 4: Change the proven pre-write abort path**

`MarkPreWriteAbort` must transition directly from `Applying` to `AbortedBeforeApply` with the bounded `prewrite-abort:` reason. A failed journal transition still leaves the unresolved `Applying` record for startup recovery; do not swallow it or claim success.

- [ ] **Step 5: Run the temporary test + permanent suite and verify GREEN**

Expected: temporary test passes; permanent total remains unchanged because the test will be deleted before final state.

- [ ] **Step 6: Commit the semantic fix**

Commit message: `fix(mutation): terminalize proven pre-write aborts`

---

### Task 2: Share recovery assessment and add rollback-biased execution

**Files:**
- Create: `src/LatencyPilot.Service/MutationRecoveryAssessment.cs`
- Create: `src/LatencyPilot.Service/MutationRecoveryExecutor.cs`
- Modify: `src/LatencyPilot.Service/MutationRecoveryInspector.cs`
- Reuse: `src/LatencyPilot.Service/MutationRecoveryPlanner.cs`
- Reuse: `src/LatencyPilot.Service/GpuInterruptAffinityMutationTransaction.cs`

**Interfaces:**
- Produces: `MutationRecoveryAssessment.Inspect(MutationJournalEntry)` returning the existing `MutationRecoveryInspection`
- Produces: `MutationRecoveryExecutor.Execute(Guid experimentId)` returning `MutationRecoveryExecutionResult`
- Startup `MutationRecoveryInspector` becomes logging/orchestration around the shared assessment rather than owning classification logic.

- [ ] **Step 1: Move actual-state classification into a side-effect-bounded assessment helper**

Use the existing codec/store/comparer/planner logic exactly once. Unsupported kind, unreadable state, driver change, diverged state and unknown state must still resolve to a manual/fail-closed plan.

Signature:

```csharp
internal static class MutationRecoveryAssessment
{
    internal static MutationRecoveryInspection Inspect(MutationJournalEntry entry);
}
```

- [ ] **Step 2: Refactor startup inspector to consume the helper**

`MutationRecoveryInspector` continues to log unresolved entries, actual-state/read failures, relation and selected recovery action. It must not execute recovery during normal Service startup.

- [ ] **Step 3: Add explicit recovery execution**

```csharp
internal sealed record MutationRecoveryExecutionResult(
    MutationRecoveryInspection Inspection,
    MutationJournalEntry JournalEntry,
    GpuDeviceRestartResult? Restart,
    bool OriginalStateRestored);

internal sealed class MutationRecoveryExecutor
{
    internal MutationRecoveryExecutor(MutationJournal journal);
    internal MutationRecoveryExecutionResult Execute(Guid experimentId);
}
```

Execution rules:

```text
None                       -> return without write
AbortPreparedWithoutApply  -> Prepared -> AbortedBeforeApply, no device write/restart
FinalizeVerifiedRollback   -> normalize unresolved state toward Reverting, exact-target restart/verification, Reverted only when original is active/trusted
RestoreOriginalState       -> normalize Applying to RecoveryRequired when needed, then reuse GpuInterruptAffinityMutationTransaction.RollbackAndActivate
ManualInterventionRequired -> throw/refuse; preserve unresolved journal
```

Always call `MutationRecoveryAssessment.Inspect` immediately before selecting the action.

- [ ] **Step 4: Keep crash ambiguity conservative**

An unresolved `Applying` entry without a proven pre-write-abort terminal record must never be assumed to have written or not written. If actual state is original, `FinalizeVerifiedRollback` may verify/restart and terminalize; if candidate, restore exact original; if diverged/unknown, refuse.

- [ ] **Step 5: Temporary compile-check Service**

Temporarily add a Service build step to hosted Tests only long enough to prove compilation, as previously done; remove it after evidence. Do not make hosted Service compilation a permanent CI contract.

- [ ] **Step 6: Commit the recovery source**

Commit message: `feat(mutation): execute rollback-biased recovery plans`

---

### Task 3: Add the permanent owner-only physical-validation harness

**Files:**
- Modify: `src/LatencyPilot.Service/LatencyPilot.Service.csproj`
- Create: `tools/LatencyPilot.PhysicalValidation/LatencyPilot.PhysicalValidation.csproj`
- Create: `tools/LatencyPilot.PhysicalValidation/Program.cs`
- Temporary smoke: `eng/Phase3PhysicalValidation.Smoke.ps1`
- Temporary CI edit: `.github/workflows/ci.yml`

**Interfaces:**
- Service friend assembly: `LatencyPilot.PhysicalValidation`
- Tool commands: `inspect`, `list-gpus`, `prepare-gpu-affinity`, `apply`, `rollback`, `recover`

- [ ] **Step 1: Add a temporary RED CLI smoke before creating the tool**

The smoke invokes:

```powershell
dotnet run --project tools/LatencyPilot.PhysicalValidation/LatencyPilot.PhysicalValidation.csproj -- inspect
```

and requires output containing `Mutation journal` plus a successful exit. It also invokes a mutation command without `--confirm-physical-mutation` and requires a non-zero exit before any transaction can run.

Run it in a temporary hosted workflow step and confirm RED because the project does not exist yet.

- [ ] **Step 2: Add narrow friend access**

In Service csproj:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="LatencyPilot.PhysicalValidation" />
</ItemGroup>
```

Do not expose Service mutation types as public product APIs.

- [ ] **Step 3: Create the tool project**

Project properties:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<OutputType>Exe</OutputType>
```

References: Service, Persistence, Platform.Windows, Core. No extra CLI framework dependency.

- [ ] **Step 4: Implement strict argument parsing and guard order**

Before calling transaction/recovery for `prepare-gpu-affinity`, `apply`, `rollback`, or `recover`:

```text
Windows check
-> administrator check
-> required exact arguments
-> explicit --confirm-physical-mutation for commands that may alter/restart hardware
-> journal initialization
-> transaction/recovery invocation
```

`prepare-gpu-affinity` is owner-state-changing because it creates an unresolved journal entry, so require elevation and explicit confirmation even though it does not write device policy.

- [ ] **Step 5: Implement read-only commands**

`inspect` initializes/reads the journal and prints each unresolved entry plus fresh recovery assessment.

`list-gpus` enumerates present SetupAPI display-class devices and prints exact instance ID, display name and driver version. It does not mutate.

- [ ] **Step 6: Implement mutation-validation commands**

`prepare-gpu-affinity` constructs a `GpuInterruptAffinityCandidate` from current topology and a group-0 logical CPU, then calls transaction `Prepare`.

`apply` calls `ApplyAndActivate`.

`rollback` calls `RollbackAndActivate`.

`recover` calls `MutationRecoveryExecutor.Execute`.

Output must include experiment ID/state and restart flags when present. Errors return non-zero and preserve the journal.

- [ ] **Step 7: Run temporary CLI smoke and Service/tool compile GREEN**

Hosted smoke proves command guards/read-only inspect and compilation only. It is not hardware validation.

- [ ] **Step 8: Remove temporary smoke and temporary CI compile steps**

Normal `.github/workflows/ci.yml` must return to test-only permanent-suite execution. Delete `eng/Phase3PhysicalValidation.Smoke.ps1`.

- [ ] **Step 9: Commit the owner-only tool**

Commit message: `feat(validation): add owner-only Phase 3 harness`

---

### Task 4: Add the physical Gate A runbook

**Files:**
- Create: `docs/PHASE3_PHYSICAL_VALIDATION.md`

**Interfaces:**
- Consumes: owner-only harness commands
- Produces: exact physical evidence sequence required before protocol v7 work

- [ ] **Step 1: Document non-mutation startup prerequisites**

Start from a clean exact `main`, green Tests run, then run `./run.ps1` from a normal terminal and prove App/Service build/install/launch + journal readiness.

- [ ] **Step 2: Document controlled recovery-classification exercise**

Use the harness only from an elevated owner terminal. Record exact experiment IDs and do not proceed when `inspect` reports unknown/diverged/manual state.

- [ ] **Step 3: Document candidate apply/runtime/rollback sequence**

Explicitly capture original state, apply one bounded candidate, record restart/reboot-required result, collect runtime GPU ISR placement evidence, rollback, and prove exact original state.

- [ ] **Step 4: Document forced-failure and Service restart/reboot cases**

A failed step must leave an unresolved journal or verified terminal rollback; never manually delete the database to make the gate pass.

- [ ] **Step 5: Define Gate A pass record**

Require exact clean source revision, Tests run, machine/GPU/driver/instance ID, candidate processor, journal transitions, restart evidence, runtime evidence, rollback/recovery result and final zero unresolved entries.

- [ ] **Step 6: Commit the runbook**

Commit message: `docs: add Phase 3 physical validation runbook`

---

### Task 5: Reconcile authoritative docs around the four gates

**Files:**
- Modify: `ROADMAP.md`
- Modify: `SYSTEM_DESIGN.md`
- Modify: `docs/OPTIMIZER_TARGET_GRAPH.md`
- Modify: `PROJECT_STATUS.md`

**Interfaces:**
- Produces canonical sequence: Gate A internal physical -> Gate B IPC implementation -> Gate C IPC physical -> Gate D UI/product arming

- [ ] **Step 1: Remove the sequencing contradiction**

Do not say "only after all physical mutation validation design IPC" in a way that implies IPC is absent from the final safety gate. State that Gate A validates the internal substrate while v6 remains read-only; Gate B adds mutation-specific typed/allowlisted IPC; Gate C physically validates that real boundary; Gate D arms product mutation.

- [ ] **Step 2: Keep Phase 2 closure independent**

Controlled-idle/accessibility/device-inspector/read-only closure remains open and does not get falsely closed by Phase 3 source progress.

- [ ] **Step 3: Update current execution ladder**

After this source tranche, the next action is owner-local Gate A. Do not list protocol v7, candidate screening or UI arming as current work until Gate A evidence exists.

- [ ] **Step 4: Record verification evidence precisely**

Record exact commits/runs for temporary red/green evidence and final normal test-only CI. State explicitly that no physical mutation occurred in hosted CI.

- [ ] **Step 5: Commit doc reconciliation**

Commit message: `docs: reconcile Phase 3 arming gates`

---

### Task 6: Final verification and cleanup

**Files:**
- Verify: `.github/workflows/ci.yml`
- Verify: `tests/LatencyPilot.CriticalTests/*`
- Verify: `src/LatencyPilot.Protocol/*`
- Verify: `src/LatencyPilot.Service/ServiceBoundary.cs`
- Verify: installer/release scripts do not include `tools/LatencyPilot.PhysicalValidation`

**Interfaces:** final source state only; no new feature surface.

- [ ] **Step 1: Confirm temporary tests/smokes are absent**

Permanent suite count remains 9/10.

- [ ] **Step 2: Confirm public protocol remains v6/read-only**

`ObservationCommand` remains `GetStatus`, `CaptureKernelLatency`; `MutationAvailable=false`.

- [ ] **Step 3: Confirm validation harness is non-shipping**

No App, installer, release package or `run.ps1` reference may launch/package it.

- [ ] **Step 4: Run final normal hosted Tests on exact HEAD**

Require completed success before claiming source tranche verification.

- [ ] **Step 5: Re-read the spec and plan against actual HEAD**

Any unverified physical requirement remains explicitly open.

- [ ] **Step 6: Report progress using the five-part repository contract**

State Completed now, Evidence, Still open, Next stage, After that.
