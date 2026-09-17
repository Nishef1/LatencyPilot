# Phase 3 Physical Validation Runbook

This is the owner-local **Gate A** procedure for the benchmark-backed GPU interrupt-affinity experiment. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false` throughout Gate A.

Gate A now has two intentionally separate owner surfaces:

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= active all-core candidate screening + ranked finalist re-screen + SMT refinement + balanced confirmation

LatencyPilot.PhysicalValidation
= lower-level substrate/preflight/recovery diagnostics
= useful for inspecting exact state and supported failure/recovery
= not the automatic candidate-selection authority
```

`Auto-optimize GPU` remains blocked until Gate B typed mutation IPC, Gate C physical App→Service proof and Gate D arming.

## Scope and evidence boundary

Use only on the supported owner-controlled Windows 11 x64 machine. The development Gate A UI and validation tools are owner/developer surfaces, not product mutation IPC.

Keep these evidence layers separate:

```text
stored interrupt-affinity policy
!= ConfigMgr allocated interrupt-resource evidence
!= runtime ETW ISR execution evidence
```

A registry write/restart is not activation proof. ConfigMgr allocated resources are independent provenance only. The previously observed RTX 3070 tuple (`irq=4294967270`, `group=1`, `affinity=0`, `flags=0x0002`) is explicitly **not** accepted as effective placement and must never be coerced into a plausible CPU mask/group/MSI claim.

A candidate trial is decision-grade only when the exact stored candidate is verified around the capture and attributable GPU-driver ISR execution is confined to the requested logical processor. Unresolved attribution is recorded separately and does not count as success.

Stop on unknown/diverged state, changed driver assumptions, unexpected target identity, failed runtime placement, failed exact rollback or recovery requiring manual intervention. Never edit/delete the SQLite journal to make validation pass.

## Required provenance

Record for the final Gate A evidence set:

- exact clean source revision;
- successful hosted **Tests** run for that exact revision;
- Windows edition/build;
- GPU name, driver version and exact PnP instance ID;
- CPU topology and CPU-set availability evidence;
- benchmark method/schema/version, deterministic seed and frozen worker/workload mapping;
- all physical-core screening candidates and deterministic order;
- ranked finalist re-screen candidates;
- SMT sibling-refinement candidates when present;
- original/control and candidate trial identities;
- D3D12 timestamp evidence and raw PresentMon evidence identity;
- exact pinned PresentMon console version/hash/path used for collection;
- ETW capture integrity, GPU-driver identity and target/off-target/unresolved ISR counts;
- every experiment ID and journal transition;
- exact-target restart/reboot-required result;
- rollback/recovery result;
- final recommendation and exact final state;
- final unresolved-journal count;
- progress/taskbar/keyboard/Stop safely observations.

The saved `latencypilot-gpu-auto-affinity-report-v1` is expected to carry source/benchmark provenance, exact original and final stored affinity state, apply/rollback/keep mutation audit entries and terminal recovery status. The report supplements rather than replaces the independent journal and runtime ISR evidence.

No physical validation claim is valid without exact clean source provenance.

## 1. Freeze the exact revision

From a normal non-elevated terminal in the repository:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
```

Requirements:

```text
branch = main
working tree = clean
HEAD = exact 40-hex revision
```

Confirm the hosted **Tests** workflow is green for that exact SHA. If HEAD changes after this point, restart the exact-revision checks; a green run for an ancestor is not sufficient.

## 2. Validate the normal development App/Service path

Run the established development launcher:

```powershell
.\run.ps1
```

Before mutation confirm:

- App opens normally;
- protected Service reaches `Running`;
- protocol-v6 observation still works;
- evidence export/verification still works;
- startup reports no unexplained unresolved mutation state;
- the development-only **Run GPU Gate A** action is visible only from a source checkout.

Do not continue if the normal path is broken.

## 3. Read-only preflight

The lower-level physical harness remains useful for exact-state inspection:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

A clean start requires `unresolved=0`. Record GPU/driver/device identity and raw allocated-resource output. Raw ConfigMgr fields stay provenance; they are never repaired or guessed.

For v1 GPU auto-affinity, multi-group mutation remains fail-closed. The supported owner system should therefore report one processor group before the automatic Gate A search proceeds.

## 4. Exercise the D3D12 benchmark without mutation

Before the full mutation search, prove that the workload generator itself works on the exact revision.

Example for the Ryzen 7 5700X owner machine:

```powershell
$session = [guid]::NewGuid().ToString('D')
$out = Join-Path $PWD 'artifacts\owner-local\gpu-benchmark-smoke.json'

