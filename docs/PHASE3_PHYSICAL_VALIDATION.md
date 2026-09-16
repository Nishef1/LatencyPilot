# Phase 3 Physical Validation Runbook

This is the owner-local **Gate A** procedure for the benchmark-backed GPU interrupt-affinity experiment. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false` throughout Gate A.

Gate A now has two intentionally separate owner surfaces:

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= active all-core candidate screening + SMT refinement + balanced confirmation

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
- SMT sibling-refinement candidates when present;
- original/control and candidate trial identities;
- D3D12 timestamp evidence and raw PresentMon evidence identity;
- ETW capture integrity, GPU-driver identity and target/off-target/unresolved ISR counts;
- every experiment ID and journal transition;
- exact-target restart/reboot-required result;
- rollback/recovery result;
- final recommendation and exact final state;
- final unresolved-journal count;
- progress/taskbar/keyboard/Stop safely observations.

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
raw PresentMon capture is available/clean enough for the method
benchmark exits normally and writes its artifact
```

Do not interpret the smoke trial as a candidate winner. It only proves the workload/evidence path can run.

## 5. Run the complete benchmark-backed Gate A search

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
→ original control trials
→ every eligible physical core screened within v1 bound (max 16)
→ winning physical core SMT-sibling refinement when applicable
→ fixed ABBA + BAAB finalist confirmation
→ verified KeepCandidate OR exact RestoreOriginal
→ report + terminal progress state
```

On the Ryzen 7 5700X system, expect eight physical-core screening candidates before refinement, subject only to explicit CPU-set eligibility exclusions. CPU0 is eligible and must not be hard-banned.

Passive processor pressure is ordering/context only. It does not pre-select the winner.

## 6. Inspect live progress behavior

The main App must **minimize**, not disappear. It must remain recoverable through normal taskbar behavior. A compact progress window must remain accessible and show real session state rather than elapsed-time interpolation:

```text
current CPU / physical core
candidate X / Y
phase and pass/run
real completed/planned percentage
frame p99 and 1% low when available
GPU ISR placement state
last completed candidate verdict
elapsed and estimated remaining
Stop safely
```

For accessibility, verify visible phase/status meaning is also exposed through UI Automation and is not communicated by color alone. Check keyboard reachability and focus behavior at the actual rendered size.

## 7. Verify candidate mutation boundaries

For at least one screened candidate, retain evidence that proves the complete ownership sequence:

```text
exact original snapshot retained
→ candidate apply is journal-owned
→ exact stored candidate verified
→ exact-target activation/restart result recorded
→ benchmark + ETW + raw PresentMon trial captured
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

- same/equivalent finalist within method thresholds; or
- explicit `NoMeasurableDifference` / restored original; or
- explicit `Inconclusive` when control/validity evidence does not support a winner.

Unacceptable outcome: arbitrary different winners with no uncertainty/validity explanation.

The original Windows state is a real control and is the preferred outcome when no candidate establishes a safe measurable improvement.

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
final stored GPU affinity state
final runtime/device identity
final unresolved journal count
report path(s)
benchmark artifact path(s)
exact source SHA
exact green Tests run
```

`unresolved=0` is mandatory for Gate A closure.

## 12. Gate A closure criteria

Gate A passes only when physical evidence on one exact clean revision proves all of the following:

1. normal App + protected Service build/install/launch works;
2. journal starts clean with zero unresolved experiments;
3. the built-in D3D12 benchmark runs without mutation, uses multiple physical cores and freezes one workload for comparison;
4. the full automatic search screens the bounded eligible physical-core set rather than a passive rank-1/four-core shortcut;
5. SMT sibling refinement behaves as planned when applicable;
6. exact-target apply reaches verified stored state and direct runtime GPU ISR placement under the requested processor rules;
7. screening candidates rollback exactly before the next candidate;
8. balanced finalist confirmation either justifies a verified Keep or restores original;
9. **Stop safely** restores/verifies original and terminalizes with no unresolved mutation;
10. one supported failure/recovery path is physically proven;
11. repeated whole searches are reproducible/equivalent or explicitly inconclusive;
12. progress window, taskbar recovery, narrow render, keyboard and accessibility behavior are physically sane;
13. final journal reports zero unresolved experiments and final machine state is understood/verified.

Passing Gate A authorizes **Gate B source development only**. It does not arm public mutation.

```text
Gate A — internal physical substrate + benchmark-backed search proof
→ Gate B — typed mutation IPC + mutation-specific authorization
→ Gate C — physical App/client → Service mutation proof
→ Gate D — user-facing product arming
```
