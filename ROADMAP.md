# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-21

`PROJECT_STATUS.md` owns current execution/evidence state. This file owns required product outcomes. Source-complete and physically validated are different claims.

## Product definition — v1

LatencyPilot v1 automates one narrow, reversible Windows 11 workflow:

```text
preflight / quiet check
→ baseline DPC/ISR evidence
→ bounded Original qualification
→ paired GPU affinity screening
→ physical-core representatives + promising SMT siblings
→ bounded finalist confirmation
→ final runtime GPU ISR-placement verification
→ measure remaining per-CPU interrupt headroom
→ resolve primary input to exact xHCI controller
→ choose/apply a separate USB/xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence + Restore original settings
```

Product/safety authority remains ADR 0006. GPU measurement/search/ranking authority is ADR 0007, which supersedes only those GPU sections of ADR 0006.

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

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a generic cross-subsystem/Pareto optimizer.

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

### Exit gate

The v1 workflow, safety boundary and rollback owner are unambiguous without chat history. **SOURCE SATISFIED.**

---

## Phase 1 — Preflight and exact snapshot

**State: MOST SOURCE EXISTS; INTEGRATED V1 PREFLIGHT OPEN**

Available:

- [x] processor-group-aware physical/logical/SMT topology;
- [x] current CPU-set eligibility evidence;
- [x] PnP GPU/input/USB/xHCI inventory primitives;
- [x] driver/version and Windows/source provenance;
- [x] exact GPU affinity stored-state snapshot;
- [x] ambiguity/fail-closed device identity contracts;
- [x] unresolved journal ownership blocks unsafe follow-on mutation.

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

**State: NOISE-TOLERANT V3 SOURCE + HOSTED CONTRACTS IMPLEMENTED; PHYSICAL PROOF OPEN**

Current source workflow:

```text
capture exact Original/default GPU affinity
→ 5 s non-scored Original warm-up
→ collect 3 scored 10 s Original observations
→ if robust median/MAD variability is high, extend to at most 5 observations
→ noise lowers confidence; it does not block candidate search
→ capture fresh 10 s Original control O0
→ Stage A: screen one eligible logical processor per physical core
→ each candidate uses Original-before → Candidate → Original-after local controls
→ local effect uses geometric mean of adjacent Original controls
→ one retry for high local drift
→ a still-noisy but structurally valid retry remains rankable
→ Stage B: refine at most top 3 physical-core hypotheses
→ Stage C: advance at most top 3 logical CPUs
→ each finalist gets 3 shuffled 30 s local pairs
→ rank every structurally valid finalist by median paired 1%-low effect
→ median/MAD + lead + pair consistency produce High/Medium/Low confidence
→ practical tie lowers confidence but does not erase rank 1
→ separate Keep guardrails decide whether the best observed CPU should remain active
→ final clean target-only ETW ISR-placement proof is mandatory for Keep
→ otherwise exact RestoreOriginal while preserving best-observed result
```

Source checklist:

- [x] D3D12 benchmark and controlled wall-period AVG / 1% / 0.1% / p99 statistics;
- [x] candidate generation from actual Windows topology/CPU-set evidence with CPU0 allowed;
- [x] 3–5 Original observations with robust median/MAD variability;
- [x] exact 10 s screening and 30 s finalist durations;
- [x] direct `Original before → Candidate → Original after` pair evidence;
- [x] geometric-mean local reference and signed paired effects;
- [x] one bounded high-drift retry without a noise-only candidate/search abort;
- [x] Stage-A physical-core representative selection;
- [x] bounded top-3 Stage-B sibling refinement;
- [x] bounded top-3 Stage-C finalist selection;
- [x] three shuffled finalist pairs;
- [x] median paired ranking + effect MAD + positive-pair consistency;
- [x] `BestObservedProcessor` persisted independently from terminal Keep/Restore state;
- [x] `SelectionConfidence` persisted as explanatory metadata, never a rank gate;
- [x] practical ties remain explicit while rank 1 remains the best observed estimate;
- [x] separate bounded AVG/frame-p99/interrupt-tail Keep guardrails;
- [x] final Keep requires clean attributable target-only GPU ISR placement;
- [x] exact rollback between candidates and on failure/cancellation;
- [x] result UX exposes actual Original → Candidate FPS/ms, absolute gain and paired percentage effect;
- [x] custom selected-CPU diagnostic always restores Original;
- [x] Original-only diagnostic performs no affinity mutation/restart;
- [x] historical v1/v2 evidence is not reinterpreted as v3;
- [ ] one exact clean green physical Gate A run on owner hardware using v3;
- [ ] repeat the whole v3 search for practical reproducibility;
- [ ] Stop safely + supported failure/recovery physical exercise;
- [ ] rendered/taskbar/keyboard/accessibility inspection of the result surface.

### GPU physical Gate A

Gate A closes only when one exact clean green revision proves on supported hardware:

1. structurally valid Original evidence continues through real-world variability and records robust noise instead of failing only for variance;
2. every eligible physical core receives a Stage-A representative screen;
3. Stage-B/Stage-C top-3 selection matches persisted paired evidence;
4. every ranked candidate has reconstructable adjacent Original controls and pair math;
5. high-drift retry evidence stays visible and does not erase an otherwise valid candidate;
6. finalists receive three shuffled 30 s pairs and rank by persisted median effect;
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

**State: READ-ONLY AUTOMATIC RECOMMENDATION SOURCE IMPLEMENTED; PHYSICAL EVIDENCE PENDING**

- [x] Raw Input identity;
- [x] PnP ancestry;
- [x] USB hub/port correlation;
- [x] exact xHCI controller identity;
- [x] Raw Input host timing metrics;
- [x] xHCI DPC/ISR attribution/readiness source;
- [x] post-GPU quiet ETW headroom capture after verified GPU Keep;
- [x] exclude the whole physical core containing the GPU winner, including SMT sibling;
- [x] rank CPU headroom by total DPC+ISR duration, then p99 interrupt tail, then event-count context;
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
- [ ] distinguish raw observations, paired decision evidence and verified terminal state;
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
✓ GPU hypotheses tested
✓ GPU target selected and verified, or Original retained safely
✓ Input/xHCI CPU selected and verified
↻ Restart if required

Restore original settings
```

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

Permanent tests remain deliberately bounded. Extend an existing high-blast-radius contract when a stable invariant needs coverage; temporary characterization may be created and removed during implementation. Do not create a new permanent test family merely to mirror implementation details.

Hosted CI proves software contracts only. It does not close physical Gate A or substitute for Windows hardware/render/recovery evidence.

## Completion rule

Repository/source completion means every v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.