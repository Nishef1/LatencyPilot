# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-21

`PROJECT_STATUS.md` owns current execution/evidence state. This file owns required product outcomes. Source-complete and physically validated are different claims.

## Product definition — v1

LatencyPilot v1 automates one narrow, reversible Windows 11 workflow:

```text
preflight / quiet check
→ baseline DPC/ISR evidence
→ benchmark GPU interrupt affinity across every eligible logical CPU
→ time-local Original controls + normalized decision evidence
→ bounded finalist confirmation
→ final runtime GPU ISR-placement verification
→ measure remaining per-CPU interrupt headroom
→ resolve the primary input device to its exact xHCI controller
→ choose/apply a separate USB/xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence + Restore original settings
```

The manual inspiration is AutoGpuAffinity + LatencyMon/ETW + Interrupt Affinity Policy Tool, but LatencyPilot replaces manual device matching and unsafe guesswork with Windows topology, ETW verification, journal-owned rollback and explicit uncertainty.

### v1 includes

- processor/device/USB topology discovery;
- ETW DPC/ISR measurement and per-CPU/module attribution;
- deterministic D3D12 GPU benchmark;
- understandable AVG FPS / 1% low / 0.1% low / p99 evidence;
- safe GPU interrupt-affinity mutation, restart and rollback;
- automatic GPU logical-CPU search with time-local controls;
- Raw Input → USB → xHCI route resolution;
- per-CPU interrupt-headroom analysis for input/xHCI placement;
- reversible xHCI/controller affinity after the shared mutation substrate is physically proven;
- one-reboot post-apply verification;
- before/after evidence and Restore original settings;
- non-elevated App with a narrow privileged boundary.

### Not in the v1 automatic path

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a generic cross-subsystem/Pareto auto-optimizer. Existing read-only/future or internal safety source may remain, but it must not complicate or silently expand the v1 automatic path.

Canonical current decision: `docs/adr/0006-simple-auto-interrupt-affinity-v1.md`.

---

## Phase 0 — Scope, safety and recovery foundation

**State: SOURCE COMPLETE; PRODUCT-SCOPE RECONCILIATION COMPLETE**

- [x] purpose/non-goals and architecture boundaries;
- [x] source-available license/contribution/security rules;
- [x] `AGENTS.md` engineering contract;
- [x] typed observation protocol with public mutation still unarmed;
- [x] durable SQLite mutation journal and recovery ownership;
- [x] exact original-state snapshot/restore semantics;
- [x] current v1 scope removes ABBA/BAAB and fixed Original-vs-candidate winner thresholds;
- [x] SMT siblings are screened directly rather than handled by a separate refinement phase;
- [x] NIC/audio/cross-subsystem automatic tuning removed from the v1 critical path.

### Exit gate

The v1 workflow, safety boundary and rollback owner are unambiguous without chat history. **SOURCE SATISFIED.**

---

## Phase 1 — Preflight and exact snapshot

**State: MOST SOURCE EXISTS; INTEGRATED V1 PREFLIGHT OPEN**

Available:

- [x] processor-group-aware physical/logical/SMT topology;
- [x] PnP GPU/input/USB/xHCI inventory primitives;
- [x] driver/version and Windows/source provenance;
- [x] exact GPU affinity stored-state snapshot;
- [x] CPU-set eligibility handling without even/odd assumptions;
- [x] ambiguity/fail-closed device identity contracts;
- [x] journal blocks unresolved unsafe follow-on mutation.

Remaining:

- [ ] one integrated **Optimize Interrupt Affinity** preflight;
- [ ] quiet/background-load check with actionable warning rather than killing user apps;
- [ ] combined GPU + xHCI baseline snapshot owned by one product session;
- [ ] explicit pending-reboot/multi-GPU/unsupported-topology user-facing stop reason.

---

## Phase 2 — Baseline interrupt evidence

**State: MEASUREMENT ENGINE EXISTS; INTEGRATED V1 DEEP BASELINE OPEN**

