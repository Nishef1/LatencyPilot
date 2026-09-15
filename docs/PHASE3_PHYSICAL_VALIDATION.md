# Phase 3 Physical Validation Runbook

This runbook is the owner-local Gate A procedure for LatencyPilot's first reversible GPU interrupt-affinity experiment. It does **not** arm public mutation. Protocol v6 remains read-only throughout Gate A.

## Scope and safety boundary

Use only on the supported owner-controlled Windows 11 x64 machine. The validation harness lives at `tools/LatencyPilot.PhysicalValidation`; it is a developer/owner tool, not a product surface, installer payload, App feature, Named Pipe command or `run.ps1` dependency.

Keep these evidence levels separate:

```text
stored interrupt-affinity policy
!= exact-target PnP allocated interrupt resources
!= runtime ETW DPC/ISR observation
```

The allocated resource layer is important: Windows Configuration Manager exposes the **allocated configuration** currently assigned to a specific devnode, including interrupt group/affinity data. Gate A therefore does not treat a registry write or a device refresh alone as activation proof.

Current owner-local stop condition (2026-09-15): on clean `7601d19`, the RTX 3070 read-only inventory returned `group=1`, `affinity=0`, and an IRQ value of `4294967270`. The descriptor's meaning as effective placement has not been established. Independently reconcile the native resource format and the actual processor topology before proceeding to device writes; do not coerce these fields into a plausible affinity or treat successful enumeration as activation proof. Microsoft's [IRQ_DES_64 contract](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/ns-cfgmgr32-irq_des_64) and [interrupt resource guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/using-interrupt-resource-descriptors) are semantic references, not physical validation of this returned data.

Runtime ETW attribution is also kept conservative. On some graphics stacks, hardware ISR work can resolve to a graphics-kernel module such as `dxgkrnl.sys` rather than the display miniport service module. Absence of service-module ISR samples is recorded as a limitation; LatencyPilot must not invent device ownership from that absence. Exact-target allocated affinity and clean ETW observation remain independently visible.

Stop immediately on unknown/diverged state, changed driver assumptions, unexpected target identity, failed allocated-affinity verification, failed rollback verification or a recovery plan that requires manual intervention. Do not delete or edit the SQLite journal to make validation pass.

## Record before starting

Record all of the following in the physical validation evidence:

- exact clean source revision;
- green hosted Tests run for that exact revision;
- Windows edition/build;
- GPU name, driver version and exact device instance ID;
- baseline evidence file/source revision used for candidate ranking;
- ranked candidate set and chosen group-0 logical processor;
- original GPU affinity snapshot summary;
- every experiment ID and journal state transition;
- restart result and restart/reboot-required flags;
- exact-target allocated IRQ group/affinity before/after where available;
- ETW capture integrity and runtime ISR/module evidence;
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

`inspect` must report journal readiness. On a clean starting state, unresolved count must be zero. `list-gpus` supplies the exact present display-adapter instance ID, driver identity and current allocated interrupt-resource evidence used by later steps.

If an unresolved experiment already exists, do not start another one. Inspect its stored-state relation and recovery disposition first.

## 3. Derive the bounded candidate set from valid evidence

Do not guess an apparently idle CPU from one screenshot or hard-exclude CPU 0. Use the valid Real-world five-window baseline plus **fresh current topology/CPU-set metadata**:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- plan-gpu-affinity --evidence '<path-to-LatencyPilot-baseline.json>'
```

This command is read-only. It rejects evidence that does not match the current evidence schema/protocol/baseline method, is not a valid `RealWorld` decision baseline, no longer matches the current processor-topology shape, contains duplicate/missing processor evidence, or has per-processor counts inconsistent with its baseline windows.

The planner then reuses LatencyPilot's bounded candidate policy: measured mean per-window DPC+ISR share, one logical sibling per physical core, current CPU-set availability when readable, hybrid efficiency-class representation, one processor group and at most four default candidates.

Record the evidence source revision and the full ranked set. Choose **one** candidate from that set for the first physical experiment. Candidate ranking is screening guidance, not proof that a processor will improve latency.

## 4. Prepare one bounded experiment

Open an elevated interactive owner terminal. Use the exact display-adapter instance ID from `list-gpus` and one processor from the ranked plan:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- prepare-gpu-affinity --device '<exact-instance-id>' --processor <cpu> --confirm-physical-mutation
```

