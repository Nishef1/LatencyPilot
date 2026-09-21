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
- [x] GPU paired-v2 measurement authority separated from broader product/safety authority;
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

**State: PAIRED-V2 SOURCE + HOSTED CONTRACTS IMPLEMENTED; PHYSICAL PROOF OPEN**

Current source workflow:

```text
capture exact Original/default GPU affinity
→ 5 s non-scored Original warm-up
→ collect 10 s scored Original observations
→ require 3-run 1%-low cluster, ±3% preferred; bounded recovery up to ±6% after observations 4/5
→ if no valid cluster after 5, verify/retain Original and stop before candidate mutation
→ capture fresh 10 s Original control O0
→ Stage A: screen one eligible logical processor per physical core
→ for each candidate use a local pair:
     Original before
     apply/restart/verify candidate
     5 s non-scored transition warm-up
     10 s scored candidate
     exact rollback + Original verification
     5 s non-scored Original warm-up
     10 s scored Original after
→ local effect uses geometric mean of adjacent Original controls
→ pair drift budget = clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)
→ one retry for an unstable pair; two consecutive exhausted candidates stop safely
→ Stage B: refine siblings only on the best 2 physical cores, plus a 3rd inside the 1% practical-equivalence margin
→ Stage C: advance best 2 logical CPUs, plus a 3rd inside the 1% margin; hard cap 3 finalists
→ each finalist receives 3 independent valid 30 s local pairs in shuffled order
→ finalist decision floor = max(1%, median finalist pair-control movement)
→ require repeatable positive 1%-low effect without material AVG/p99/interrupt-tail regression
→ practical ties within 1 percentage point remain ties
→ apply selected operational target once more
→ final benchmark-only warm-up + clean target-only ETW ISR-placement verification
→ Keep only with verified terminal candidate state; otherwise exact RestoreOriginal
```

Source checklist:

- [x] D3D12 benchmark and queue timestamp evidence;
- [x] controlled benchmark wall-period AVG / 1% / 0.1% / p99 statistics;
- [x] candidate generation from actual Windows topology/CPU-set evidence with CPU0 allowed;
- [x] bounded pre-mutation Original 3-of-up-to-5 qualification;
- [x] exact 10 s screening duration and 30 s finalist duration owned by the method;
- [x] direct `Original before → Candidate → Original after` persisted pair evidence;
- [x] geometric-mean local reference and signed paired effects;
- [x] bounded 6–10% pair drift budget derived from qualified Original noise;
- [x] one pair retry and early safe stop after two consecutive exhausted candidates;
- [x] Stage-A physical-core representative selection;
- [x] bounded Stage-B sibling refinement;
- [x] bounded Stage-C finalist selection, hard cap three;
- [x] three independent valid local pairs per finalist;
- [x] finalist `DecisionFloor` persisted;
- [x] practical ties represented explicitly rather than hidden by decimal ordering;
- [x] comparable GPU-driver DPC/ISR tails used as guardrails when sufficient evidence exists;
- [x] final Keep requires clean attributable target-only GPU ISR placement;
- [x] exact rollback between candidates and on failure/cancellation;
- [x] custom selected-CPU diagnostic scope always restores Original;
- [x] Original-only diagnostic scope performs no affinity mutation/restart;
- [x] result presentation consumes persisted decision/finalist authority instead of re-ranking execution order;
- [x] direct pair evidence is visible in result UX;
- [x] `GateAClosureEligible` is rendered as **Evidence eligible**, not as physical gate closure;
- [x] historical v1 evidence is not reinterpreted as v2;
- [ ] one exact clean green physical Gate A run on owner hardware using paired-v2;
- [ ] repeat the whole paired-v2 search for practical reproducibility;
- [ ] Stop safely + supported failure/recovery physical exercise;
- [ ] rendered/taskbar/keyboard/accessibility inspection of the result surface.

### GPU physical Gate A

Gate A closes only when one exact clean green revision proves on supported hardware:

1. Original qualification either establishes the bounded three-run regime or stops before mutation after five misses;
2. every eligible physical core receives a Stage-A representative screen;
3. Stage-B sibling refinement matches the persisted selected physical-core hypotheses;
4. every ranked candidate has reconstructable adjacent Original controls and pair math;
5. unstable pairs obey the one-retry rule and never silently become ranked evidence;
6. finalists obey the hard cap and each accepted finalist has three valid 30 s pairs;
7. persisted decision floor/guardrails match the authoritative Keep decision;
8. exact rollback occurs between candidate activations and on failure/cancellation;
9. final ETW proves target-only GPU ISR placement before Keep;
10. terminal stored state verifies with zero unresolved journal ownership;
11. Stop safely plus one supported failure path restore exact Original;
12. a repeated whole search is practically reproducible or reports instability explicitly;
13. real Windows rendering/accessibility is inspected with the actual report/evidence bundle.

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