Available:

- [x] bounded kernel ETW DPC/ISR capture;
- [x] per-CPU DPC/ISR counts;
- [x] DPC/ISR duration distributions and module attribution;
- [x] integrity/lost-event accounting;
- [x] read-only evidence export/provenance;
- [x] existing repeated steady baseline product.

Remaining:

- [ ] wire the deep baseline into the automatic workflow;
- [ ] persist before values needed by final comparison: per-CPU DPC/ISR counts, total duration, tail duration and module attribution;
- [ ] surface quiet-condition warnings without making background-app closure a blind hard requirement.

LatencyMon is not a dependency; LatencyPilot uses its own ETW evidence.

---

## Phase 3 — Automatic GPU core search

**State: SIMPLIFIED V1 SOURCE + HOSTED CONTRACTS IMPLEMENTED; PHYSICAL PROOF OPEN**

Current source workflow:

```text
exact original/default GPU affinity
→ normal-user deterministic D3D12 calibration
→ frozen worker map/workload/seed
→ 5 s non-scored Original warm-up/reference
→ 3 scored Original runs; one bounded replacement if needed
→ screen every eligible logical CPU in blocks of at most four:
     journaled apply/restart + stored-state verify
     5 s non-scored warm-up
     1 scored 30 s screening run
     exact rollback + Original-state verification
     fresh Original block control after each full block when candidates remain
→ final Original control after the last screening block
→ normalize rankable screening decision metrics against time-local Original controls
   while preserving raw scored trials unchanged
→ carry measured control movement as uncertainty
→ if effective 1%-low variability >15%:
     retain screening diagnostics, skip finalist confirmation, RestoreOriginal
→ otherwise rank decision aggregates by 1% low → AVG → lower p99 → 0.1% low rare-tail context
→ shortlist best three plus candidates within min(3%, max(1%, effective variability)) of third place, capped at five
→ two independent shuffled finalist re-test rounds
→ prefer tightest stable 3-of-up-to-4 cluster; otherwise retain all four and carry observed variance
→ fresh Original control after finalist re-tests; merge phase movement into uncertainty
→ evaluate finalists against Original + repeatability + time-local uncertainty + frame/interrupt-tail guardrails
→ apply highest-ranked clean winner once
→ final benchmark-only warm-up → ETW verification capture
→ Keep only with clean attributable target-only runtime GPU ISR placement
   otherwise exact RestoreOriginal
```

Source checklist:

- [x] D3D12 benchmark and queue timestamp evidence;
- [x] controlled benchmark wall-period AVG / 1% / 0.1% / p99 statistics;
- [x] all eligible logical-CPU candidates from actual Windows topology, including SMT siblings and CPU0;
- [x] one scored screening run per candidate;
- [x] bounded Original block controls during screening plus final screening control;
- [x] time-local normalization separates candidate decision evidence from gradual background movement;
- [x] raw candidate/control observations remain preserved for audit/diagnostics;
- [x] measured local movement raises uncertainty and Keep thresholds rather than being called a candidate win;
- [x] effective >15% 1%-low variability skips expensive finalist confirmation and restores Original;
- [x] finalist confirmation capped at five candidates;
- [x] deterministic finalist-order shuffling reduces ordering bias;
- [x] repeatability capped at four scores; stable 3-run cluster preferred, otherwise all four valid runs remain usable with observed variance;
- [x] ranking treats <=1% differences in 1% low / AVG / p99 as practical ties; 0.1% low only breaks a remaining tie above 5%;
- [x] post-finalist Original control contributes additional time-local uncertainty before Keep evaluation;
- [x] comparable GPU-driver DPC/ISR p99 tails are median/noise-aware Keep guardrails;
- [x] rejected top finalist falls through to the next ranked clean improvement;
- [x] no separate SMT-refinement phase;
- [x] no ABBA/BAAB confirmation loop;
- [x] Windows default is exact recovery/reference state, not a fixed minimum-improvement opponent;
- [x] screening PresentMon/ETW outages degrade diagnostics instead of aborting valid benchmark-owned frame evidence;
- [x] healthy ETW proving wrong/off-target placement invalidates that candidate;
- [x] final Keep requires clean ETW + attributable GPU ISR + target-only placement;
- [x] exact rollback between candidates and on failed final verification/cancellation;
- [x] standalone pinned PresentMon remains an independent cross-check, not a service dependency;
- [x] PresentMon cadence parsing distinguishes current `FrameTime`, legacy `MsBetweenPresents` and different `MsBetweenAppStart` semantics;
- [x] failed PresentMon raw diagnostics use bounded retention;
- [x] development result presentation consumes persisted decision rank/metrics instead of re-ranking shuffled raw data;
- [x] EvidenceReady / DevelopmentOnly / Blocked source-state policy is shared by App/helper;
- [x] dirty runs are explicitly non-closure evidence;
- [ ] one exact clean green physical Gate A run on owner hardware using the current method;
- [ ] repeat the whole search for practical reproducibility;
- [ ] Stop safely + supported failure/recovery physical exercise;
- [ ] rendered/taskbar/keyboard/accessibility inspection of the new result surface.