dotnet run --project .\src\LatencyPilot.GpuBenchmark\LatencyPilot.GpuBenchmark.csproj --configuration Release -- `
  --session-id $session `
  --width 1280 `
  --height 720 `
  --worker-count 8 `
  --seed 1337 `
  --duration-seconds 15 `
  --output $out
```

This command is **read-only** with respect to device interrupt affinity. It does not require elevation.

Confirm from the benchmark output/artifact and Task Manager/independent observation where practical:

```text
multiple physical-core workers are materially active
CPU0 is not the only materially active core
calibration freezes once before measured work
D3D12 GPU timestamp values are finite/stable
benchmark exits normally and writes its artifact
```

The standalone smoke intentionally does **not** collect the Gate A raw PresentMon or kernel ETW evidence. Those independent measurement streams are started and synchronized by the controlled Gate A backend during the full search in sections 5–7.

Do not interpret the smoke trial as a candidate winner. It only proves the deterministic workload/artifact path can run.

## 5. Run the complete benchmark-backed Gate A search

There is **no separate PresentMon Service/API installation prerequisite** for Gate A. LatencyPilot uses the pinned standalone `PresentMon-2.5.1-x64.exe` collector. It first accepts a packaged copy at `ThirdParty\PresentMon`, otherwise it resolves a LatencyPilot-controlled per-user cache and may provision the exact official release asset. The executable must match the pinned SHA-256 before it is used. A missing download path, failed integrity check or unusable collector fails closed; it is never replaced by an arbitrary installed PresentMon DLL/service.

In the development App click:

```text
Run GPU Gate A
```

Expected architecture:

```text
normal-user App
→ normal-user LatencyPilot.GpuBenchmark
→ one explicit UAC consent for the owner Gate A helper
→ exact original-state capture
→ 5 s original warm-up (not scored)
→ two original reference controls
→ for every eligible physical core: apply/restart → 5 s warm-up → two scored runs → exact rollback
→ rank valid/repeatable cores by median frame-p99
→ re-screen the best up-to-three cores with fresh warm-up + scored runs
→ winning physical core SMT-sibling refinement when applicable
→ fixed ABBA + BAAB finalist confirmation, with a fresh 5 s warm-up after every state transition
→ verified KeepCandidate OR exact RestoreOriginal
→ report + terminal progress state
```

On the Ryzen 7 5700X system, expect eight physical-core screening candidates before finalist re-screen/refinement, subject only to explicit CPU-set eligibility exclusions. CPU0 is eligible and must not be hard-banned.

Passive processor pressure is ordering/context only. It does not pre-select the winner.

### Ranking semantics

The product question is **which valid CPU is the best GPU interrupt target**, not whether every candidate individually clears a fixed improvement threshold against the Windows default. The original/default state remains an important reference and confirmation side, but it is not the winner gate for a forced-CPU auto-affinity search.

A candidate is rankable only when its evidence is decision-grade, ISR attribution/placement is valid and its repeated frame-p99 measurements remain within the repeatability bound. Rankable candidates are ordered by lower median run-level frame-p99 with deterministic tie-breaks. The best up-to-three candidates are then re-measured from fresh post-restart warm-ups before one physical-core finalist is chosen. `Inconclusive` evidence is never ranked.

If no candidate survives validity/repeatability, restore exact original state rather than guessing. Near-equal but valid candidates may still produce a deterministic best observed CPU; the report retains the Original comparison and raw deltas so the result is auditable.

## 6. Inspect live progress behavior

The main App must **minimize**, not disappear. It must remain recoverable through normal taskbar behavior. A compact progress window must remain accessible and show real session state rather than elapsed-time interpolation:

```text
current CPU / physical core
candidate X / Y
phase and scored/warm-up pass
real completed/planned percentage
frame p99 and 1% low when available
GPU ISR placement state
last completed candidate verdict
elapsed and estimated remaining
Stop safely
```

Warm-up trials must be visibly marked as **not scored**.

For accessibility, verify visible phase/status meaning is also exposed through UI Automation and is not communicated by color alone. Check keyboard reachability and focus behavior at the actual rendered size.

## 7. Verify candidate mutation boundaries

For at least one screened candidate, retain evidence that proves the complete ownership sequence:

```text
exact original snapshot retained
→ candidate apply is journal-owned
→ exact stored candidate verified
→ exact-target activation/restart result recorded
→ post-restart non-scored warm-up
→ benchmark + ETW + raw standalone-PresentMon trial captured
→ exact stored candidate verified after capture
→ >=1 attributed GPU ISR on requested logical processor
→ 0 attributed GPU ISR on off-target logical processors
→ unresolved ISR attribution recorded separately
→ exact rollback to original before next screening candidate
```

