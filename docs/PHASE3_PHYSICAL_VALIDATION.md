# Phase 3 Physical Validation Runbook

This is the owner-local **GPU Gate A** procedure for the paired local-control GPU search in ADR 0007. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false` during this gate.

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= bounded Original qualification
= direct Original-before → Candidate → Original-after local pairs
= physical-core representative screen + bounded sibling refinement
= up to 3 finalists, 3 independent 30 s pairs each
= final ETW runtime ISR-placement proof before Keep

LatencyPilot.PhysicalValidation
= lower-level exact-state/preflight/recovery diagnostics
= not the candidate-selection authority
```

## Scope and evidence boundary

Use only on the supported owner-controlled Windows 11 x64 machine.

Keep these evidence layers separate:

```text
stored interrupt-affinity policy
!= ConfigMgr allocated interrupt resources
!= runtime ETW ISR execution
```

A registry write/restart is not activation proof. ConfigMgr resources are provenance only. Never coerce ambiguous IRQ/group/affinity fields into a plausible placement claim.

Keep these measurement layers separate:

```text
raw scored observation
!= paired derived effect
!= persisted decision/finalist authority
!= verified terminal machine state
```

During **screening**, valid controlled benchmark evidence may remain rankable if optional PresentMon or kernel ETW is unavailable; that absence must be explicit. If healthy ETW proves off-target placement, the candidate is invalid.

During **final Keep verification**, missing/unhealthy ETW is not acceptable. Keep requires clean ETW, attributable GPU ISR samples and target-only placement on the selected logical processor.

Stop on unknown/diverged stored state, target-identity change, invalid/mismatched benchmark evidence, failed exact rollback or recovery requiring manual intervention. Never edit/delete the SQLite journal to make validation pass.

## Required provenance

Record:

- exact clean `main` revision;
- successful hosted Tests run for that exact revision;
- Windows build;
- GPU name/driver/PnP identity;
- CPU topology, CPU-set eligibility and Stage-A physical-core representatives;
- benchmark method/schema/seed/frozen worker/workload mapping;
- Original qualification observations and accepted cluster/noise;
- every local pair's processor, stage, attempt and three capture ids;
- raw Original-before / Candidate / Original-after 1%-low/AVG/p99/0.1%-low values;
- paired effects, control movement, drift budget and verdict;
- Stage-B sibling hypothesis selection;
- Stage-C finalist selection;
- all finalist pair numbers, median effects, decision floors and verdicts;
- PresentMon version/path/hash and any bounded collector diagnostic state;
- ETW integrity and ISR target/off-target/unresolved counts where available;
- every mutation-journal apply/rollback/keep transition;
- exact original/final stored states;
- final recommendation/processor and recovery status;
- final unresolved journal count;
- generated evidence ZIP path/status;
- rendered Overview/progress/taskbar/keyboard/Stop-safely observations.

## 1. Freeze the exact revision

From a normal non-elevated repository terminal:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
```

Require:

```text
branch = main
working tree = clean
HEAD = exact 40-hex revision
hosted Tests = green for this exact HEAD
```

If HEAD changes, restart the authoritative physical evidence set. An older physical run is historical evidence only.

## 2. Validate the normal App/Service path

Run:

```powershell
.\run.ps1
```

Before mutation confirm:

- the App launches non-elevated;
- observation Service connectivity works;
- startup reports no unexplained unresolved mutation state;
- evidence/session paths are writable;
- the Gate A developer controls are present and understandable.

Do not continue if the normal path is broken.

## 3. Read-only preflight

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Require `unresolved=0`. Record device/driver/topology/CPU-set evidence. Current GPU mutation remains fail-closed for unsupported processor-group/topology cases.

## 4. Optional no-mutation methodology check

Before spending time on a full search, the developer UI may run:

```text
Original only · no system changes
```

This path must perform no GPU affinity write or device restart. It is useful for checking current Original variability and benchmark health, but it **cannot** prove candidate benefit, restart stability or Gate A closure.

If the Original-only diagnostic is obviously unstable, fix the environment/methodology issue before interpreting a full search. Do not widen thresholds or lengthen sleeps merely to force a pass.

## 5. Run the complete paired-v2 GPU Gate A search

There is no separate PresentMon Service installation prerequisite. LatencyPilot uses the pinned standalone PresentMon console executable and its bounded verification/provisioning path.

In the development App choose the full GPU Gate A search.

Expected flow:

```text
normal-user benchmark + one UAC owner helper
→ capture exact Original stored state
→ 5 s non-scored Original warm-up
→ 10 s scored Original observations
→ establish 3-run 1%-low cluster:
     ±3% preferred
     bounded recovery up to ±6% after observations 4/5