### GPU physical Gate A

Gate A closes only when one exact clean green revision proves on supported hardware:

1. every expected eligible logical CPU receives one scored screening run;
2. Original block/final controls are captured and normalization/uncertainty are persisted correctly;
3. if effective variability exceeds the finalist-confirmation budget, the run restores Original without manufacturing a Keep winner;
4. otherwise the shortlist receives the required bounded re-tests;
5. repeatability never exceeds four scored attempts per Original/finalist decision set;
6. exact rollback occurs between candidate activations and on failure/cancellation;
7. the selected finalist clears the documented noise/time-local uncertainty and guardrail thresholds;
8. final ETW proves target-only GPU ISR placement before Keep;
9. terminal stored state is verified with zero unresolved journal ownership;
10. Stop safely plus one supported failure path restore exact Original;
11. a repeated whole search is practically reproducible or reports uncertainty explicitly.

Public mutation IPC remains unarmed until this physical gate passes.

---

## Phase 4 — Keep GPU winner and product mutation boundary

**State: INTERNAL FINAL-KEEP SOURCE IMPLEMENTED; PRODUCT ARMING GATED**

- [x] internal winner apply and exact stored-state verification;
- [x] final ETW target-only ISR placement mandatory for Keep;
- [x] failure/cancellation rollback preserves exact original state ownership;
- [ ] Gate A physical proof;
- [ ] typed allowlisted mutation-specific Service IPC after Gate A;
- [ ] App → Service physical mutation authorization proof;
- [ ] normal-user GPU mutation arming only after those gates.

---

## Phase 5 — Automatic USB/input CPU selection

**State: READ-ONLY AUTOMATIC RECOMMENDATION SOURCE IMPLEMENTED; PHYSICAL EVIDENCE PENDING**

- [x] Raw Input identity;
- [x] PnP ancestry;
- [x] USB hub/port correlation;
- [x] exact xHCI controller identity;
- [x] Raw Input host timing metrics;
- [x] xHCI DPC/ISR attribution/readiness source;
- [x] post-GPU quiet ETW headroom capture after verified GPU Keep;
- [x] exclude the whole physical core containing the GPU winner, including SMT sibling;
- [x] rank CPU headroom by total DPC+ISR duration, then p99 interrupt tail, then event count context;
- [x] bind recommendation to the exact interrupt-owning xHCI controller;
- [x] persist route/controller/CPU and transparent reason in Gate A evidence;
- [ ] representative physical input/xHCI evidence;
- [ ] normal-user rendering in the integrated workflow.

---

## Phase 6 — USB/xHCI apply and safety check

**State: INTERNAL REVERSIBLE MUTATION SUBSTRATE IMPLEMENTED; PRODUCT/PHYSICAL GATED**

