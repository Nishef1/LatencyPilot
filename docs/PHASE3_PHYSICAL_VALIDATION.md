# Phase 3 Physical Validation Runbook

This is the owner-local **GPU Gate A** procedure for the simplified v1 interrupt-affinity search in ADR 0006. It does **not** arm public mutation. Protocol v6 remains observation-only and `ServiceBoundary.MutationAvailable=false` during this gate.

```text
Run GPU Gate A
= primary end-to-end development validation path
= normal-user D3D12 benchmark + elevated owner helper
= one scored screen per physical core
= two extra scored re-tests for the best up to three
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

During **screening**, valid controlled benchmark evidence may remain rankable if standalone PresentMon or kernel ETW is unavailable; that absence must be explicit in the report. If ETW is healthy and proves off-target placement, the candidate is invalid.

During **final Keep verification**, missing/unhealthy ETW is not acceptable. Keep requires clean ETW, attributable GPU ISR samples and target-only placement on the selected processor.

Stop on unknown/diverged stored state, target-identity change, failed exact rollback or recovery requiring manual intervention. Never edit/delete the SQLite journal to make validation pass.

## Required provenance

Record:

- exact clean `main` revision;
- successful hosted Tests run for that exact revision;
- Windows build;
- GPU name/driver/PnP identity;
- CPU topology and candidate list/order;
- benchmark method/schema/seed/frozen worker/workload mapping;
- one screening scored run per physical core;
- two additional scored runs for each finalist;
- AVG / 1% / 0.1% / p99 trial values;
- PresentMon version/hash/path and any collector diagnostic state;
- ETW integrity and ISR target/off-target/unresolved counts where available;
- every experiment/journal apply/rollback/keep transition;
- exact original/final stored states;
- final recommendation/processor and recovery status;
- final unresolved journal count;
- progress/taskbar/keyboard/Stop-safely observations.

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

If HEAD changes, restart the evidence set.

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

There is no separate PresentMon Service installation prerequisite. LatencyPilot uses the pinned standalone PresentMon 2.5.1 console executable with the pinned SHA-256.

In the development App click:

```text
Run GPU Gate A
```

Expected v1 flow:

```text
normal-user benchmark + one UAC owner helper
→ exact original-state capture
→ 5 s original warm-up/reference (not scored)
→ for every eligible physical core:
     apply/restart/verify stored state
     5 s warm-up (not scored; benchmark only, no PresentMon/ETW)
     1 scored screening run
     exact rollback
→ rank by higher 1% low, then 0.1% low, AVG; p99 is diagnostic/tie context
→ best up to three:
     fresh apply/restart/verify
     5 s warm-up (benchmark only; no PresentMon/ETW)
     2 additional scored runs
     exact rollback
→ rank finalists from three-run medians
→ apply winner once
→ final 5 s ETW-backed placement-verification capture
→ Keep only with verified target-only GPU ISR placement
   otherwise exact RestoreOriginal
→ report + terminal progress state
```

On the owner Ryzen 7 5700X, absent CPU-set exclusions, expect eight physical-core representatives. CPU0 is eligible. Do not assume representatives are even-numbered on arbitrary hardware.

### Ranking semantics

A finalist is ranked lexicographically by:

1. higher median 1% low;
2. higher median 0.1% low;
3. higher median AVG FPS;
4. lower median p99 only as deterministic diagnostic/tie fallback.

There is no fixed “must beat Original by 3%” rule. There is no SMT sibling-refinement phase and no ABBA/BAAB confirmation loop in v1.

For finalists, repeated 1% low spread above the current 20% bound is unstable/unrankable. If no finalist remains valid/repeatable, restore Original rather than guessing.

## 6. Inspect live progress

The main App should minimize, not disappear. The compact progress window should show:

```text
current CPU / physical core
phase (warm-up vs scored)
real completed/planned percentage
1% low primary metric
0.1% low / AVG / p99 context in final ranking
GPU ISR placement state
last completed candidate status
elapsed / estimated remaining
Stop safely
```

Ranking bars must reflect **1% low**, not p99. Accessibility meaning must be present in visible text/UI Automation rather than color alone.

## 7. Verify one candidate mutation boundary

Retain evidence for at least one screened candidate:

```text
original snapshot retained
→ journal-owned apply
→ exact stored candidate verified
→ target restart/activation recorded
→ 5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
→ scored benchmark artifact
→ exact stored candidate verified after capture
→ ETW placement evidence if healthy
→ exact rollback to Original before next candidate
```

Screening ETW absence may lower confidence without blocking ranking. Healthy ETW showing off-target ISR must invalidate the candidate.

## 8. Verify final winner Keep boundary

This is the hard activation proof.

After ranking, require a `final-verification` capture with:

```text
stored winner state verified before/after
ETW integrity complete
ETW lost events = 0
attributable GPU ISR sample count > 0
requested CPU target ISR count > 0
resolved off-target ISR count = 0
```

If any item is missing, the winner must **not** be kept. Exact Original must be restored and verified.

A successful Keep report must include non-null `finalProcessor`, `finalStateVerified=true`, the expected final stored affinity and final placement evidence consistent with that processor.

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

- same/equivalent winner from the documented low-FPS ranking; or
- explicit instability / Original restore when no finalist is repeatable.

Unacceptable: arbitrary winner changes with no uncertainty explanation.

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
final stored GPU affinity understood
final runtime identity understood
final unresolved journal count = 0
report/source SHA matches exact tested revision
Tests run matches exact tested revision
```

Inspect `latencypilot-gpu-auto-affinity-report-v1` and require mutation audit/recovery state to agree with the live journal. A JSON report can never override conflicting live machine/journal state.

## Gate A closure criteria

Gate A passes only when one exact clean green revision physically proves:

1. normal App/Service development path works;
2. journal starts/ends with zero unresolved state;
3. D3D12 benchmark smoke works without mutation;
4. every eligible physical core receives exactly one scored screening run after warm-up;
5. best up-to-three each receive exactly two extra scored re-tests;
6. winner is selected by the ADR 0006 metric order and repeated finalists are stable;
7. exact rollback succeeds between every candidate block;
8. final Keep occurs only after clean target-only GPU ISR placement proof;
9. failed/unverified final placement restores exact Original;
10. Stop safely restores/verifies Original;
11. one supported failure/recovery path is proven;
12. repeated whole searches are reproducible/equivalent or explicitly unstable;
13. progress/taskbar/keyboard/accessibility behavior is physically sane;
14. final machine state is known and verified.

Passing GPU Gate A authorizes the next mutation-boundary work; it does not arm public mutation by itself.


## Post-GPU USB recommendation evidence

When Gate A finishes with a verified GPU Keep, the helper now performs one additional **read-only** 10 s quiet ETW capture after stopping the benchmark. It resolves Raw Input mouse routes to exact USB hub/port/xHCI ownership, excludes the physical core containing the GPU winner, and records the recommended xHCI CPU in `UsbRecommendation` together with total DPC+ISR duration, p99 interrupt tail and DPC/ISR counts. Multiple distinct mouse xHCI controllers or unresolved routes produce `NotReady`; this phase does not mutate USB/xHCI policy before the GPU physical gate passes.
