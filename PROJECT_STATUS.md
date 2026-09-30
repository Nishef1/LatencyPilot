# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-29

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
- Subsystem surface/authority separation: **ADR 0012**.
- ADR 0010 remains the historical v5 contract; ADR 0009 remains historical v4 (ADR 0008 historical v3). None governs new v6 evidence.
- Hosted GitHub Actions is **software-contract evidence only**.

## 2026-09-29 subsystem-surface separation

The product surface is now intentionally split by subsystem:

- **GPU Gate A is GPU-only.** The App no longer asks for a primary mouse before Gate A, the elevated Gate A runner no longer accepts `--primary-input`, and new Gate A reports no longer create a post-GPU USB recommendation.
- **USB / xHCI has its own Devices-page action** for route/controller evidence. Its future automatic selection/apply flow owns its own readiness and verification instead of borrowing Gate A authority.
- **Network / RSS has its own Devices-page action** and remains read-only in v1. The action selects a PnP-correlated physical NIC independently of RSS settings (excluding virtual/debug/software adapters), supplements MSI/MSI-X evidence from `MSFT_NetAdapterHardwareInfoSettingData` when the RSS settings row is absent, captures a short target-miniport DPC/ISR sample with network-environment continuity checks, and uses interface byte deltas to distinguish an idle zero-event capture from missing runtime attribution. RSS is not modeled as a GPU-style single-CPU tournament.
- **Interrupt Policy Lab stays separate** as a manual development utility and is not the automatic entry point for GPU, USB, or network optimization.
- Historical GPU reports that already contain a `UsbRecommendation` remain renderable for compatibility; new GPU Gate A sessions leave that optional field unset.

The long-term GPU → xHCI product sequence is still valid as orchestration, but it is no longer represented as one Gate A button/session. Each subsystem must be independently observable, runnable and verifiable before any combined "Optimize all" experience can compose them.

## Current v1 direction