Record the experiment GUID, target, candidate mask, original snapshot summary and journal state. `prepare-gpu-affinity` must journal the exact original state but must not write the candidate device policy.

## 5. Prove unresolved-state survival and classification

Before applying the candidate, restart the LatencyPilot Service using the normal protected Service lifecycle. Re-run `inspect` and confirm that the same experiment survives and is classified from current machine state rather than stale intent.

For a still-original prepared entry, the relation/recovery disposition must be consistent with a no-write prepared experiment. Any unknown/diverged classification is a stop condition.

This step proves durable journal/recovery inspection across Service restart; it does not prove mutation activation.

## 6. Apply and record exact-target restart behavior

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

## 7. Independently verify allocated affinity and runtime observation

After candidate storage/restart is in a trusted active state, put the same representative workload into its warmed/repeatable state. From an elevated owner terminal run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- verify-gpu-placement --experiment <guid>
```

This command is read-only with respect to device/journal state, but elevation is required for raw kernel ETW capture. It performs two independent checks:

1. re-enumerates the **exact journaled display-adapter devnode** and requires its currently allocated interrupt resources to be readable and confined to the candidate processor group/mask;
2. captures a 20-second raw kernel DPC/ISR observation and records ETW integrity plus best-effort service-module ISR correlation.

`allocated-affinity-match=false` is a hard stop: stored policy/restart did not produce independently observable allocated affinity for the exact target.

The ETW capture must also have clean capture integrity. If the display service module has resolvable ISR events, record target/off-target counts. If it has none but graphics ISR activity resolves elsewhere (for example through the graphics-kernel stack), record `service-module-correlation=not-observed` rather than inventing ownership. Gate A does **not** manufacture a service-module pass threshold from one machine.

The evidence claim for this step is therefore precise:

```text
exact target allocated affinity matches candidate
AND clean runtime ETW observation exists
AND any service-module ISR correlation is reported as observed/unavailable, never guessed
```

A successful registry write or device restart alone does not satisfy this step.

## 8. Exact rollback

From the elevated owner terminal:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- rollback --experiment <guid> --confirm-physical-mutation
```

Record the terminal state and exact-target restart evidence. The experiment counts as reverted only if the original stored state is verified and activation/final-state evidence is trusted. If rollback cannot be proven, the journal must remain unresolved.

Re-run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Verify that the experiment is terminal, there is no unresolved residue, and allocated-resource observations are recorded after restoration.

## 9. Forced-failure and recovery exercise

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

## 10. Reboot-required path

If physical testing naturally reaches a reboot-required state, preserve the unresolved journal, reboot normally, confirm the Service starts, run `inspect`, re-read actual stored/allocated state and complete only the recovery action justified by the fresh assessment.

Do not force a reboot solely to manufacture a passing result if the supported hardware does not naturally require one. In that case record the reboot-required path as not exercised on this hardware; do not mark that physical obligation complete.

## 11. Final closure checks for Gate A

Gate A passes only when the recorded evidence proves all of these on the exact clean revision:

1. normal App + Service build/install/launch is healthy;
2. journal startup readiness is healthy;
3. candidate selection came from a valid topology-matched Real-world baseline, not a guessed CPU;
4. unresolved state survives Service restart and is correctly reclassified;
5. exact-target restart/reboot-required behavior is understood on the tested hardware;
6. candidate apply and stored verification are followed by an exact-target **allocated interrupt affinity** match and a clean runtime ETW observation;
7. service-module ISR correlation is recorded when observable and its absence is not replaced with guessed ownership;
8. forced failure/recovery is proven;
9. exact original state is restored and `inspect` reports zero unresolved experiments.

Passing Gate A authorizes the next **source-development** stage: Gate B, mutation-specific typed/allowlisted IPC. It does not make mutation user reachable.

The later sequence remains:

```text
Gate A — internal physical substrate proof
-> Gate B — typed mutation IPC + mutation-specific authorization
-> Gate C — physical end-to-end App/client -> Service mutation proof
-> Gate D — user-facing product arming
```

Only after Gate C may `MutationAvailable` become true for supported hardware and the one-click GPU workflow become user reachable.
