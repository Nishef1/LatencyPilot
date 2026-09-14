# Phase 3 Physical Validation Runbook

This runbook is the owner-local Gate A procedure for LatencyPilot's first reversible GPU interrupt-affinity experiment. It does **not** arm public mutation. Protocol v6 remains read-only throughout Gate A.

## Scope and safety boundary

Use only on the supported owner-controlled Windows 11 x64 machine. The validation harness lives at `tools/LatencyPilot.PhysicalValidation`; it is a developer/owner tool, not a product surface, installer payload, App feature, Named Pipe command or `run.ps1` dependency.

A registry write, stored-value equality or successful device refresh is not proof of effective interrupt placement. Keep these evidence levels separate:

```text
stored interrupt configuration
!= allocated resource state
!= runtime DPC/ISR behavior
```

Stop immediately on unknown/diverged state, changed driver assumptions, unexpected target identity, failed rollback verification or a recovery plan that requires manual intervention. Do not delete or edit the SQLite journal to make validation pass.

## Record before starting

Record all of the following in the physical validation evidence:

- exact clean source revision;
- green hosted Tests run for that exact revision;
- Windows edition/build;
- GPU name, driver version and exact device instance ID;
- chosen group-0 logical processor;
- original GPU affinity snapshot summary;
- every experiment ID and journal state transition;
- restart result and restart/reboot-required flags;
- runtime ISR placement evidence;
- rollback/recovery result;
- Service restart/reboot observations;
- final unresolved-journal count.

A physical validation claim without exact clean source provenance is invalid.

## 1. Prove the normal read-only product path first

From a normal, non-elevated terminal at the clean revision:

```powershell
.\run.ps1
```

Before any mutation experiment, confirm that App and Service build/install/launch successfully, the protected Service reaches `Running`, the App reports the expected source revision, protocol-v6 observation still works, evidence export still works and Service startup logs report mutation-journal readiness.

Do not continue if the normal product path is broken.

## 2. Read-only harness inspection

The following commands do not require the mutation acknowledgement:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

`inspect` must report journal readiness. On a clean starting state, unresolved count must be zero. `list-gpus` supplies the exact present display-adapter instance ID and driver identity used by the later steps.

If an unresolved experiment already exists, do not start another one. Inspect its stored-state relation and recovery disposition first.

## 3. Prepare one bounded experiment

Open an elevated interactive owner terminal. Choose one existing group-0 logical processor in the current topology; v1 supports only a single processor group and an x64 KAFFINITY-representable CPU (0-63).

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- prepare-gpu-affinity --device '<exact-instance-id>' --processor <cpu> --confirm-physical-mutation
```

Record the experiment GUID, target, candidate mask, original snapshot summary and journal state. `prepare-gpu-affinity` must journal the exact original state but must not write the candidate device policy.

## 4. Prove unresolved-state survival and classification

Before applying the candidate, restart the LatencyPilot Service using the normal protected Service lifecycle. Re-run `inspect` and confirm that the same experiment survives and is classified from current machine state rather than stale intent.

For a still-original prepared entry, the relation/recovery disposition must be consistent with a no-write prepared experiment. Any unknown/diverged classification is a stop condition.

This step proves durable journal/recovery inspection across Service restart; it does not prove mutation activation.

## 5. Apply and record exact-target restart behavior

From the elevated owner terminal:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- apply --experiment <guid> --confirm-physical-mutation
```

Record:

- journal state/revision;
- stored-state verification result;
- whether the exact target restarted in place;
- `system-restart-required`;
- device-started/problem status;
- devnode/install flags.

If the command leaves the experiment in `RecoveryRequired`, returns a non-success result, or Windows reports reboot/restart required, do not describe the candidate as active. Preserve the journal and follow the recovery/reboot path. Never bypass the unresolved state.

## 6. Prove runtime ISR placement separately

After candidate storage/restart is in a trusted active state, capture runtime ETW evidence using the existing LatencyPilot observation path under a representative repeatable workload. Reconcile GPU-driver ISR execution with the candidate processor using the existing runtime-placement analysis.

Record target-CPU ISR count, off-target ISR count and unresolved attribution. Do not invent a pass threshold from one machine. The requirement here is evidence that stored configuration and observed runtime placement are independently visible and can be reconciled.

A successful registry write or device restart alone does not satisfy this step.

## 7. Exact rollback

From the elevated owner terminal:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- rollback --experiment <guid> --confirm-physical-mutation
```

Record the terminal state and exact-target restart evidence. The experiment counts as reverted only if the original stored state is verified and activation/final-state evidence is trusted. If rollback cannot be proven, the journal must remain unresolved.

Re-run `inspect` and verify that the experiment is terminal and there is no unresolved residue.

## 8. Forced-failure and recovery exercise

Gate A also requires one deliberate failure/recovery exercise on supported hardware. The exercise must fail through a controlled, documented condition; do not corrupt arbitrary registry state or introduce an unrelated system tweak.

After the failure, restart the Service if the scenario requires it and run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect
```

If the recovery plan is automatic and rollback-safe, execute:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- recover --experiment <guid> --confirm-physical-mutation
```

Unknown/diverged/driver-changed state is a manual-intervention result, not permission for a blind write. Recovery in journal v1 is rollback-biased and never resumes forward automatically.

## 9. Reboot-required path

If physical testing naturally reaches a reboot-required state, preserve the unresolved journal, reboot normally, confirm the Service starts, run `inspect`, re-read actual stored state and complete only the recovery action justified by the fresh assessment.

Do not force a reboot solely to manufacture a passing result if the supported hardware does not naturally require one. In that case record the reboot-required path as not exercised on this hardware; do not mark that physical obligation complete.

## 10. Final closure checks for Gate A

Gate A passes only when the recorded evidence proves all of these on the exact clean revision:

1. normal App + Service build/install/launch is healthy;
2. journal startup readiness is healthy;
3. unresolved state survives Service restart and is correctly reclassified;
4. exact-target restart/reboot-required behavior is understood on the tested hardware;
5. candidate apply, stored verification, restart, runtime ISR observation and exact rollback are proven;
6. forced failure/recovery is proven;
7. exact original state is restored and `inspect` reports zero unresolved experiments.

Passing Gate A authorizes the next **source-development** stage: Gate B, mutation-specific typed/allowlisted IPC. It does not make mutation user reachable.

The later sequence is:

```text
Gate A — internal physical substrate proof
-> Gate B — typed mutation IPC + mutation-specific authorization
-> Gate C — physical end-to-end App/client -> Service mutation proof
-> Gate D — user-facing product arming
```

Only after Gate C may `MutationAvailable` become true for supported hardware and the one-click GPU workflow become user reachable.
