# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-29

`PROJECT_STATUS.md` owns current execution/evidence state. This file owns required product outcomes. Source-complete and physically validated are different claims.

## Product definition — v1

LatencyPilot v1 exposes narrow, reversible **subsystem-scoped actions** rather than one umbrella benchmark button:

```text
GPU Gate A
→ GPU-only benchmark / candidate search / Keep-or-Restore / runtime verification

USB / xHCI
→ independent input-route + controller evidence
→ independent xHCI readiness / selection / verification flow

Network / RSS
→ independent read-only evidence in v1
→ no GPU-style single-CPU mutation contract
```

A later product-level "Optimize all" orchestration may sequence already-proven subsystem actions, but it must call those actions as separate authorities rather than hiding them behind GPU Gate A. Product/safety authority remains ADR 0006. GPU measurement/search/ranking authority is ADR 0011; subsystem-surface separation is ADR 0012. GPU ranking is never reused as a USB ranking, and v1 has no joint weighted/Pareto allocator.

### v1 includes

- processor/device/USB topology discovery;
- ETW DPC/ISR measurement and per-CPU/module attribution;
- deterministic D3D12 GPU benchmark;
- understandable AVG FPS / 1% low / 0.1% low / p99 evidence;
- safe GPU interrupt-affinity mutation, restart and rollback;
- paired local-control GPU search;
- Raw Input → USB → xHCI route resolution;
- per-CPU interrupt-headroom analysis for input/xHCI placement;
- reversible xHCI/controller affinity after the shared mutation substrate is physically proven;
- one-reboot post-apply verification;
- before/after evidence and Restore original settings;
- non-elevated App with a narrow privileged boundary.

### Not in the v1 automatic path

NIC/RSS mutation, audio/storage affinity, BIOS changes, HAGS changes, MSI-mode forcing, power-plan/timer/HPET/processor-performance changes, mouse/keyboard `DataQueueSize` tuning, automatic polling-rate changes, PCI bridge/root-complex affinity, generic debloating and a generic cross-subsystem/Pareto optimizer.

A development-only **Interrupt Policy Lab**, independent of GPU Gate A, may inspect and set the documented interrupt-affinity policy for present PnP devices, matching the intent of Microsoft's former IntPolicy workflow. It may reuse the existing development helper executable/project, but it does not inherit Gate A closure authority or source-state semantics. Current allocated-resource visibility does not gate policy editing. GPU and USBXHCI have subsystem-specific runtime verification; xHCI remains fail-closed, while manual GPU may retain an explicit policy-only state when Windows does not expose translated allocation, clearly without an active ISR-placement claim. Other generic manual targets likewise verify translated allocation whenever Windows exposes it and otherwise remain explicitly policy-only. HDAudio MSI is not an editable target; recovery-only parsing may remain solely to restore exact journal-owned MSI state created by a superseded development build. NIC RSS remains a separate mechanism and is not silently rewritten by this control. **Storage-class devices and Windows System-class infrastructure (including buses/bridges/ACPI) are not product tuning targets.** The development picker and elevated helper now block new mutations for those classes while preserving journal-owned Restore. None of these development actions widen the automatic v1 scope or public privileged API. The automatic GPU search continues to evaluate one logical-CPU candidate at a time.

### Research-reconciled expansion policy

The 2026-09-28 cross-check against Windows interrupt-affinity/RSS semantics, maintained community tools and field reports changes the expansion order, not the current v1 scope:

1. **Finish v1 before adding another optimizer.** GPU and exact primary-input xHCI remain the only mutation-capable v1 domains, exposed as separate subsystem actions. A later orchestrator may sequence them without merging their authority.
2. **Physical NIC/RSS mutation is the first post-v1 candidate, not a v1 requirement.** v1 already exposes a separate read-only Network/RSS evidence action for physical-NIC identity, RSS capability/profile/queue count, current RSS processor set and MSI/MSI-X context. Device interrupt affinity and RSS steering are separate mechanisms and must remain separate in the model.
3. **Do not create a GPU-style single-CPU NIC tournament.** RSS is intentionally multi-CPU and can align MSI-X messages with receive queues. Any future network mutation needs its own ADR/methodology and must compare a small bounded RSS-aware configuration set under controlled local/kernel evidence.
4. **Do not use public Internet speed tests as decision authority.** Kernel/NDIS counters, ETW/per-CPU interrupt evidence and, when available, a controlled local peer are the acceptable evidence sources. Existing checksum/LSO/RSC/interrupt-moderation defaults are not blindly disabled; each has workload-dependent tradeoffs.
5. **Storage stays diagnostics-only.** Inventory, driver identity and DPC/ISR evidence may be reported, but LatencyPilot will not automatically or manually tune NVMe/SATA/Storport affinity, MSI/MSI-X layout or queue topology.
6. **Audio remains diagnostic/conditional.** USB audio is first reasoned about through its owning USB/xHCI route. PCIe/HDAudio gets no automatic affinity path unless a future device-specific verifier and physical evidence justify one.
7. **Do not build a generic WDF/driver-stack optimizer now.** Reuse existing ETW module attribution and exact xHCI routing first. Add deeper stack attribution only if a material WDF hotspot remains unexplained after the narrow v1 path.
8. **Keep allocation simple.** Every subsystem reads the same current explicit-policy CPU reservations and skips those physical cores in its own benchmark. Do not pass benchmark winners between subsystems and do not add a weighted score or Pareto allocator merely to anticipate NIC/audio work.

Before any post-v1 NIC mutation is implemented, a dedicated ADR and measurement contract must define snapshot/restore, RSS-aware candidate constraints, primary metrics, guardrails and physical exit evidence.

---

## Phase 0 — Scope, safety and recovery foundation

**State: SOURCE COMPLETE**

- [x] purpose/non-goals and architecture boundaries;
- [x] source-available license/contribution/security rules;
- [x] `AGENTS.md` engineering contract;
- [x] typed observation protocol with public mutation unarmed;
- [x] durable SQLite mutation journal and recovery ownership;
- [x] exact original-state snapshot/restore semantics;
- [x] GPU measurement/ranking authority separated from broader product/safety authority;
- [x] NIC/audio/generic cross-subsystem tuning removed from the v1 critical path.
- [x] Network/RSS action filters to physical PnP-correlated RSS-capable NICs and uses existing miniport attribution + continuity checks for a bounded read-only runtime sample.

### Exit gate

The v1 workflow, safety boundary and rollback owner are unambiguous without chat history. **SOURCE SATISFIED.**

---

## Phase 1 — Preflight and exact snapshot

**State: MOST SOURCE EXISTS; PER-SUBSYSTEM PREFLIGHT UX OPEN**

Available:

- [x] processor-group-aware physical/logical/SMT topology;
- [x] current CPU-set eligibility evidence;
- [x] PnP GPU/input/USB/xHCI inventory primitives;
- [x] driver/version and Windows/source provenance;
- [x] exact GPU affinity stored-state snapshot;
- [x] ambiguity/fail-closed device identity contracts;
- [x] unresolved journal ownership blocks unsafe follow-on mutation.

Remaining:

- [ ] per-subsystem preflight/status surface for GPU and USB/xHCI;
- [ ] quiet/background-load check with actionable warning rather than killing user apps;
- [x] generic CPU-reservation snapshot from current explicit device policies; no benchmark-result handoff between GPU and USB/xHCI;
- [ ] explicit pending-reboot/multi-GPU/unsupported-topology user-facing stop reason.

---

## Phase 2 — Baseline interrupt evidence

**State: MEASUREMENT ENGINE EXISTS; PER-SUBSYSTEM DEEP BASELINE INTEGRATION OPEN**

Available:

- [x] bounded kernel ETW DPC/ISR capture;
- [x] per-CPU DPC/ISR counts;
- [x] DPC/ISR duration distributions and module attribution;
- [x] integrity/lost-event accounting;
- [x] read-only evidence export/provenance;
- [x] existing repeated steady baseline product.

Remaining:

- [ ] wire the deep baseline into the applicable subsystem actions without creating a shared hidden score;
- [ ] persist before values needed by final comparison: per-CPU DPC/ISR counts, total duration, tail duration and module attribution;
- [ ] surface quiet-condition warnings without making background-app closure a blind hard requirement.

LatencyMon is not a dependency; LatencyPilot uses its own ETW evidence.

---

## Phase 3 — Automatic GPU core search

**State: RESTART-CANONICALIZED OBSERVER-ISOLATED V6 SOURCE + HOSTED CONTRACTS IMPLEMENTED; PHYSICAL PROOF OPEN**

Current source workflow:

```text
capture exact Original/default GPU affinity
→ verify Original and perform one in-place GPU restart with the policy unchanged
→ re-verify Original + driver identity and recreate the renderer
→ 5 s non-scored Original warm-up
→ collect 3 scored 10 s Original observations
→ if robust median/MAD variability is high, extend to at most 5 observations
→ noise lowers confidence; it does not block candidate search
→ capture fresh 10 s Original control O0
→ Stage A: screen every eligible logical processor with a real Original-before → Candidate → Original-after local pair
→ local effect uses geometric mean of adjacent Original controls
→ one retry for high local drift
→ a still-noisy but structurally valid retry remains rankable
→ Stage B: retain the observed top 4 logical CPUs when available + at most one uncertainty-overlapping fifth challenger for one additional pair
→ give every shortlisted CPU one additional 10 s local pair
→ advance the top two by median short-screen effect
→ give both finalists 2 shuffled 15 s local pairs
→ add one third 15 s round only while their lead remains inside measured uncertainty
→ rank every structurally valid finalist by median paired 1%-low effect
→ median/MAD + lead + pair consistency produce High/Medium/Low confidence
→ practical tie lowers confidence but does not erase rank 1
→ separate Keep guardrails identify guardrail-safe finalists without changing best-observed rank
→ try guardrail-safe finalists in rank order; final clean target-only ETW ISR-placement proof is mandatory for Keep
→ otherwise exact RestoreOriginal while preserving best-observed result
```

Source checklist:

- [x] D3D12 benchmark and controlled wall-period AVG / 1% / 0.1% / p99 statistics;
- [x] candidate generation from actual Windows topology/CPU-set evidence with CPU0 allowed;
- [x] 3–5 Original observations with robust median/MAD variability;
- [x] exact 10 s screening/recheck and 15 s finalist durations;
- [x] external-observer startup is isolated in a bounded unscored 2–4 s settle with a 500 ms quiet tail before scored QPC timing;
- [x] benchmark CPU workers use frozen physical-core affinity masks instead of fixed first-SMT-sibling pinning, with masks persisted and topology-verified;
- [x] direct `Original before → Candidate → Original after` pair evidence;
- [x] geometric-mean local reference and signed paired effects;
- [x] one bounded high-drift retry without a noise-only candidate/search abort;
- [x] exhaustive Stage-A first-pass paired screening of every eligible logical CPU;
- [x] bounded uncertainty-aware recheck of the observed top four whenever available plus at most one uncertainty-overlapping fifth challenger;
- [x] top-two finalist confirmation uses two shuffled 15 s pairs, with one third round only when uncertainty remains;
- [x] median paired ranking + effect MAD + positive-pair consistency;
- [x] `BestObservedProcessor` persisted independently from terminal Keep/Restore state;
- [x] `SelectionConfidence` persisted as explanatory metadata, never a rank gate;
- [x] practical ties remain explicit while rank 1 remains the best observed estimate;
- [x] separate bounded AVG/frame-p99/interrupt-tail Keep guardrails, with best-observed rank kept distinct from the actually retained CPU;
- [x] guardrail-safe finalists are attempted in rank order, with exact Original restored between failed final-placement attempts;
- [x] final Keep requires clean attributable target-only GPU ISR placement;
- [x] exact rollback between candidates and on failure/cancellation;
- [x] result UX exposes actual Original → Candidate FPS/ms, absolute gain and paired percentage effect;
- [x] custom selected-CPU diagnostic always restores Original;
- [x] Original-only diagnostic performs no affinity mutation/restart;
- [x] historical v1/v2/v3/v4/v5 evidence is not reinterpreted as v6;
- [ ] one exact clean green physical Gate A run on owner hardware using v6;
- [ ] repeat the whole v6 search for practical reproducibility;
- [ ] Stop safely + supported failure/recovery physical exercise;
- [ ] rendered/taskbar/keyboard/accessibility inspection of the result surface.

### GPU physical Gate A

Gate A closes only when one exact clean green revision proves on supported hardware:

1. structurally valid Original evidence continues through real-world variability and records robust noise instead of failing only for variance;
2. every eligible logical CPU receives a real first-pass paired screen;
3. only repeat work is adaptive: the top-four recheck (plus optional fifth uncertainty challenger) preserves plausible noisy near-leaders without sending every CPU to finalist confirmation;
4. every ranked candidate has reconstructable adjacent Original controls and pair math;
5. high-drift retry evidence stays visible and does not erase an otherwise valid candidate;
6. the top two finalists receive two shuffled 15 s pairs, with one additional 15 s round only when their lead remains inside measured uncertainty;
7. best-observed CPU, confidence, raw before/after values and terminal Keep/Restore state agree across report and UI;
8. Keep guardrails remain separate from ranking;
9. exact rollback occurs between candidate activations and on failure/cancellation;
10. final ETW proves target-only GPU ISR placement before Keep;
11. terminal stored state verifies with zero unresolved journal ownership;
12. Stop safely plus one supported failure path restore exact Original;
13. a repeated whole search is practically reproducible or reports lower confidence honestly;
14. real Windows rendering/accessibility is inspected with the actual report/evidence bundle.

Public mutation IPC remains unarmed until this physical gate passes.

---

## Phase 4 — Keep GPU winner and product mutation boundary

**State: INTERNAL FINAL-KEEP SOURCE IMPLEMENTED; PRODUCT ARMING GATED**

- [x] internal winner apply and exact stored-state verification;
- [x] final ETW target-only ISR placement mandatory for Keep;
- [x] failure/cancellation rollback preserves exact original-state ownership;
- [ ] Gate A physical proof;
- [ ] typed allowlisted mutation-specific Service IPC after Gate A;
- [ ] App → Service physical mutation authorization proof;
- [ ] normal-user GPU mutation arming only after those gates.

---

## Phase 5 — Automatic USB/input CPU selection

**State: INDEPENDENT READINESS/RECOMMENDATION ACTION IMPLEMENTED; PHYSICAL EVIDENCE PENDING**

- [x] independent USB/xHCI Devices-page action owns explicit primary-mouse selection; multiple exact mouse routes require an explicit user choice rather than heuristic substitution;
- [x] PnP ancestry;
- [x] USB hub/port correlation;
- [x] exact xHCI controller identity;
- [x] Raw Input host timing metrics;
- [x] candidate-aware xHCI ISR runtime-placement verifier with fail-closed shared-controller disambiguation;
- [x] independent elevated quiet ETW headroom capture owned by the USB/xHCI action;
- [x] derive reservations from actual current specified-processor device policies, regardless of whether they were set manually or by another LatencyPilot subsystem;
- [x] keep subsystem benchmarks independent: no GPU-result prerequisite or benchmark-result handoff;
- [x] stabilize USB/xHCI selection with three short windows, physical-core majority voting and median sibling selection;
- [x] exclude every reserved physical core, including SMT siblings, while excluding the current benchmark target from its own reservation snapshot;
- [x] rank CPU headroom by total DPC+ISR duration, then p99 interrupt tail, then event-count context;
- [x] bind recommendation to the exact interrupt-owning xHCI controller;
- [x] on shared-service multi-xHCI systems, exclude CPUs that overlap readable peer-controller translated allocation and fail closed when peer allocation is unavailable;
- [x] independent USB result renders controller/CPU/reason without modifying or extending GPU Gate A reports;
- [ ] representative physical input/xHCI evidence;
- [ ] normal-user rendering in the integrated product workflow after physical arming gates.

---

## Phase 6 — USB/xHCI apply and safety check

**State: INTERNAL REVERSIBLE MUTATION SUBSTRATE IMPLEMENTED; PRODUCT/PHYSICAL GATED**

- [x] bounded reversible xHCI/controller-affinity mutation source;
- [x] exact stored-state snapshot and durable journal integration;
- [x] restart-required/reboot-pending state plus exact rollback/recovery source;
- [x] fail-closed controller-specific ETW runtime-placement verifier primitive for either one present `USBXHCI` service instance or multiple same-service controllers whose translated allocations are all readable and disjoint from the requested mask; unknown/overlapping peer allocation remains ambiguous;
- [x] candidate-aware xHCI runtime verification is integrated into the elevated mutation substrate, including exact rollback on failed verification;
- [x] the independent USB/xHCI development action calls that same bounded Apply/verify/rollback substrate only after a Ready recommendation;
- [x] reboot-pending xHCI state is detected from the durable journal and resumed with the exact owned mask instead of planning a new candidate;
- [x] development xHCI verification captures a bounded host-observable Raw Input timing sanity sample concurrently with ETW when one exact mouse route owns the controller; ambiguous/no-mouse routes remain explicitly unavailable and never weaken the ETW Keep gate;
- [x] USB verification failure rolls back only the xHCI-owned state and does not rewrite a separately retained GPU state;
- [ ] representative high-polling hardware physical evidence;
- [ ] public typed product mutation arming after the physical gates close.