→ no valid cluster after 5:
     verify exact Original
     stop before candidate mutation
→ capture fresh 10 s Original control O0
→ Stage A:
     one eligible logical-CPU representative per physical core
     representative selection uses current eligibility/pressure evidence
→ for each candidate:
     OriginalBefore already available
     apply/restart/verify candidate state
     5 s non-scored candidate warm-up
     10 s scored candidate
     exact rollback + Original verification
     5 s non-scored Original warm-up
     10 s scored OriginalAfter
     compute pair movement/effects/verdict
→ unstable pair:
     one fresh retry only
→ two consecutive candidates that exhaust retry:
     stop safely, verify/retain Original
→ Stage B:
     best 2 physical-core hypotheses
     + third only inside 1 percentage-point margin
     screen still-untested siblings on those cores
→ Stage C:
     best 2 logical CPUs
     + third only inside 1 percentage-point margin
     hard cap 3 finalists
→ finalist confirmation:
     fresh 30 s Original control
     3 rounds
     deterministic finalist shuffle per round
     3 independent valid 30 s local pairs per accepted finalist
→ finalist decision floor = max(1%, median local pair-control movement)
→ reject finalist on insufficient positive primary evidence or material primary/AVG/p99/interrupt-tail regression
→ if remaining improvement-capable finalists are within 1 percentage point:
     record Practical tie
     use passive ordering only to choose an operational target
→ apply selected operational target once more
→ final non-scored warm-up
→ clean kernel-ETW runtime-placement verification
→ Keep only with attributable target-only GPU ISR placement and verified terminal state
   otherwise exact RestoreOriginal
