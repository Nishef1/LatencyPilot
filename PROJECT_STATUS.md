# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-22

## Overall

- Product version: **0.0.2 pre-alpha** (`RELEASE_VERSION`).
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- GPU benchmark method: **`gpu-affinity-benchmark-v3`**.
- GPU evidence envelope/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v3`**. The evidence envelope schema remains v1; method/report identities are versioned independently.
- Product/safety authority: **ADR 0006**.
- GPU measurement/search/ranking authority: **ADR 0008**.
- ADR 0007 remains the historical paired-v2 contract and does not govern new evidence.
- Hosted GitHub Actions is **software-contract evidence only**.

## Current v1 direction

LatencyPilot remains a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet-context capture
→ deep ETW baseline
→ robust Original variability estimate
→ paired GPU screening: Original before → Candidate → Original after
→ physical-core representatives → top-3 refinement → up to 3 finalists
→ three shuffled 30 s local pairs per finalist
→ best-observed CPU + confidence
→ separate Keep guardrails + final target-only GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore original settings
```

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power changes and a generic cross-subsystem optimizer remain outside the v1 automatic path.

## Implemented source

### Measurement/recovery foundations

Implemented:

- processor-group-aware topology and CPU-set eligibility evidence;
- PnP/device/driver inventory;
- bounded kernel ETW DPC/ISR capture with per-CPU/module attribution and integrity accounting;
- evidence provenance/export contracts;
- durable SQLite mutation journal, compare-and-set lifecycle and recovery ownership;
- exact stored-state snapshot/restore primitives;
- non-elevated App plus narrow privileged helper/service boundaries;
- GPU interrupt-affinity mutation, target restart, renderer recreation and exact rollback;
- Raw Input → USB hub/port → xHCI read-only topology and input-host timing source.

### GPU noise-tolerant v3 contract

Current source implements ADR 0008:

1. 5 s non-scored Original warm-up.
2. Three scored 10 s Original observations; if robust median/MAD variability is high, extend to at most five.
3. Ordinary Original variability lowers confidence rather than stopping candidate mutation.
4. Fresh 10 s Original control `O0`.
5. Local screening pairs `O0 → C1 → O1 → C2 → O2 ...` with exact rollback between candidates.
6. Pair effect uses the geometric mean of adjacent Original controls; raw values remain untouched.
7. High local drift gets one fresh retry. A still-noisy but structurally valid retry remains rankable; there is no consecutive-noise early stop.
8. Stage A screens one representative logical CPU per eligible physical core.
9. Stage B refines at most the top three physical-core hypotheses.
10. Stage C advances at most the top three logical CPUs.
11. Finalists receive three deterministically shuffled 30 s local pairs.
12. Every structurally valid finalist is ranked by median paired 1%-low effect.
13. MAD, Original variability, lead over runner-up, pair consistency and practical-tie state produce `High`/`Medium`/`Low` confidence; confidence never gates rank.
14. `RecommendedForKeep` is separate: positive median benefit plus bounded AVG/frame-p99/interrupt-tail guardrails.
15. Final Keep still requires exact stored state and clean attributable target-only GPU ISR placement. Otherwise exact Original is restored while the best-observed CPU remains in the report.

The v3 report persists raw trials/pairs, finalist medians, effect MAD, positive-pair count, noise guide, raw median Original/Candidate FPS/ms, `BestObservedProcessor`, `SelectionConfidence`, Keep recommendation, provenance and terminal state.

### Diagnostic scopes

The developer UI provides:

- **Full search** — the authoritative noise-tolerant v3 machine search.
- **Selected CPUs · restore Original** — real paired screening for an exact subset; diagnostic-only, no finalist Keep, always restores Original, cannot close Gate A.
- **Original only · no system changes** — five 10 s Original observations with no affinity mutation or device restart.

CPU selection uses current topology/CPU-set eligibility; unsupported topology fails safely. The dialog is a WinUI `ContentDialog` owned by the current `XamlRoot`.

### Gate A result experience

The validated completion path remains:

```text
validate report/session/source
→ package evidence ZIP when possible
→ build GateAResultPresentation
→ render authoritative result in Overview
```

The result surface now distinguishes:

- best-observed CPU from kept CPU / restored Original;
- persisted rank from shuffled execution order;
- High/Medium/Low confidence from ranking itself;
- actual median Original → Candidate 1% low / AVG FPS and frame-p99 ms;
- absolute FPS/ms improvement plus paired percentage effect;
- practical tie from “no result”;
- MAD/noise detail instead of a hard winner decision floor;
- direct pair evidence and exact terminal machine state.