---

## Phase 7 — One reboot and post-login verification

**State: DEVICE-INTERRUPT REBOOT/RECOVERY PRIMITIVES IMPLEMENTED; INDEPENDENT USB RESUME WIRED**

- [x] durable journal survives interruption/restart;
- [x] recovery re-reads actual machine state;
- [x] GPU restart/reboot-required detection primitives;
- [x] generic device-interrupt apply/rollback reboot-pending + resume source;
- [x] independent USB/xHCI UI detects an owned ApplyRebootPending controller candidate and resumes the exact journal mask;
- [ ] physically verify the independent xHCI reboot/resume path;
- [ ] public product reboot/resume UX after mutation arming;
- [ ] restore only the subsystem whose owned state cannot be verified, preserving independently proven state elsewhere.

---

## Phase 8 — Final before/after evidence

**State: METRIC/REPORT PRIMITIVES EXIST; INTEGRATED V1 REPORT OPEN**

- [ ] repeat like-for-like deep ETW baseline conditions after optimization;
- [ ] short GPU verification benchmark;
- [ ] show GPU CPU, AVG FPS, 1% low, 0.1% low and p99 context where comparable;
- [ ] show xHCI CPU, DPC/ISR counts, total/tail durations and Raw Input host timing where comparable;
- [ ] distinguish raw observations, paired decision evidence and verified terminal state;
- [ ] never label an unmeasured proxy as click-to-photon or network latency;
- [ ] export provenance and verification state with the result.

---

## Phase 9 — Normal-user v1 UX and release

**State: SEPARATE DEVELOPMENT SUBSYSTEM SURFACES + RELEASE FOUNDATIONS EXIST; PRODUCT ARMING OPEN**

Target normal-user surface:

```text
GPU
Run GPU optimization
→ GPU-only progress/result/Keep-or-Restore

USB / xHCI
Run USB/xHCI optimization
→ exact input route → headroom → Apply/verify/rollback

Network / RSS
Analyze network / RSS
→ physical NIC only → RSS/MSI-X/queues/processors → 5 s miniport DPC/ISR + continuity → read-only evidence in v1

Restore original settings
```

A later optional `Optimize all` action may sequence already-proven subsystem actions, but each action still discovers reservations from machine state and remains independently runnable. The orchestrator must not pass hidden winner/provenance state from one benchmark into another.

- [x] non-elevated WinUI shell;
- [x] development GPU Gate A progress and safe-stop experience;
- [x] Gate A source-state UX shows Evidence-ready / Development only / Blocked;
- [x] selected-CPU and Original-only developer diagnostic scopes;
- [x] validated Gate A sessions package a shareable evidence ZIP;
- [x] Overview receives authority-selected Gate A result presentation instead of auto-opening raw JSON;
- [x] result surface includes direct pair evidence, candidate/finalist comparison, decision evidence and explicit evidence actions;
- [x] self-contained Windows 11 x64 App/Service release source;
- [x] install/upgrade/uninstall recovery checks and signing hooks;
- [ ] real Windows render inspection in light/dark/high-contrast and text scaling;
- [ ] arm typed normal-user GPU and USB mutation actions after physical gates close;
- [ ] concise per-subsystem before/after result UI;
- [ ] prominent Restore original settings;
- [ ] physical accessibility/keyboard pass;
- [ ] signed package, clean-machine install, upgrade/uninstall and recovery validation.

---

## Post-v1 / future work

Existing NIC/RSS, profile/Pareto and other experimental source is retained as future/read-only capability, but it does not gate v1:

- NIC/RSS automatic mutation and network experiments;
- audio interrupt affinity;
- cross-subsystem/Pareto automatic optimizer;
- workload-profile-driven multi-subsystem tuning.

Any future mutation must independently satisfy the same evidence, attribution and rollback bar. Generic tweak packs, security weakening and folklore-only changes remain out of scope.

---

## Permanent-test policy

Permanent tests remain deliberately bounded. Extend an existing high-blast-radius contract when a stable invariant needs coverage; temporary characterization may be created and removed during implementation. Do not create a new permanent test family merely to mirror implementation details.

Hosted CI proves software contracts only. It does not close physical Gate A or substitute for Windows hardware/render/recovery evidence.

## Completion rule

Repository/source completion means every v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, independent xHCI apply/verify/reboot-resume evidence, final per-subsystem before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.