LatencyPilot remains a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet-context capture
→ deep ETW baseline
→ robust Original variability estimate
→ paired GPU screening: Original before → Candidate → Original after
→ exhaustive paired first-pass screen of every eligible logical CPU
→ top-four 10 s recheck shortlist + at most one uncertainty-overlapping fifth challenger → top 2
→ two shuffled 15 s pairs per finalist; third pair only if uncertainty remains
→ best-observed CPU + confidence
→ separate Keep guardrails + active allocated-resource proof + final direct-driver target-only GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore original settings
```

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power changes and a generic cross-subsystem optimizer remain outside the v1 automatic path.

The v1 subsystem benchmarks are **independent actions**. GPU evidence owns only the GPU decision; USB/xHCI owns only its own route, headroom ranking and verification. Cross-subsystem coordination is limited to a CPU-reservation snapshot derived from current explicit specified-processor device policies. A manually fixed GPU therefore reserves the same CPU/core capacity as a Gate-A-fixed GPU, without requiring benchmark provenance. There is no joint weighted score or hidden cross-benchmark ranking.

### 2026-09-28 scope reconciliation

Research and source review found four scope contradictions and resolves them without widening v1:

- an earlier expansion idea would have made physical NIC/RSS the third automatic mutation stage, but Windows RSS is a separate multi-CPU receive-steering mechanism and MSI-X can align interrupts with RSS queues. **Decision:** keep a separate read-only Network/RSS evidence action in v1, keep NIC/RSS mutation outside v1, and require a dedicated ADR/method plus physical evidence before any future mutation;
- the development manual picker previously allowed generic storage/system-device affinity edits, while the owner direction and Windows storage/platform architecture argue against LatencyPilot tuning them. **Resolved:** storage and Windows System-class infrastructure are now inspection-only for new mutations in both UI and elevated helper; journal-owned Restore remains available;
- a generic WDF stack-attribution/optimizer layer was considered to explain every remaining hotspot. **Decision:** do not build it preemptively; use existing module evidence and exact xHCI routing first, then add deeper attribution only for an unresolved material hotspot;
- a generic cross-subsystem weighted allocator was considered for GPU/xHCI/NIC/audio. **Decision:** do not couple benchmark results. Use one simple read-only CPU-reservation snapshot derived from current explicit device policies, then let each subsystem benchmark its own remaining candidates independently. No weighted/Pareto allocator is justified.

This reconciliation is a planning/scope change only. Current source exposes GPU Gate A and USB/xHCI as separate subsystem actions; a later orchestrator may sequence them without merging their authority. Public mutation remains unarmed.

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
- Raw Input → USB hub/port → xHCI read-only topology and input-host timing source;
- read-only NIC/RSS, audio and storage diagnostics remain available without automatic mutation;
- speculative network benchmark/profile/Pareto optimizer source has been removed from the v1 code path.

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
9. Stage A gives every eligible logical CPU one real local paired screen; Full search no longer infers an untested SMT sibling from a physical-core representative.
10. Stage B rechecks the observed top four logical CPUs whenever available and admits at most one additional uncertainty-overlapping fifth challenger.
11. Only repeat work after the exhaustive first pass is adaptive; no eligible logical CPU is pruned before receiving decision-grade first-pass evidence.
12. Scored observer-active trials use a bounded unscored 2–4 s startup settle with a 500 ms quiet tail; warm-ups without external observers skip that cost.
13. Frozen CPU workers use exact physical-core affinity masks persisted in workload identity and verified against captured topology.
14. Only the top two CPUs advance to finalist confirmation.
15. Finalists receive two deterministically shuffled 15 s local pairs; one third 15 s round is added only while their lead remains inside measured uncertainty.
16. Every structurally valid finalist is ranked by median paired 1%-low effect.
17. MAD, Original variability, lead over runner-up, pair consistency and practical-tie state produce `High`/`Medium`/`Low` **ranking confidence**; confidence never gates rank or Keep.
18. `RecommendedForKeep` is separate: positive median benefit, positive-pair consistency and bounded AVG/frame-p99/interrupt-tail guardrails. Rank 1 remains best-observed, while final Keep tries guardrail-safe finalists in rank order so a safe runner-up is not discarded merely because rank 1 failed a guardrail.
19. Final Keep requires exact stored state, non-empty translated GPU interrupt resources confined to the requested mask, and clean **direct display-driver** target-only GPU ISR placement. A failed final-placement attempt restores exact Original before the next guardrail-safe finalist may be tried; if none passes, Original remains active while the best-observed CPU remains in the report.

The v6 method keeps the existing v3 report schema and persists raw trials/pairs, finalist medians, effect MAD, positive-pair count, noise guide, raw median Original/Candidate FPS/ms, `BestObservedProcessor`, `SelectionConfidence`, structured finalist guardrail reasons, provenance and terminal state. Historical v5 and earlier evidence is never reinterpreted as v6.

### Diagnostic scopes

The developer UI provides:

- **Full search** — the authoritative restart-canonicalized observer-isolated adaptive v6 machine search.
- **Selected CPUs · restore Original** — real paired screening for an exact subset; diagnostic-only, no finalist Keep, always restores Original, cannot close Gate A.
- **Original only · no system changes** — five 10 s Original observations with no affinity mutation or device restart.

CPU selection uses current topology/CPU-set eligibility; unsupported topology fails safely. The diagnostic scope selector remains a WinUI `ContentDialog`; the manual interrupt-affinity tool is an independent WinUI window.

### Development Interrupt Policy Lab

The Devices page now exposes a development-only manual affinity surface without changing the public observation-only Service contract. Its availability is independent of the GPU Gate A predicate; the two surfaces share internal helper implementation only to avoid duplicating privileged/recovery plumbing:

- the development UI keeps the familiar Interrupt-Affinity Policy Configuration Tool information model, but now opens as an independent modern WinUI tool window with an adaptive device master/detail layout, a default-on **Supported only** filter (journal-owned recovery rows remain visible), theme-matched title chrome, concise advanced identity details, Fluent device/action icons, physical-core-grouped multi-CPU selection, and Current policy / Specified mask / Current assignment evidence;
- current stored interrupt policy / `AssignmentSetOverride` and translated allocated interrupt-resource masks are shown separately for latency-sensitive present devices;
- the development affinity tool exposes documented Windows group-0 interrupt-affinity editing only for policy-eligible rows; storage-class and Windows System-class infrastructure rows are inspection-only for new changes, while any journal-owned prior change remains restorable. Current ConfigMgr allocation visibility does not gate editing for otherwise eligible devices;
- GPU and USBXHCI retain stronger subsystem-specific runtime ISR verification capability. Generic devices remain policy-only when ConfigMgr allocation is unavailable. Manual GPU still attempts direct display-driver ETW verification in that case; it may claim runtime placement only when the stored mask is stable and all attributable GPU ISR events stay inside the requested mask. Otherwise it remains policy-only. Contradictory readable allocation/ISR evidence remains rollback-authoritative;
- recovery compatibility is retained only so an HDAudio MSI experiment journaled by the superseded development build can still Restore its exact original state; no new HDAudio MSI Apply/Keep is exposed;
- manual Apply accepts a non-empty group-0 KAFFINITY set and uses an elevated one-shot helper: exact snapshot → journal → store policy → activation/restart handling → re-read stored policy → verify translated allocation when observable → full verified Keep, policy-only Keep when active allocation is unavailable, or exact rollback on contradictory readable evidence. Pending ApplyRebootPending candidates resume the same journal-owned mask instead of being converted into Restore;
- a **fully runtime-verified** GPU manual result requires direct display-driver ISR attribution; shared `dxgkrnl` fallback is diagnostic-only. Missing translated allocation no longer skips ETW: a clean direct-driver target-mask-only capture can independently verify runtime placement, while missing/inconclusive ETW leaves the explicit policy retained but runtime-unverified. xHCI keeps target translated allocation as the minimum Keep authority; controller-specific ETW is stronger optional proof. If an in-place xHCI restart leaves allocation unavailable or structurally unusable, the same candidate gets one full-reboot verification attempt before rollback rather than being mislabeled as contradictory;
- the helper keeps `AudioMsi` parsing only for Restore/recovery compatibility; `IRQ_DES_64.IRQD_Flags` is not treated as proof of message-signaled delivery;
- reboot-pending xHCI, generic device-affinity and manual GPU experiments resume only through the same journal-owned target/candidate. The App reconstructs the pending KAFFINITY mask from the journal so the selected CPU remains visible after restart; Restore original stays a separate explicit action;
- Restore is per-device and only unwinds retained changes actually owned by the LatencyPilot mutation journal; an external pre-existing override is never claimed or overwritten as LatencyPilot-owned;
- the App blocks manual mutation while GPU Gate A is running.

This lab does **not** arm `ServiceBoundary.MutationAvailable` or widen the automatic v1 path to NIC/audio/storage tuning. Generic manual affinity remains an explicit development-only IntPolicy-style per-device action constrained to present PnP devnodes, the documented Windows affinity-policy values and durable ownership. For NICs, this edits device interrupt affinity only; RSS remains a separate network processor policy.

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

### Independent USB/xHCI action

The Devices page now owns an independent USB/xHCI workflow rather than receiving a recommendation from GPU Gate A:

```text
select one exact primary USB mouse
→ resolve Raw Input → USB hub/port → exact xHCI controller
→ inspect journal-owned pending xHCI state and resume the same candidate when required
→ capture current explicit device-policy CPU reservations
→ exclude every reserved physical core except the current xHCI target
→ three elevated 5 s quiet kernel ETW headroom windows
→ choose a majority-winning physical core, then the quieter logical sibling by median evidence
→ show a subsystem-owned benchmark result even when peer-controller allocation is unreadable
→ evaluate controller-attribution strength separately
→ when the recommendation is Ready and no unrelated mutation owns the machine:
   journal/apply the recommended xHCI mask through the existing bounded helper