Candidate bars remain based on persisted paired 1%-low effect and UI code does not invent a second ranking. A RestoreOriginal result can still truthfully show the best-observed CPU.

### USB/xHCI recommendation

After a verified GPU Keep, current source can stop the GPU benchmark, capture quiet ETW headroom, resolve primary Raw Input routes to exact xHCI controllers, exclude the GPU winner's physical core, rank remaining CPUs by interrupt duration/tail evidence, and persist the recommendation.

The xHCI recommendation remains read-only/product-gated until the shared mutation/recovery substrate closes physical GPU Gate A and the integrated xHCI verification path is completed.

## Verification state

### Hosted software verification

The v3 work reuses the consolidated critical-test budget. New/updated contracts specifically guard:

- noise cannot stop the search after two candidates;
- high drift gets one retry but a structurally valid retry remains rankable;
- Original variability uses median/MAD rather than quiet-cluster cherry-picking;
- best-observed CPU persists independently from Keep/Restore;
- selection confidence is metadata rather than a winner threshold;
- result presentation includes actual before/after FPS/ms plus absolute and percentage improvement;
- final target-only ISR placement and rollback safety remain separate Keep requirements.

Exact-head hosted **Tests** are mandatory for every revision used as physical closure evidence. An older green run is never reused for a newer SHA.

### Physical GPU Gate A

**OPEN for v3.**

The 2026-09-22 v2 investigation remains useful historical evidence: real owner runs showed large Original/pair variability and graphics-hook context, demonstrating that a hard noise-as-validity gate could prevent any candidate ranking on an ordinary Windows system. Those v2 reports are not reinterpreted as v3 evidence.

Current v3 physical validation must prove on one exact clean green revision:

1. noisy but structurally valid Original observations continue into candidate testing and reduce confidence instead of causing a noise-only stop;
2. Stage-A coverage and bounded top-3 refinement match actual topology;
3. pair math and raw controls reconstruct from persisted evidence;
4. high-drift retry evidence remains visible while valid candidates remain ranked;
5. finalists receive three shuffled 30 s observations and persisted median/MAD authority matches the rank;
6. `BestObservedProcessor` and `SelectionConfidence` agree with report/UI even when Original is restored;
7. actual before/after FPS/ms and paired percentage effect render correctly;
8. Keep guardrails do not affect who is ranked first;
9. exact rollback succeeds between candidates and on failure/cancellation;
10. final ETW proves attributable target-only GPU ISR placement before Keep;
11. terminal state verifies with `unresolved=0`;
12. **Stop safely** and one supported failure/recovery path restore exact Original;
13. a second full v3 search produces comparable ranking/confidence behavior without requiring identical decimals;
14. real Windows result UI is inspected for theme/text-scale/keyboard/accessibility behavior.

Dirty runs remain development evidence only. Public mutation remains unarmed until physical Gate A passes.

## Roadmap snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Keep exact-head verification current |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI preflight |
| 2 Baseline | **ETW engine exists** | Wire deep comparable baseline into one-button workflow |
| 3 GPU search | **Noise-tolerant v3 source/result contracts implemented** | Exact-head CI + physical v3 Gate A + repeat + recovery/render inspection |
| 4 GPU Keep | **Internal verified-Keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only recommendation implemented** | Representative physical evidence + product rendering |
| 6 USB apply | **Internal reversible substrate / product-gated** | Integrated physical apply/verify/rollback evidence |
| 7 Reboot verify | **Recovery/reboot primitives implemented** | Combined GPU+xHCI reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated comparable final capture/report |
| 9 UX/release | **Development Gate A result UX implemented** | Real render/accessibility + one-button product UX + release closure |

## Immediate execution ladder

1. **Completed now:** v3 source separates best-observed ranking/confidence from Keep, removes noise-only early stops, adds median/MAD evidence and actual before/after result values.
2. **Evidence:** consolidated critical tests are updated to fail if noise again erases valid ranking or the result UI loses confidence/absolute-gain evidence.
3. **Still open:** hosted Tests must be green for the final documentation-aligned HEAD, then v3 needs physical owner evidence.
4. **Next stage:** run the exact-head full v3 search and inspect best-observed rank, confidence, FPS/ms gains, rollback and final placement.
5. **After that:** repeat the search, exercise Stop safely/failure recovery, and complete real WinUI/accessibility inspection before product mutation arming.

## Completion rule

Repository/source completion means every current v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.
