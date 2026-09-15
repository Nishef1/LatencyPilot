# Phase 3 Physical Validation Runbook

This is the owner-local **Gate A** procedure for LatencyPilot’s first reversible GPU interrupt-affinity experiment. It does **not** arm public mutation. Protocol v6 remains read-only and `MutationAvailable=false` throughout Gate A.

## Scope and evidence boundary

Use only on the supported owner-controlled Windows 11 x64 machine. `tools/LatencyPilot.PhysicalValidation` is an owner/developer harness, not a product IPC/UI/installer surface.

Keep these evidence layers separate:

```text
stored interrupt-affinity policy
!= ConfigMgr allocated interrupt-resource evidence
!= runtime ETW ISR execution evidence
```

A registry write/restart is not activation proof. ConfigMgr allocated resources are useful independent evidence when their semantics are valid for the target, but the previously observed RTX 3070 tuple (`irq=4294967270`, `group=1`, `affinity=0`, `flags=0x0002`) is explicitly **not** accepted as an effective-placement result. Do not coerce it into a plausible CPU mask, group or MSI claim.

Current internal optimizer source additionally requires Candidate evidence from the same ETW measurement interval to contain attributable GPU-driver ISR execution on the candidate processor and no attributable GPU-driver ISR execution off target. Unresolved attribution does not count as successful placement.

Stop immediately on unknown/diverged state, changed driver assumptions, unexpected target identity, untrusted restart/reboot state, failed runtime-placement verification, failed exact rollback or a recovery plan requiring manual intervention. Never edit/delete the SQLite journal to make validation pass.

## Required provenance

Record:

- exact clean source revision;
- successful hosted **Tests** run for that exact revision;
- Windows edition/build;
- GPU name, driver version and exact PnP instance ID;
- baseline evidence path/hash/source revision used for candidate planning;
- ranked candidates and selected processor;
- exact original GPU affinity snapshot summary;
- every experiment ID and journal transition;
- exact-target restart/reboot-required result;
- ConfigMgr allocated-resource evidence as raw independent provenance when available;
- ETW capture integrity, GPU-driver identity and target/off-target/unresolved ISR counts;
- rollback/recovery result;
- Service restart/reboot observations;
- final unresolved-journal count.

No physical validation claim is valid without exact clean source provenance.

## 1. Validate the normal product path first

From a normal non-elevated terminal on current clean `main`:

```powershell
.\run.ps1
```

Before mutation confirm:

- App and protected Service build/install/launch successfully;
- Service reaches `Running`;
- App reports the expected clean source revision;
- protocol-v6 observation still works;
- evidence export/verification still works;
- Service startup reports mutation-journal readiness with no unexplained unresolved state.

Do not continue if the normal product path is broken.

## 2. Read-only preflight

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

A clean start requires `unresolved=0`. Record exact GPU/driver/device identity and raw allocated-resource output. Raw ConfigMgr fields are provenance unless their target semantics are independently trustworthy; they are never repaired or guessed.

## 3. Plan candidates from valid current evidence

Use the valid Real-world baseline from the **same exact clean source revision** plus fresh topology/CPU-set metadata. Resolve and record the full 40-hex commit first:

```powershell
$commit = (git rev-parse HEAD).Trim()
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- plan-gpu-affinity --evidence '<path-to-LatencyPilot-baseline.json>' --expected-commit $commit
```

The planner must reject missing, abbreviated or stale source provenance before candidate generation. `sourceRevisionId` in the evidence must be an exact full 40-hex match for `--expected-commit`; a valid baseline from a different revision is not Gate-A planning evidence for the current revision. It must also reject wrong-schema/wrong-protocol/invalid-baseline/changing-workload/topology-mismatched evidence. Candidate policy remains bounded: measured per-window DPC+ISR pressure, one logical sibling per physical core, current CPU-set availability when readable, hybrid efficiency-class representation, group-0 v1 boundary and at most four default candidates.

Record `expected-commit`, `evidence-revision`, `source-revision-match`, workload-stability method/status and the entire ranked set. Select one candidate for the first physical exercise. Ranking is screening guidance, not proof of improvement.