- [x] bounded reversible xHCI/controller-affinity mutation source;
- [x] exact stored-state snapshot and durable journal integration;
- [x] restart-required/reboot-pending state plus exact rollback/recovery source;
- [ ] controller-specific ETW runtime-placement verification in the integrated product flow;
- [ ] Raw Input timing sanity check after apply;
- [ ] integrated rollback of USB/xHCI while preserving a proven GPU winner when USB verification fails;
- [ ] representative high-polling hardware physical evidence.

---

## Phase 7 — One reboot and post-login verification

**State: DEVICE-INTERRUPT REBOOT/RECOVERY PRIMITIVES IMPLEMENTED; COMBINED PRODUCT WORKFLOW OPEN**

- [x] durable journal survives interruption/restart;
- [x] recovery re-reads actual machine state;
- [x] GPU restart/reboot-required detection primitives;
- [x] generic device-interrupt apply/rollback reboot-pending + resume source;
- [ ] persist one combined GPU+xHCI pending-verification product session;
- [ ] request one product-level reboot when required;
- [ ] post-login verify stored GPU/xHCI policies as one session;
- [ ] prove runtime GPU and xHCI interrupt placement;
- [ ] restore baseline if either managed state cannot be verified.

---

## Phase 8 — Final before/after evidence

**State: METRIC/REPORT PRIMITIVES EXIST; INTEGRATED V1 REPORT OPEN**

- [ ] repeat like-for-like deep ETW baseline conditions after optimization;
- [ ] short GPU verification benchmark;
- [ ] show GPU CPU, AVG FPS, 1% low, 0.1% low and p99 context where comparable;
- [ ] show xHCI CPU, DPC/ISR counts, total/tail durations and Raw Input host timing where comparable;
- [ ] distinguish raw observations, decision aggregates and verified terminal state;
- [ ] never label an unmeasured proxy as click-to-photon or network latency;
- [ ] export provenance and verification state with the result.

---

## Phase 9 — Normal-user v1 UX and release

**State: DEVELOPMENT RESULT UX + RELEASE FOUNDATIONS EXIST; FINAL ONE-BUTTON PRODUCT FLOW OPEN**

Target normal-user surface:

```text
Optimize Interrupt Affinity

✓ Hardware detected
✓ Baseline captured
✓ GPU cores tested
✓ Best GPU core selected and verified
✓ Input/xHCI CPU selected and verified
↻ Restart if required

Restore original settings
```

- [x] non-elevated WinUI shell;
- [x] development GPU Gate A progress and safe-stop experience;
- [x] Gate A source-state UX shows Evidence-ready / Development only / Blocked;
- [x] validated Gate A sessions package a shareable evidence ZIP;
- [x] Overview receives authority-selected Gate A result presentation instead of auto-opening raw JSON;
- [x] result surface includes candidate comparison, trial history, decision evidence and explicit evidence actions;
- [x] self-contained Windows 11 x64 App/Service release source;
- [x] install/upgrade/uninstall recovery checks and signing hooks;
- [ ] real Windows render inspection of the new result surface in light/dark/high-contrast and text scaling;
- [ ] integrated normal-user `Optimize Interrupt Affinity` orchestration;
- [ ] concise final product before/after result UI;
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

- Keep the permanent suite small and behavior-focused; do not add one test per implementation detail.
- The critical suite is consolidated behind one MSTest entrypoint that invokes durable `AuditCase` contracts; internal audit cases are not separate permanent test-method count.
- The repository default remains 10 permanent automated test methods total; owner-authorized growth to at most 20 remains an exception for genuinely necessary durable boundaries, not a target.
- Temporary TDD characterization logic must be merged into canonical contracts or removed once represented.
- Hardware validation runs are evidence, not unit tests.

## Completion rule

Repository/source completion requires every v1 source outcome to be implemented or explicitly gated by a documented physical prerequisite, canonical docs to match actual source, public mutation to remain fail-closed until its gate, and the exact final HEAD to be green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed install/upgrade/uninstall evidence.