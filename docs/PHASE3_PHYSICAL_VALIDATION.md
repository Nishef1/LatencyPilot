# Phase 3 Physical Validation Runbook

This is the owner-local **GPU Gate A** procedure for the simplified v1 interrupt-affinity search in ADR 0006, including the 2026-09-20 time-local measurement amendment. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false` during this gate.

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= one scored screen per eligible logical processor
= bounded Original controls around screening blocks
= time-local normalized decision evidence + explicit uncertainty
= bounded finalist re-tests when variability permits
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

Keep these measurement layers separate as well:

```text
raw scored trial history
!= time-local normalized decision aggregate
!= verified terminal machine state
```

During **screening**, valid controlled benchmark evidence may remain rankable if standalone PresentMon or kernel ETW is unavailable; that absence must be explicit. If healthy ETW proves off-target placement, the candidate is invalid.

During **final Keep verification**, missing/unhealthy ETW is not acceptable. Keep requires clean ETW, attributable GPU ISR samples and target-only placement on the selected processor.

Stop on unknown/diverged stored state, target-identity change, invalid/mismatched benchmark evidence, failed exact rollback or recovery requiring manual intervention. Ordinary gradual Original-control movement is not by itself a structural failure; current source normalizes decision aggregates against time-local controls and carries that movement into uncertainty.

Never edit/delete the SQLite journal to make validation pass.

## Required provenance

Record:

- exact clean `main` revision;
- successful hosted Tests run for that exact revision;
- Windows build;
- GPU name/driver/PnP identity;
- CPU topology and full eligible logical-processor candidate list/order;
- benchmark method/schema/seed/frozen worker/workload mapping;
- Original baseline scored observations and repeatability/noise;
- one screening scored run per eligible logical processor;
- intermediate/final Original block controls;
- raw trial AVG / 1% / 0.1% / p99 values;
- persisted candidate decision aggregates/ranks after time-local normalization where applicable;
- local-control uncertainty;
- finalist/replacement runs when finalist confirmation runs;
- PresentMon version/hash/path and any collector diagnostic state;
- ETW integrity and ISR target/off-target/unresolved counts where available;
- every experiment/journal apply/rollback/keep transition;
- exact original/final stored states;
- final recommendation/processor and recovery status;
- final unresolved journal count;
- generated evidence ZIP path/status;
- rendered Overview result/taskbar/keyboard/Stop-safely observations.

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

If HEAD changes, restart the authoritative evidence set.

## 2. Validate normal App/Service path

Run the established development launcher:

```powershell
.\run.ps1
```

Before mutation confirm App/Service observation works, evidence export works and startup reports no unexplained unresolved mutation state. Do not continue if the normal path is broken.

## 3. Read-only preflight

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect

dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- list-gpus
```

Require `unresolved=0`. Record device/driver/topology. Multi-group GPU mutation remains fail-closed in v1.

## 4. D3D12 benchmark smoke without mutation

Run the existing read-only benchmark smoke on the exact revision. Verify calibration freezes, multiple workers are active, D3D12 timing is finite and an artifact is produced. This is not a winner trial; it only proves the workload path.

## 5. Run complete GPU Gate A search

There is no separate PresentMon Service installation prerequisite. LatencyPilot uses the pinned standalone PresentMon console executable and verifies the expected binary/hash contract.

In the development App click:

```text
Run GPU Gate A
```

Expected current v1 flow:

```text
normal-user benchmark + one UAC owner helper
→ exact Original-state capture
→ Original non-scored warm-up/reference
→ three scored Original observations; one bounded replacement if needed
→ every eligible logical processor, in bounded blocks of at most four:
     apply/restart/verify stored candidate state
     non-scored benchmark warm-up
     one scored 30 s screening run
     exact rollback + Original-state verification
     fresh Original control after each full block when candidates remain
→ fresh Original control after final screening block
→ compute time-local control movement
→ normalize rankable candidate decision aggregates back to the session Original baseline
   while retaining raw scored trials unchanged
→ carry local movement into uncertainty
→ if effective 1%-low variability >15%:
     retain diagnostic screening evidence
     skip finalist re-tests
     verify/retain Original
→ otherwise rank normalized decision evidence by 1% low → AVG → lower p99 → 0.1% rare-tail context
→ shortlist best three plus candidates inside the bounded noise-aware cutoff, capped at five
→ two independent shuffled finalist re-test rounds
→ one bounded replacement score only when a preferred three-run cluster is still missing
→ fresh Original control after finalist phase; merge phase movement into uncertainty
→ evaluate finalists against Original + repeatability + time-local uncertainty + frame/interrupt-tail guardrails
→ apply highest-ranked clean finalist once
→ final benchmark-only warm-up + ETW runtime-placement verification
→ Keep only with verified target-only GPU ISR placement
   otherwise exact RestoreOriginal
→ validate report identity/source eligibility
→ package shareable evidence ZIP when possible
→ render authority-selected result in Overview
```

On the owner Ryzen 7 5700X, absent CPU-set exclusions, expect **16 logical-processor candidates**, not eight physical-core representatives. Do not assume even-numbered candidates or collapse SMT siblings on arbitrary hardware.

### Ranking semantics

Candidate decision evidence is ranked lexicographically by:

1. higher 1% low, treating <=1% relative difference as a practical tie;
2. higher AVG FPS, same <=1% equivalence margin;
3. lower frame-p99, same <=1% equivalence margin;
4. 0.1% low only when a remaining relative difference exceeds 5%;
5. passive deterministic topology/pressure fallback only if measured metrics remain tied.

There is no fixed “must beat Original by 3%” rule. There is no separate SMT-refinement phase and no ABBA/BAAB confirmation loop.

### Time-local control semantics