## 4. Prepare without writing

From an elevated owner terminal:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- prepare-gpu-affinity --device '<exact-instance-id>' --processor <cpu> --confirm-physical-mutation
```

Record experiment GUID, target, candidate mask, exact original snapshot and journal revision/state. Prepare must not change the device policy.

## 5. Prove durable unresolved-state classification

Before applying, restart the LatencyPilot Service through the normal protected Service lifecycle. Run `inspect` again.

The same prepared experiment must survive and be reclassified from the actual current machine state. Unknown/diverged classification is a stop condition.

This proves journal/recovery inspection across Service restart, not mutation activation.

## 6. Apply and record exact-target activation behavior

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- apply --experiment <guid> --confirm-physical-mutation
```

Record:

- journal state/revision;
- stored-state verification;
- exact target restart result;
- `system-restart-required`;
- device-start/problem status and install flags.

If the experiment becomes `RecoveryRequired`, returns non-success, or Windows requires a reboot/restart that has not been completed and revalidated, do not call the candidate active. Preserve the journal and execute only the justified recovery/reboot path.

## 7. Verify effective placement during a representative workload

Put the same representative workload into its warmed/repeatable state.

Use the current owner validation path to capture exact-target resource and runtime evidence. Where the harness exposes the dedicated command:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- verify-gpu-placement --experiment <guid>
```

Requirements:

1. exact stored candidate still matches immediately before/after the measurement;
2. ETW capture integrity is clean;
3. the target display-adapter driver/module identity is authoritative rather than guessed;
4. at least one **attributed GPU ISR** is observed on the candidate logical processor;
5. zero **attributed GPU ISR** is observed on off-target logical processors;
6. unresolved ISR attribution is recorded separately and does not satisfy rule 4;
7. ConfigMgr allocated resources are recorded independently when readable, but an ambiguous/invalid descriptor does not become a fabricated pass.

The claim is therefore:

```text
verified stored candidate
AND clean same-interval runtime ETW
AND attributable GPU ISR observed on candidate CPU
AND no attributable GPU ISR observed off target
```

A successful registry write, SetupAPI refresh or plausible allocated-resource tuple alone is insufficient.

## 8. Exact rollback

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- rollback --experiment <guid> --confirm-physical-mutation
```

Re-run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Rollback counts as complete only when exact original stored state and trusted final activation are verified and the journal reaches terminal `Reverted`. Failed/incomplete rollback remains unresolved.

## 9. Forced-failure recovery exercise

Gate A also requires one deliberate **supported** failure/recovery scenario. Do not corrupt unrelated registry/device state merely to manufacture a failure.

After the controlled failure, restart the Service if relevant and run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect
```

If fresh recovery assessment says rollback is automatic/safe:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- recover --experiment <guid> --confirm-physical-mutation
```

Unknown/diverged/driver-changed state is manual intervention, not permission for a blind write. Recovery remains rollback-biased.

## 10. Reboot-required path

If the hardware naturally reaches reboot-required state, preserve the unresolved journal, reboot normally, confirm Service startup, re-run `inspect`, re-read actual state and perform only the action justified by fresh recovery assessment.

Do not force a reboot solely to manufacture coverage. If the supported machine does not naturally exercise this path, record it as not physically exercised.

## 11. Gate A closure criteria

Gate A passes only when evidence on the exact clean revision proves:

1. normal App + Service build/install/launch;
2. journal startup readiness;
3. bounded candidate came from an exact-revision, topology-matched, stable Real-world baseline;
4. unresolved state survives Service restart and is correctly reclassified;
5. exact-target restart/reboot-required behavior is understood;
6. one candidate apply reaches verified stored state and effective runtime GPU ISR placement under the rules above;
7. exact original rollback is verified;
8. one controlled failure/recovery path is proven;
9. final actual state is exact original and `inspect` reports zero unresolved experiments.

Passing Gate A authorizes **Gate B source development only**. It does not arm public mutation.

```text
Gate A — internal physical substrate proof
→ Gate B — typed mutation IPC + mutation-specific authorization
→ Gate C — physical App/client → Service mutation proof
→ Gate D — user-facing product arming
```
