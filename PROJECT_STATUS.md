# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-25

## Overall

- Product version: **0.0.2 pre-alpha** (`RELEASE_VERSION`).
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- GPU benchmark method: **`gpu-affinity-benchmark-v6`**.
- GPU evidence envelope/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v3`**. The evidence envelope schema remains v1; method/report identities are versioned independently.
- Product/safety authority: **ADR 0006**.
- GPU measurement/search/ranking authority: **ADR 0011**.
- ADR 0010 remains the historical v5 contract; ADR 0009 remains historical v4 (ADR 0008 historical v3). None governs new v6 evidence.
- Hosted GitHub Actions is **software-contract evidence only**.

## Current v1 direction

LatencyPilot remains a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet-context capture
→ deep ETW baseline
→ robust Original variability estimate
→ paired GPU screening: Original before → Candidate → Original after
→ physical-core representatives → uncertainty-aware core/sibling refinement
→ top-four 10 s recheck shortlist + at most one uncertainty-overlapping fifth challenger → top 2
→ two shuffled 15 s pairs per finalist; third pair only if uncertainty remains
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

### GPU restart-canonicalized observer-isolated adaptive v6 contract

Current source implements ADR 0011:

1. Full/Selected-CPU paired search first verifies the exact captured Original policy + driver, performs one real in-place GPU restart with that policy unchanged, re-verifies Original + driver identity, and recreates the D3D12 renderer before any benchmark warm-up or scored evidence.
2. A 5 s non-scored Original warm-up follows that canonical restart.
3. Three scored 10 s Original observations are captured; if robust median/MAD variability is high, the estimate extends to at most five.
4. Ordinary Original variability lowers confidence rather than stopping candidate mutation.
5. Fresh 10 s Original control `O0`.
6. Local screening pairs `O0 → C1 → O1 → C2 → O2 ...` with exact rollback between candidates.
7. Pair effect uses the geometric mean of adjacent Original controls; raw values remain untouched.
8. High local drift gets one fresh retry. A still-noisy but structurally valid retry remains rankable; there is no consecutive-noise early stop.
9. Stage A screens one representative logical CPU per eligible physical core.
10. Stage B retains at most four physical-core hypotheses whose bounded uncertainty can still overlap the leader.
11. Stage C rechecks the observed top four logical CPUs whenever available and admits at most one additional uncertainty-overlapping fifth challenger.
12. Scored observer-active trials use a bounded unscored 2–4 s startup settle with a 500 ms quiet tail; warm-ups without external observers skip that cost.
13. Frozen CPU workers use exact physical-core affinity masks persisted in workload identity and verified against captured topology.
14. Only the top two CPUs advance to finalist confirmation.
15. Finalists receive two deterministically shuffled 15 s local pairs; one third 15 s round is added only while their lead remains inside measured uncertainty.
16. Every structurally valid finalist is ranked by median paired 1%-low effect.
17. MAD, Original variability, lead over runner-up, pair consistency and practical-tie state produce `High`/`Medium`/`Low` **ranking confidence**; confidence never gates rank or Keep.
18. `RecommendedForKeep` is separate: positive median benefit, positive-pair consistency and bounded AVG/frame-p99/interrupt-tail guardrails.
19. Final Keep still requires exact stored state and clean attributable target-only GPU ISR placement. Otherwise exact Original is restored while the best-observed CPU remains in the report.

The v6 method keeps the existing v3 report schema and persists raw trials/pairs, finalist medians, effect MAD, positive-pair count, noise guide, raw median Original/Candidate FPS/ms, `BestObservedProcessor`, `SelectionConfidence`, structured finalist guardrail reasons, provenance and terminal state. Historical v5 and earlier evidence is never reinterpreted as v6.

### Diagnostic scopes

The developer UI provides:

- **Full search** — the authoritative restart-canonicalized observer-isolated adaptive v6 machine search.
- **Selected CPUs · restore Original** — real paired screening for an exact subset; diagnostic-only, no finalist Keep, always restores Original, cannot close Gate A.
- **Original only · no system changes** — five 10 s Original observations with no affinity mutation or device restart.

CPU selection uses current topology/CPU-set eligibility; unsupported topology fails safely. The diagnostic scope selector remains a WinUI `ContentDialog`; the manual interrupt-affinity tool is an independent WinUI window.

### Development manual device affinity lab

The Devices page now exposes a development-only manual affinity surface without changing the public observation-only Service contract:

- the development UI keeps the familiar Interrupt-Affinity Policy Configuration Tool information model, but now opens as an independent modern WinUI tool window with stacked device → selected-device → interrupt-affinity sections, search, concise identity details, Fluent device/action icons, inline multi-CPU mask selection, and Current policy / Specified mask / Current assignment evidence;
- current stored interrupt policy / `AssignmentSetOverride` and translated allocated interrupt-resource masks are shown separately for latency-sensitive present devices;
- GPU and USBXHCI are the only editable targets because they already have bounded journal/restart/rollback ownership;
- network, audio, storage, HID and other latency-sensitive devices remain read-only;
- manual Apply accepts a non-empty group-0 KAFFINITY set and uses an elevated one-shot helper: exact snapshot → journal → apply/restart → verify every translated allocation stays inside the requested mask → clean ETW ISR proof that attributable runtime execution stays inside that mask → Keep, or exact rollback on failed verification;
- GPU manual verification reuses the existing GPU runtime-placement verifier; xHCI now has a controller-specific `USBXHCI` ISR verifier and deliberately fails closed when more than one present controller shares that driver service because the shared module stream is ambiguous;
- a reboot-pending xHCI experiment may resume only for the same journaled CPU candidate;
- Restore is per-device and only unwinds retained changes actually owned by the LatencyPilot mutation journal; an external pre-existing override is never claimed or overwritten as LatencyPilot-owned;
- the App blocks manual mutation while GPU Gate A is running.

This lab does **not** arm `ServiceBoundary.MutationAvailable`, add generic registry mutation, or widen the automatic v1 path to NIC/audio/storage tuning.

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
- absolute FPS/ms improvement plus direct raw-median percentage change, with the separate drift-adjusted paired effect preserved for ranking;
- practical tie from “no result”;
- MAD/noise detail instead of a hard winner decision floor;
- direct pair evidence and exact terminal machine state.

Candidate bars remain based on persisted paired 1%-low effect and UI code does not invent a second ranking. A RestoreOriginal result can still truthfully show the best-observed CPU.

### USB/xHCI recommendation

After a verified GPU Keep, current source can stop the GPU benchmark, capture quiet ETW headroom, resolve primary Raw Input routes to exact xHCI controllers, exclude the GPU winner's physical core, rank remaining CPUs by interrupt duration/tail evidence, and persist the recommendation.

The xHCI recommendation remains read-only/product-gated until the shared mutation/recovery substrate closes physical GPU Gate A. A fail-closed controller-specific ETW runtime-placement verifier now exists for unambiguous single-`USBXHCI`-controller systems, but the automatic product apply/verify flow and multi-controller attribution remain open.

## Verification state

### Hosted software verification

The v6 work reuses the consolidated critical-test budget. New/updated contracts specifically guard:

- noise cannot stop the search after two candidates;
- high drift gets one retry but a structurally valid retry remains rankable;
- Original variability uses median/MAD rather than quiet-cluster cherry-picking;
- best-observed CPU persists independently from Keep/Restore;
- selection confidence is metadata rather than a winner threshold;
- result presentation includes actual before/after FPS/ms plus absolute and percentage improvement;
- final target-only ISR placement and rollback safety remain separate Keep requirements.

Exact-head hosted **Tests** are mandatory for every revision used as physical closure evidence. An older green run is never reused for a newer SHA.

### Physical GPU Gate A

**OPEN for v6.**

The 2026-09-22 v2 investigation remains useful historical evidence: real owner runs showed large Original/pair variability and graphics-hook context, demonstrating that a hard noise-as-validity gate could prevent any candidate ranking on an ordinary Windows system. Those v2 reports are not reinterpreted as later-method evidence.

The 2026-09-24 owner v4 development run exposed two additional methodology defects: phase-locked startup transients in observer-active scored windows and a short-screen cut that failed to recheck CPU4/CPU14-like positive candidates. v5 addressed those defects and removed fixed-first-SMT worker affinity asymmetry.

The 2026-09-25 owner v5 run from source revision `071abc671352ee698c38e23851828f2667ef0e4b` completed safely and provided useful decision evidence: finalist confirmation reversed CPU15's optimistic short-screen signal, CPU4 remained the best observed finalist, an interrupt-tail Keep guardrail rejected it, and exact Original finished verified with `recoveryStatus=clean-zero-unresolved`. It also exposed a new methodology defect: the initial decision baseline was captured before any GPU restart (about 297.8 FPS 1% low / 367.5 FPS AVG), while the first Original after a real candidate apply+rollback/restart entered a much higher regime (about 379.4 FPS / 430.9 FPS) that persisted. Local pairing protected ranking, but the initial Original estimate and paired controls did not share equivalent restart/renderer history. ADR 0011/v6 therefore canonicalizes Original with one real restart before scoring.

Current v6 physical validation must prove on one exact clean green revision:

1. the pre-score Original canonicalization restart completes in place under the unchanged captured policy, exact Original + driver identity re-verify, and the renderer is recreated before any scored evidence;
2. the initial 3–5 Original estimate and subsequent rollback Original controls no longer show a one-time pre-restart/post-restart regime discontinuity of the kind exposed by the v5 owner run;
3. noisy but structurally valid Original observations continue into candidate testing and reduce confidence instead of causing a noise-only stop;
4. Stage-A coverage, uncertainty-aware core refinement and the top-four-plus-optional-fifth recheck shortlist match persisted evidence;
5. scored observer-active trials reach the bounded quiet pre-score boundary and warm-ups do not pay observer-settle overhead;
6. persisted worker affinity masks exactly match physical-core topology and stay frozen across Original/Candidate controls;
7. pair math and raw controls reconstruct from persisted evidence;
8. high-drift retry evidence remains visible while valid candidates remain ranked;
9. the top two finalists receive two 15 s observations, with a third only when uncertainty remains, and persisted median/MAD authority matches the rank;
10. `BestObservedProcessor` and ranking confidence agree with report/UI even when Original is restored;
11. actual before/after FPS/ms and paired percentage effect render correctly;
12. Keep guardrails remain separate from rank and the concrete failed guardrail is shown when a best-observed CPU is not kept;
13. exact rollback succeeds between candidates and on failure/cancellation;
14. final ETW proves attributable target-only GPU ISR placement before Keep;
15. terminal state verifies with `unresolved=0`;
16. **Stop safely** and one supported failure/recovery path restore exact Original;
17. a second full v6 search produces comparable ranking/confidence behavior without requiring identical decimals;
18. real Windows result UI is inspected for theme/text-scale/keyboard/accessibility behavior.

Graphics-hook warnings such as `nvspcap64.dll` remain explicit interference context, not proof of causation. Dirty runs remain development evidence only. Public mutation remains unarmed until physical Gate A passes.

## Roadmap snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Keep exact-head verification current |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI preflight |
| 2 Baseline | **ETW engine exists** | Wire deep comparable baseline into one-button workflow |
| 3 GPU search | **Restart-canonicalized observer-isolated adaptive v6 source/result contracts implemented** | Exact-head CI + physical v6 Gate A + repeat + recovery/render inspection |
| 4 GPU Keep | **Internal verified-Keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only recommendation implemented** | Representative physical evidence + product rendering |
| 6 USB apply | **Internal reversible substrate / product-gated** | Integrated physical apply/verify/rollback evidence |
| 7 Reboot verify | **Recovery/reboot primitives implemented** | Combined GPU+xHCI reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated comparable final capture/report |
| 9 UX/release | **Development Gate A result UX implemented** | Real render/accessibility + one-button product UX + release closure |

## Immediate execution ladder

1. **Completed now:** source has moved to `gpu-affinity-benchmark-v6`. Full and Selected-CPU paired search canonicalize the exact Original state with one verified in-place GPU restart + renderer recreation before any warm-up/scored baseline. Original-only diagnostics remain restart-free. Result UX now labels **ranking confidence** explicitly, surfaces concrete Keep-blocker evidence, gives metric cards a concise value hierarchy, expands finalist/noisy pairs by default, prevents candidate-state text truncation, compacts Evidence actions, and collapses the duplicate long candidate list in the terminal progress window.
2. **Evidence:** the design change is grounded in the 2026-09-25 owner v5 bundle from `071abc671352ee698c38e23851828f2667ef0e4b`, which safely restored Original but exposed a large pre-restart → post-restart Original regime shift. Hosted exact-head Tests for this v6 implementation are pending and remain software-contract evidence only.
3. **Still open:** one new physical v6 Full run on owner hardware, then a repeat run, Stop-safely/recovery exercise, and render/accessibility inspection. The manual multi-CPU GPU/xHCI mask path also still needs owner-hardware proof. Public product mutation remains unarmed.
4. **Next stage:** get exact-head hosted Tests green, then run v6 Full Gate A and inspect the persisted mutation audit for the canonical Original restart plus the first/ongoing Original controls for regime continuity.
5. **After that:** repeat the whole v6 search and close physical recovery/render gates before any typed public mutation IPC or normal-user arming.



## Completion rule

Repository/source completion means every current v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.