→ require usable target translated allocation; add controller-specific requested-mask-only ISR proof when attribution is available
→ if in-place restart leaves allocation unavailable/structurally unusable, defer the same journaled mask to one full reboot
→ Keep, exact rollback, or durable reboot-pending resume
```

USB/xHCI no longer consumes a GPU report or special GPU reservation object. Any present device with a readable explicit specified-processor policy contributes its current mask to the reservation snapshot, and USB excludes the corresponding physical cores before ranking. The current xHCI target is excluded from reservation discovery so its own prior policy does not hide candidates while it is being benchmarked.

The controller-specific runtime-placement verifier supports one present `USBXHCI` service instance and same-service multi-controller systems. It prefers device-associated IRQ-vector ownership from `Win32_PnPAllocatedResource → Win32_IRQResource` matched against ETW ISR `Vector`; when target and peers have disjoint vector sets, a peer ConfigMgr allocation failure no longer blocks Apply. Allocation-disjoint attribution remains the fallback, and shared/unknown ownership stays fail-closed. Non-vector attribution with unresolved ISR module events is downgraded instead of being presented as target-only proof; unique device-associated vector ownership can still attribute the vector even when the module path is unresolved. Target translated allocation remains the minimum Keep authority. If a healthy in-place restart cannot expose usable target allocation, the journaled candidate is deferred to one full reboot and then either verifies or restores the exact original state; unreadable/structurally unusable descriptors are not mislabeled as an out-of-mask contradiction. Raw Input timing remains bounded host-observable sanity evidence and never replaces placement authority. Public product mutation is still unarmed; this is the development/physical-validation path for the independent USB surface.

## Verification state

### Hosted software verification

The v6 work reuses the consolidated critical-test budget. New/updated contracts specifically guard:

- noise cannot stop the search after two candidates;
- high drift gets one retry but a structurally valid retry remains rankable;
- Original variability uses median/MAD rather than quiet-cluster cherry-picking;
- best-observed CPU persists independently from Keep/Restore;
- selection confidence is metadata rather than a winner threshold;
- result presentation includes actual before/after FPS/ms plus absolute and percentage improvement;
- final GPU Keep requires stored policy + active translated allocation + direct display-driver target-only ISR placement, while shared `dxgkrnl` fallback stays diagnostic-only;
- direct GPU ISR attribution remains usable on multi-adapter systems only when the target driver service is unique; shared-service direct attribution and multi-adapter `dxgkrnl` fallback remain fail-closed.

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
4. every eligible logical CPU has persisted first-pass paired evidence, and the top-four-plus-optional-fifth adaptive recheck shortlist matches persisted evidence;
5. scored observer-active trials reach the bounded quiet pre-score boundary and warm-ups do not pay observer-settle overhead;
6. persisted worker affinity masks exactly match physical-core topology and stay frozen across Original/Candidate controls;
7. pair math and raw controls reconstruct from persisted evidence;
8. high-drift retry evidence remains visible while valid candidates remain ranked;
9. the top two finalists receive two 15 s observations, with a third only when uncertainty remains, and persisted median/MAD authority matches the rank;
10. `BestObservedProcessor` and ranking confidence agree with report/UI even when Original is restored;
11. actual before/after FPS/ms and paired percentage effect render correctly;
12. Keep guardrails remain separate from rank and the concrete failed guardrail is shown when a best-observed CPU is not kept;
13. exact rollback succeeds between candidates and on failure/cancellation;
14. translated GPU interrupt resources remain inside the requested CPU mask and final ETW proves **direct display-driver** target-only GPU ISR placement before Keep; shared `dxgkrnl` fallback cannot close this gate;
15. terminal state verifies with `unresolved=0`;
16. **Stop safely** and one supported failure/recovery path restore exact Original;
17. a second full v6 search produces comparable ranking/confidence behavior without requiring identical decimals;
18. real Windows result UI is inspected for theme/text-scale/keyboard/accessibility behavior.

Graphics-hook warnings such as `nvspcap64.dll` remain explicit interference context, not proof of causation. Dirty runs remain development evidence only. Public mutation remains unarmed until physical Gate A passes.

## Roadmap snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Keep exact-head verification current |
| 1 Preflight | **Most primitives exist** | Per-subsystem quiet/preflight UX + explicit handoff rules |
| 2 Baseline | **ETW engine exists** | Wire comparable baseline evidence into the applicable subsystem actions |
| 3 GPU search | **Restart-canonicalized observer-isolated adaptive v6 source/result contracts implemented** | Exact-head CI + physical v6 Gate A + repeat + recovery/render inspection |
| 4 GPU Keep | **Internal verified-Keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Independent readiness/recommendation action implemented** | Representative physical evidence |
| 6 USB apply | **Independent development Apply/verify/rollback wired to bounded xHCI substrate** | Physical apply/verify/rollback evidence + product arming |
| 7 Reboot verify | **Recovery/reboot primitives + independent USB resume wired** | Physical reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Comparable per-subsystem final capture/report |
| 9 UX/release | **Separate GPU / USB / Network development surfaces implemented** | Real render/accessibility + product arming + release closure |

## Immediate execution ladder

1. **Completed source work:** GPU Gate A v6 now gives every eligible logical CPU a paired first-pass screen, keeps best-observed rank separate from retained CPU, and can try the highest-ranked guardrail-safe finalist for final placement verification.
2. **Hosted software evidence rule:** only a green **Tests** workflow on the exact final HEAD is current evidence. Older green runs remain historical and are never promoted to a newer SHA; hosted success proves software contracts only and never substitutes for physical GPU/xHCI evidence.
3. **Scope decision completed:** v1 keeps GPU and exact primary-input xHCI as the mutation-capable domains, but exposes them as separate subsystem actions. NIC/RSS has its own read-only action; storage remains diagnostics-only; audio remains diagnostic/conditional; no generic WDF optimizer or cross-subsystem allocator is added.
4. **Source hardening completed:** storage/System-class new mutations are blocked in UI, helper and the lowest generic write boundary; actual PnP target kind owns GPU/xHCI/generic verification routing; xHCI runtime verification supports single-controller, device-associated IRQ-vector, and allocation-disjoint same-service multi-controller attribution while keeping shared/unknown ownership fail-closed. GPU Gate A is now GPU-only; USB/xHCI route/controller evidence and Network/RSS evidence have independent Devices-page actions. The elevated development xHCI verification path can capture bounded host-observable Raw Input report timing concurrently with ETW when exactly one mouse route owns that controller; this is diagnostic sanity evidence only, never click-to-photon evidence and never a substitute for controller-attributed ISR placement.
5. **Still open for v1:** one clean physical v6 Full GPU Gate A run, whole-search repeatability, Stop/recovery/render inspection, representative independent xHCI Apply/runtime + Raw Input sanity evidence on real hardware, then public typed mutation arming, final per-subsystem before/after UX and release closure. These are intentionally gated; no public/normal-user mutation surface is armed ahead of that evidence.
6. **Completed cleanup:** storage-class and Windows System-class infrastructure rows are inspection-only in the manual tool and elevated helper; recovery of older journal-owned state remains supported.
7. **Only after v1 closure:** consider a bounded physical-NIC/RSS **mutation** experiment. The v1 Network/RSS action remains read-only; mutation is not authorized until a separate ADR/methodology and physical evidence make an RSS-aware experiment worthwhile.


## Completion rule

Repository/source completion means every current v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, independent xHCI apply/verify/reboot-resume evidence, final per-subsystem before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.
