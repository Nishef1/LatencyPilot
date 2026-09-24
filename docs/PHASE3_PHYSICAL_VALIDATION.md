# Phase 3 Physical Validation Runbook

This is the owner-local **GPU Gate A** procedure for the adaptive uncertainty-aware GPU search in ADR 0009. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false`.

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= 3–5 Original observations for robust noise context
= direct Original-before → Candidate → Original-after local pairs
= Stage-A physical-core representatives + uncertainty-aware refinement across at most 4 physical-core hypotheses
= top-two-guaranteed shortlist, capped at 5 CPUs, with one additional 10 s recheck
= top 2 finalists, 2 shuffled 15 s pairs each, plus one third round only if uncertainty remains
= best-observed CPU + confidence
= separate Keep guardrails
= final ETW runtime ISR-placement proof before Keep
```

## Scope and evidence boundary

Use only on the supported owner-controlled Windows 11 x64 machine.

Keep these evidence layers separate:

```text
stored interrupt-affinity policy
!= ConfigMgr allocated interrupt resources
!= runtime ETW ISR execution
```

Keep these result layers separate:

```text
raw observation
!= paired effect
!= best-observed rank
!= selection confidence
!= Keep recommendation
!= terminal machine state
```

During screening, structurally valid benchmark evidence remains rankable even when run-to-run/local-control variability is high. Missing optional PresentMon or screening ETW remains explicit context. Healthy ETW that proves off-target placement invalidates that candidate.

During final Keep verification, clean ETW, attributable GPU ISR samples and target-only placement remain mandatory.

Stop on unknown/diverged stored state, target-identity change, invalid benchmark identity, non-finite required metrics, failed exact rollback, or recovery requiring manual intervention. Do not stop merely because otherwise valid FPS measurements are noisy.

## Required provenance

Record:

- exact clean `main` revision and hosted Tests result for that exact SHA;
- Windows build, GPU/driver/PnP identity and CPU topology/CPU-set eligibility;
- benchmark method/schema/seed/frozen worker/workload mapping;
- every scored Original observation plus median/MAD variability;
- every local pair's processor, stage, attempt and capture ids;
- raw Original-before / Candidate / Original-after values and paired effects;
- pair control movement/noise guide and retry outcome;
- Stage-A representatives, Stage-B refined cores and Stage-C finalists;
- finalist median effects, effect MAD, positive-pair count and raw median Original/Candidate values;
- best-observed CPU and `SelectionConfidence`;
- `RecommendedForKeep` and guardrail evidence;
- PresentMon/ETW availability and integrity;
- mutation-journal transitions;
- exact original/final stored states;
- final recommendation/processor and recovery status;
- generated evidence ZIP path/status;
- real rendered UI/accessibility observations.

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

If HEAD changes, restart the authoritative physical evidence set.

## 2. Validate the normal App/Service path

Run:

```powershell
.\run.ps1
```

Before mutation confirm:

- App launches non-elevated;
- observation Service connectivity works;
- startup reports no unexplained unresolved mutation state;
- evidence/session paths are writable;
- Gate A developer controls are present.

## 3. Read-only preflight

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Require `unresolved=0`. Record device/driver/topology/CPU-set evidence.

## 4. Optional Original-only diagnostic

The developer UI may run:

```text
Original only · no system changes
```

This path performs no GPU affinity write or restart. It is useful context for current variability and graphics-hook warnings, but it does not gate the full v3 search solely because the environment is noisy.

A structurally broken diagnostic still needs investigation before interpreting a full run.

## 5. Run the complete v3 GPU Gate A search

Expected flow:

```text
normal-user benchmark + one UAC owner helper
→ capture exact Original stored state
→ 5 s non-scored Original warm-up
→ 3 × 10 s scored Original observations
→ compute robust median/MAD variability
→ when noisy, extend to observation 4/5
→ broad valid variance lowers confidence; it does not block candidate testing
→ capture fresh 10 s Original O0
→ Stage A:
     one eligible logical representative per physical core
→ for each short-screen candidate:
     OriginalBefore
     apply/restart/verify candidate
     5 s non-scored candidate warm-up
     10 s scored candidate
     exact rollback + Original verification
     5 s non-scored Original warm-up
     10 s OriginalAfter
     compute paired effects + control movement
→ high control movement:
     one fresh retry
→ second noisy but structurally valid attempt:
     keep rankable and persist its uncertainty
→ Stage B:
     retain at most 4 physical-core hypotheses whose bounded uncertainty can still overlap the leader
     refine eligible siblings only on those cores
→ Stage C:
     retain the observed top two logical CPUs whenever available
     add uncertainty-overlapping challengers up to 5 CPUs total
     give every shortlisted CPU one additional 10 s local pair
     advance the top two by median short-screen effect
→ finalist confirmation:
     fresh 15 s Original control
     2 shuffled 15 s rounds by default
     one local pair per finalist per round
     add one third 15 s round only while the top-two lead remains inside measured uncertainty
→ persist median effects + 1%-low effect MAD + pair consistency
→ rank every structurally valid finalist
→ persist BestObservedProcessor + High/Medium/Low SelectionConfidence
→ evaluate separate Keep guardrails
→ apply best observed CPU only when Keep is recommended
→ final warm-up + clean kernel-ETW runtime placement verification
→ Keep only with attributable target-only GPU ISR placement
   otherwise exact RestoreOriginal
→ preserve best-observed result even when Original is restored
→ package evidence ZIP and render Overview result
```

### Expected Ryzen 7 5700X search shape

On an 8-core / 16-thread Ryzen 7 5700X with all logical processors eligible:

- Stage A should normally screen **8 physical-core representatives**, not blindly all 16 logical CPUs;
- Stage B refines only still-untested siblings on at most three top physical-core hypotheses;
- Stage C caps finalists at 3.

Do not hard-code those counts; actual topology/eligibility evidence owns the set.

## 6. Verify local pair math and noise semantics

For each persisted pair independently reconstruct:

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
- pair capture ids resolve to the expected observations;
- control movement uses adjacent Original controls;
- the first high-drift attempt can trigger one retry;
- a structurally valid second attempt remains rankable even when movement is above the noise guide;
- high drift appears in the report/UI as uncertainty rather than being hidden;
- ordinary noise alone never triggers a "two candidates => stop" path.

Structural evidence failures must still abort/fail closed.

## 7. Verify Stage A/B/C authority

### Stage A

- one eligible representative per physical core;
- no even/odd SMT assumption;
- CPU0 not globally banned;
- current CPU-set eligibility respected.

### Stage B

- candidates are aggregated by paired 1%-low effect and bounded uncertainty;
- no more than four plausible physical-core hypotheses are refined;
- the observed top two hypotheses remain eligible whenever two structurally valid hypotheses exist;
- only untested eligible siblings are added.

### Stage C

- the observed top two logical CPUs remain shortlisted whenever at least two structurally valid CPUs exist;
- additional uncertainty-overlapping challengers may join up to five total CPUs;
- every shortlisted CPU gets one additional 10-second local pair;
- only the top two by median short-screen effect advance to finalist confirmation.

## 8. Verify finalist rank and confidence

For every structurally valid finalist require:

- two shuffled 15-second pair observations in the ordinary successful path, with one third 15-second round only when the top-two lead remains inside measured uncertainty;
- persisted median 1%-low/AVG/frame-p99 effects;
- persisted 1%-low effect MAD;
- positive-pair count;
- persisted noise guide;
- raw median local Original/Candidate 1% low / AVG / p99 values.

Then verify:

- rank 1 is the highest median paired 1%-low effect;
- a practical tie does not erase rank 1;
- practical tie lowers confidence;
- `SelectionConfidence` is `High`, `Medium` or `Low` and does not control whether rank 1 exists;
- the report still has `BestObservedProcessor` on a RestoreOriginal outcome when valid ranking evidence exists.

Confidence is a best-estimate quality label, not statistical proof.