ConfigMgr allocated-resource evidence may be recorded when readable but cannot substitute for runtime ISR placement.

If Windows naturally reports reboot-required state, preserve journal ownership, reboot normally, re-read actual state and continue only through the recovery action justified by fresh state. Do not force a reboot merely to manufacture coverage.

## 8. Exercise **Stop safely**

During a separate Gate A run, press **Stop safely** while work is in progress.

Required behavior:

```text
no future trial starts after the cancellation boundary
an already-owned candidate is not silently kept
progress remains in stopping/restoring state while rollback is owned
exact original state is verified before terminal safe completion
journal unresolved count returns to 0
```

Closing the progress window before terminal state must request the same safe stop rather than abandoning the helper.

If terminal status says final state could not be verified, stop validation and use the supported recovery workflow; do not start another experiment.

## 9. Repeat the complete search

Run the full automatic Gate A search at least twice on the same exact revision under comparable conditions.

Acceptable outcome:

- same/equivalent finalist after the fresh top-candidate re-screen and confirmation; or
- explicit restored original when no candidate remains decision-grade/repeatable; or
- explicit `Inconclusive` when identity/integrity/placement evidence cannot support ranking.

Unacceptable outcome: arbitrary different winners with no uncertainty/validity explanation.

The original Windows state is the recovery/reference state. It is restored when the experiment cannot establish a valid and repeatable ranked finalist; it is not used as a fixed minimum-improvement threshold that prevents choosing the best tested CPU.

## 10. Supported failure/recovery exercise

Use only the existing supported validation/recovery mechanisms. Do not corrupt unrelated registry/device state to manufacture a failure.

The lower-level physical harness may be used to inspect/recover a journal-owned experiment:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect
```

If fresh recovery assessment explicitly says rollback is automatic/safe, use the supported recovery command documented by the harness. Unknown/diverged/driver-changed state is manual intervention, never permission for a blind write.

The exercise passes only when interruption/failure remains journal-owned and the final exact state is verified.

## 11. Final reconciliation

After the final run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Record:

```text
final recommendation
final selected processor
final stored GPU affinity state
final runtime/device identity
final unresolved journal count
PresentMon console version/path/hash provenance
report path(s)
benchmark artifact path(s)
exact source SHA
exact green Tests run
```

Inspect the saved auto-affinity report and require `finalStateVerified=true`, `recoveryStatus=clean-zero-unresolved`, populated `originalStoredState` / `finalStoredState`, benchmark `provenance`, and mutation-audit entries consistent with the run. A successful Keep must have non-null `finalProcessor`, exact candidate state in `finalStoredState`, consistent ISR attribution and confirmed target-only placement in the decision-grade candidate evidence. `unresolved=0` from the journal inspector is still mandatory; the JSON report cannot override a conflicting live/journal state.

## 12. Gate A closure criteria

Gate A passes only when physical evidence on one exact clean revision proves all of the following:

1. normal App + protected Service build/install/launch works;
2. journal starts clean with zero unresolved experiments;
3. the built-in D3D12 benchmark runs without mutation, uses multiple physical cores and freezes one workload for comparison;
4. the full automatic search screens the bounded eligible physical-core set, performs a non-scored post-transition warm-up, ranks all valid/repeatable candidates, and freshly re-screens the best up-to-three candidates before selecting a physical-core finalist;
5. SMT sibling refinement behaves as planned when applicable;
6. exact-target apply reaches verified stored state and direct runtime GPU ISR placement under the requested processor rules;
7. screening/finalist/refinement candidates rollback exactly before the next candidate;
8. balanced finalist confirmation remains decision-grade/repeatable and either verifies the ranked candidate Keep or restores exact original state;
9. the standalone pinned PresentMon collector produces synchronized raw-frame evidence without requiring an installed PresentMon Service/API;
10. **Stop safely** restores/verifies original and terminalizes with no unresolved mutation;
11. one supported failure/recovery path is physically proven;
12. repeated whole searches are reproducible/equivalent or explicitly inconclusive;
13. progress window, taskbar recovery, narrow render, keyboard and accessibility behavior are physically sane;
14. final journal reports zero unresolved experiments and final machine state is understood/verified.

Passing Gate A authorizes **Gate B source development only**. It does not arm public mutation.

```text
Gate A — internal physical substrate + benchmark-backed search proof
→ Gate B — typed mutation IPC + mutation-specific authorization
→ Gate C — physical App/client → Service mutation proof
→ Gate D — user-facing product arming
```