→ validate report identity/source eligibility
→ package shareable evidence ZIP when possible
→ render authority-selected result in Overview
```

### Expected Ryzen 7 5700X search shape

On an 8-core / 16-thread Ryzen 7 5700X with all logical processors eligible:

- Stage A should normally screen **8 physical-core representatives**, not blindly all 16 logical CPUs;
- Stage B should add only the untested siblings on the bounded selected physical-core hypotheses;
- Stage C must still cap finalists at 3.

Do not hard-code those counts. Actual topology/eligibility evidence owns the expected set.

## 6. Verify local pair math and stability semantics

For every valid pair, independently check that persisted evidence can reconstruct:

```text
reference = sqrt(OriginalBefore × OriginalAfter)
```

For higher-is-better metrics:

```text
effect = Candidate / reference - 1
```

For lower-is-better frame p99:

```text
effect = reference / Candidate - 1
```

Check that:

- raw trial values were not rewritten;
- pair capture ids resolve to the expected three observations;
- control movement uses the two adjacent Original controls;
- pair drift budget is `clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)`;
- a pair above budget is not ranked;
- attempt 2 is the only statistical retry;
- an exhausted pair becomes Inconclusive;
- two consecutive exhausted candidates trigger a safe stop rather than a manufactured ranking.

A result that simply chooses the fastest raw candidate without valid local controls is a failure.

## 7. Verify Stage A/B/C selection authority

Check that the persisted execution/report evidence agrees with the algorithm:

### Stage A

- one eligible representative per physical core;
- no even/odd SMT assumption;
- CPU0 not globally banned;
- current CPU-set eligibility respected;
- passive pressure only used for representative/fallback ordering, not as a performance score.

### Stage B

- best two physical-core hypotheses selected from valid paired evidence;
- third only when within the 1 percentage-point practical-equivalence margin of second place;
- no more than three physical cores refined;
- only untested eligible siblings added.

### Stage C

- best two logical CPUs advance;
- third only when within the same practical-equivalence margin of second place;
- no more than three finalists.

## 8. Verify finalist authority

For each accepted finalist require:

- exactly three valid 30-second pair numbers;
- median paired 1%-low/AVG/frame-p99 effects persisted;
- diagnostic 0.1%-low effect persisted where available;
- `DecisionFloor = max(1%, median finalist pair-control movement)`;
- at least two of three primary pair effects positive before an ImprovementCapable verdict;
- median primary effect above decision floor;
- no material primary regression beyond the floor;
- no material AVG/frame-p99 regression;
- no material supported interrupt-tail regression.

If a finalist cannot produce three valid pairs under the bounded retry policy, it must be Inconclusive and cannot be kept.

If improvement-capable finalists are within one percentage point, the report/result UI must say **Practical tie** and must not claim that the operationally selected CPU proved faster.

## 9. Inspect final runtime placement and terminal state

A `KeepCandidate` outcome requires all of the following on the final verification capture:

```text
stored candidate state verified before/after
ETW integrity clean
ETW lost events = 0
attributable GPU ISR samples > 0
target processor = selected processor
resolved off-target ISR = 0
terminal stored state verified
unresolved journal ownership = 0
```

If any required final-placement condition is absent, the run must restore exact Original.

For `RestoreOriginal`, verify the final stored state matches the exact captured Original state and `unresolved=0`.

## 10. Inspect live progress and final result UX

While running, the main App should minimize rather than disappear. The compact progress surface should expose meaningful phase/progress state and **Stop safely**.

After a valid terminal report, the main window should return to Overview and render the in-product result rather than automatically opening raw JSON.

Inspect:

```text
terminal outcome
Evidence eligible / Development evidence
best measured candidate when relevant, clearly marked not kept
practical-tie state when applicable
direct Original-before / Candidate / Original-after pair evidence
paired effect
control movement + drift budget
pair attempt/verdict
candidate/finalist rank authority
Why this decision rows
runtime placement/final state
evidence ZIP status
actions: Open ZIP / Copy ZIP path / Open session folder / Open raw report
```

For RestoreOriginal outcomes, any positive candidate measurement is diagnostic/comparison-only. The UI must not re-rank shuffled execution data or turn a positive effect into a Keep claim.

## 11. Real Windows visual/accessibility inspection

CI compilation is not visual evidence. Inspect the actual result/progress surfaces on the exact physical revision in:

- Light;
- Dark;
- High Contrast;
- increased text scale;
- narrow and wide window sizes;
- keyboard-only navigation;
- UI Automation/Narrator as applicable.

Require:

- no clipping/overlap;
- readable chart labels/tooltips;
- correct focus order;
- meaningful accessible names/help text;
- decision state not encoded by color alone;
- Stop safely and evidence actions keyboard reachable.

Record screenshots/UIA evidence in the closure bundle.

## 12. Exercise Stop safely

During a separate full-search session on the same exact revision:

1. wait until a candidate mutation is owned;
2. invoke **Stop safely**;
3. require the helper/session to rollback exact Original;
4. verify terminal stored state;
5. require `unresolved=0`;
6. confirm the UI reports stopped/restored truthfully.

Do not reuse the interrupted session as winner evidence.

## 13. Exercise one supported failure/recovery path

Use one existing supported physical-validation/recovery scenario that can be induced without inventing unsupported mutation. Examples include a bounded cancellation/failure path already represented by the physical-validation tooling.

Require:

- journal ownership is visible;
- recovery re-reads actual machine state;
- exact Original is restored or the tool fails closed with explicit manual-intervention state;
- `unresolved=0` before declaring the exercise closed.

Never manufacture a green result by deleting the journal.

## 14. Repeat the whole search

After returning to a clean exact Original state, run the full paired-v2 search a second time on the same source revision and comparable conditions.

Compare:

- Original qualification/noise;
- Stage-A valid hypotheses;
- Stage-B sibling refinements;
- finalist set;
- finalist median paired effects/decision floors;
- final Keep/Restore/practical-tie outcome;
- runtime-placement evidence.

The second run need not produce identical decimals. It must either be practically reproducible under the method's equivalence semantics or explicitly expose instability/inconclusive evidence rather than manufacturing consistency.

## 15. Gate A closure criteria

Physical GPU Gate A closes only when one exact clean green revision has recorded evidence that:

- the normal App/helper path works;
- Original qualification is bounded and fail-closed before mutation;
- every eligible physical core receives the correct representative screen;
- Stage-B/Stage-C selection matches the persisted paired evidence;
- local pair math/control movement/retry semantics reconstruct correctly;
- finalists meet the three-valid-pair contract;
- practical ties are represented honestly;
- exact rollback works between candidates;
- final Keep, when present, has clean attributable target-only GPU ISR proof;
- RestoreOriginal, when present, restores exact Original;
- terminal journal ownership is zero;
- Stop safely restores exact Original;
- one supported failure/recovery exercise closes cleanly;
- a second whole search is reproducible or explicitly inconclusive;
- real result/progress UI passes the recorded render/accessibility inspection.

Only after those conditions pass may the project proceed to arming the typed allowlisted product mutation boundary.

## 16. What this gate does not prove

GPU Gate A does not by itself prove:

- xHCI mutation/runtime verification;
- combined GPU+xHCI reboot/resume behavior;
- final one-button before/after product workflow;
- signed package/install/upgrade/uninstall closure;
- performance for arbitrary games/workloads beyond the controlled LatencyPilot method.

Those remain later v1 gates.