The Original controls are not candidates and ordinary drift is not automatically a failed experiment. Check that:

- raw candidate/control measurements remain preserved;
- decision aggregates use the persisted time-local normalization result rather than raw collection order;
- measured control movement is visible as uncertainty;
- the final Keep threshold includes that uncertainty;
- if effective 1%-low variability exceeds 15%, finalist confirmation is skipped and Original is retained;
- structural evidence failures remain fail-closed rather than being normalized.

A result that simply chooses the fastest early raw sample despite measured background movement is a failure.

## 6. Inspect live progress and final result

While running, the main App should minimize, not disappear. The compact progress window should show meaningful phase/progress state and expose **Stop safely**.

After a valid terminal report is available, the main window should return to Overview and show the in-product Gate A result instead of automatically opening raw JSON.

Inspect:

```text
terminal result + source/closure eligibility
Original → comparison decision metrics
candidate comparison chart
scored repeatability/trial history
Why this decision rows
evidence ZIP status
actions: Open ZIP / Copy ZIP path / Open session folder / Open raw report
```

For RestoreOriginal outcomes, any ranked candidate shown for diagnosis must be explicitly comparison-only/not kept. The UI must not re-rank shuffled raw candidate order.

Check Light, Dark, High Contrast, text scale, narrow/wide layout, keyboard reachability and UI Automation meaning. Color alone must not carry decision state.

## 7. Verify one candidate mutation boundary

Retain evidence for at least one screened candidate:

```text
Original snapshot retained
→ journal-owned apply
→ exact stored candidate verified
→ target restart/activation recorded
→ non-scored warm-up
→ scored benchmark artifact
→ exact stored candidate verified after capture
→ ETW placement evidence if healthy
→ exact rollback to Original before next candidate
```

Screening ETW absence may lower confidence without blocking benchmark-owned ranking. Healthy ETW proving off-target ISR must invalidate the candidate.

## 8. Verify final winner Keep boundary

This is the hard activation proof.

After ranking, a `final-verification` capture must prove:

```text
stored winner state verified before/after
ETW integrity complete
ETW lost events = 0
attributable GPU ISR sample count > 0
requested CPU target ISR count > 0
resolved off-target ISR count = 0
```

If any item is missing, the winner must **not** be kept. Exact Original must be restored and verified.

A successful Keep report must include non-null `finalProcessor`, `finalStateVerified=true`, expected final stored affinity and placement evidence consistent with that processor.

## 9. Exercise Stop safely

During a separate run, request **Stop safely** while work is in progress.

Require:

```text
no future trial starts beyond the cancellation boundary
owned candidate is never silently kept
rollback/recovery stays owned until terminal state
exact Original verified
unresolved journal count = 0
```

Closing the progress window before terminal state must request the same safe stop rather than abandon the helper.

## 10. Repeat the complete search

Run the full search at least twice on the same exact revision under comparable quiet conditions.

Acceptable:

- same/equivalent decision-grade winner under the documented ranking/uncertainty method; or
- explicit RestoreOriginal when variability/noise/guardrails/placement do not support Keep.

Unacceptable: arbitrary winner changes with no uncertainty explanation or a winner inferred from raw shuffled order.

## 11. Supported failure/recovery exercise

Use only existing supported validation/recovery mechanisms. Do not corrupt unrelated registry/device state to manufacture failure. The exercise passes only when interruption remains journal-owned and final exact state is verified.

## 12. Final reconciliation

After the final run:

```powershell
dotnet run --project .\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj --configuration Release -- inspect
```

Require:

```text
final recommendation understood
final selected processor or verified Original restore
raw vs decision evidence understood
local-control uncertainty understood
final stored GPU affinity understood
final runtime identity understood
final unresolved journal count = 0
report/source SHA matches exact tested revision
Tests run matches exact tested revision
```

Inspect `latencypilot-gpu-auto-affinity-report-v1` and the generated evidence ZIP. Mutation audit/recovery state must agree with the live journal. A JSON report or UI presentation can never override conflicting live machine/journal state.

## Gate A closure criteria

Gate A passes only when one exact clean green revision physically proves:

1. normal App/Service development path works;
2. journal starts/ends with zero unresolved state;
3. D3D12 benchmark smoke works without mutation;
4. every eligible logical processor receives one scored screening run;
5. Original block/final controls are captured and time-local normalization/uncertainty are persisted correctly;
6. effective variability above the exhaustive-confirmation budget safely retains Original without manufacturing a Keep winner;
7. otherwise the documented bounded shortlist receives its required independent re-tests;
8. ranking follows the ADR 0006 decision order using persisted decision aggregates;
9. exact rollback succeeds between candidate activations;
10. final Keep occurs only after clean target-only GPU ISR placement proof;
11. failed/unverified final placement restores exact Original;
12. Stop safely restores/verifies Original;
13. one supported failure/recovery path is proven;
14. repeated whole searches are reproducible/equivalent or explicitly uncertain;
15. Overview result/evidence actions and accessibility behavior are physically sane;
16. final machine state is known and verified.

Passing GPU Gate A authorizes the next mutation-boundary work; it does not arm public mutation by itself.

## Post-GPU USB recommendation evidence

When Gate A finishes with a verified GPU Keep, the helper performs one additional **read-only** quiet ETW capture after stopping the benchmark. It resolves Raw Input mouse routes to exact USB hub/port/xHCI ownership, excludes the physical core containing the GPU winner, and records the recommended xHCI CPU in `UsbRecommendation` together with total DPC+ISR duration, p99 interrupt tail and DPC/ISR counts. Multiple distinct mouse xHCI controllers or unresolved routes produce `NotReady`; this phase does not mutate USB/xHCI policy before the GPU physical gate passes.