## 9. Verify Keep is separate from ranking

A best-observed CPU is not automatically a kept CPU.

Check `RecommendedForKeep` separately:

- best median 1%-low effect is positive;
- median AVG/frame-p99 guardrails do not show a clear bounded regression;
- supported GPU-driver DPC/ISR tail evidence does not materially regress.

Final Keep additionally requires:

```text
stored candidate state verified before/after
ETW integrity clean
ETW lost events = 0
attributable GPU ISR samples > 0
target processor = best observed processor
resolved off-target ISR = 0
terminal stored state verified
unresolved journal ownership = 0
```

If this final verification fails, exact Original is restored while the best-observed CPU remains in the report.

## 10. Inspect result UX

The normal Overview result should expose:

```text
Best observed CPU
Selection confidence
1% low: Original → Candidate FPS
Absolute 1% low FPS gain
Paired percentage effect
Average FPS: Original → Candidate + absolute/percentage gain
Frame p99: Original → Candidate ms + improvement
Practical tie when applicable
Kept vs Original restored
MAD/noise details
Direct pair evidence
Final runtime/terminal state
Evidence ZIP actions
```

Do not present low confidence as "no winner." Do not present a best-observed CPU as kept when Original is actually active.

## 11. Real Windows visual/accessibility inspection

CI compilation is not visual evidence. Inspect the actual result/progress surfaces on the exact physical revision in:

- Light;
- Dark;
- High Contrast;
- increased text scale;
- narrow and wide windows;
- keyboard-only navigation;
- UI Automation/Narrator as applicable.

Require no clipping/overlap, readable numbers, meaningful accessible names, correct focus order, state not encoded by color alone, and keyboard-reachable Stop safely/evidence actions.

## 12. Exercise Stop safely

During a separate full-search session:

1. wait until a candidate mutation is owned;
2. invoke **Stop safely**;
3. require exact Original rollback;
4. verify terminal stored state;
5. require `unresolved=0`.

Do not reuse the interrupted session as ranking evidence.

## 13. Exercise one supported failure/recovery path

Use an existing supported physical-validation/recovery scenario. Require visible journal ownership, actual machine-state reread, exact Original restore or explicit manual-intervention fail-closed state, and `unresolved=0` before closure.

Never make a run green by deleting the journal.

## 14. Repeat the whole search

Return to exact Original and run the full v3 search a second time on the same source revision and comparable conditions.

Compare:

- Original median/MAD variability;
- Stage-A/B/C candidate set;
- best-observed CPU;
- finalist median effects/MAD;
- confidence;
- Keep/Restore result;
- runtime-placement evidence.

Identical decimals are not required. A different close winner is acceptable only when the report exposes the uncertainty/practical tie rather than manufacturing confidence.

## 15. Gate A closure criteria

Physical GPU Gate A closes only when one exact clean green revision has evidence that:

- source/hosted Tests are exact-head green;
- valid noisy evidence remains rankable;
- structural invalidity still fails closed;
- Stage-A/B/C selection matches persisted evidence;
- pair math/retry/noise semantics reconstruct;
- finalist median/MAD rank is correct;
- best-observed CPU and confidence survive independent of Keep/Restore;
- raw before/after FPS/ms and paired percent render correctly;
- Keep guardrails and final target-only ISR proof remain independent from rank;
- exact rollback works between candidates and on cancellation/failure;
- terminal journal ownership is zero;
- Stop safely restores exact Original;
- one supported failure/recovery exercise closes cleanly;
- second whole search is honestly reproducible at the rank/confidence level;
- real result/progress UI passes recorded render/accessibility inspection.

Only then may the project proceed to arming typed allowlisted product mutation.

## 16. What this gate does not prove

GPU Gate A does not by itself prove:

- xHCI mutation/runtime verification;
- combined GPU+xHCI reboot/resume behavior;
- final one-button before/after product workflow;
- signed package/install/upgrade/uninstall closure;
- performance for arbitrary games/workloads beyond the controlled LatencyPilot method.
