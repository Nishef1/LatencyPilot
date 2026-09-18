# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-18

`PROJECT_STATUS.md` owns current execution/evidence state. This file owns the required product outcomes. Source-complete and physically validated are different claims.

## Product definition — v1

LatencyPilot v1 automates one narrow, reversible Windows 11 workflow:

```text
preflight / quiet check
→ baseline DPC/ISR evidence
→ benchmark GPU interrupt affinity across eligible physical cores
→ re-test the best up to three and select the best repeatable core
→ final runtime GPU ISR-placement verification
→ measure remaining per-CPU interrupt headroom
→ resolve the primary input device to its exact xHCI controller
→ choose/apply a separate USB/xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence + Restore Windows Defaults
```

The manual inspiration is AutoGpuAffinity + LatencyMon/ETW + Interrupt Affinity Policy Tool, but LatencyPilot replaces manual device matching and unsafe guesswork with Windows topology, ETW verification, journal-owned rollback and explicit uncertainty.

### v1 includes

- processor/device/USB topology discovery;
- ETW DPC/ISR measurement and per-CPU/module attribution;
- deterministic D3D12 GPU benchmark;
- understandable AVG FPS / 1% low / 0.1% low results;
- safe GPU interrupt-affinity mutation, restart and rollback;
- automatic GPU-core search;
- Raw Input → USB → xHCI route resolution;
- per-CPU interrupt-headroom analysis for input/xHCI placement;
- reversible xHCI/controller affinity after the shared mutation substrate is physically proven;
- one-reboot post-apply verification;
- before/after evidence and Restore Windows Defaults;
- non-elevated App with a narrow privileged boundary.

### Not in the v1 automatic path

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a generic cross-subsystem/Pareto auto-optimizer. Existing read-only/future source may remain, but it must not complicate the v1 critical path.

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
- [x] current v1 scope removes ABBA/BAAB, active SMT refinement and Original-vs-candidate winner thresholds;
- [x] NIC/audio/cross-subsystem automatic tuning removed from the v1 critical path.

### Exit gate

The v1 workflow, safety boundary and rollback owner are unambiguous without chat history. **SOURCE SATISFIED.**

---

## Phase 1 — Preflight and exact snapshot

**State: MOST SOURCE EXISTS; INTEGRATED V1 PREFLIGHT OPEN**

Already available:

- [x] processor-group-aware physical/logical/SMT topology;
- [x] PnP GPU/input/USB/xHCI inventory primitives;
- [x] driver/version and Windows/source provenance;
- [x] exact GPU affinity stored-state snapshot;
- [x] CPU-set eligibility handling without assuming even/odd CPU numbers;
- [x] ambiguity/fail-closed device identity contracts;
- [x] journal blocks unresolved unsafe follow-on mutation.

Remaining v1 wiring:

- [ ] one integrated **Optimize Interrupt Affinity** preflight;
- [ ] quiet/background-load check with actionable warning rather than killing user apps;
- [ ] combined GPU + future xHCI baseline snapshot owned by one session;
- [ ] explicit pending-reboot/multi-GPU/unsupported-topology user-facing stop reason.

---

## Phase 2 — Baseline interrupt evidence

**State: MEASUREMENT ENGINE EXISTS; V1 10-MINUTE BASELINE WORKFLOW OPEN**

Available source:

- [x] bounded kernel ETW DPC/ISR capture;
- [x] per-CPU DPC/ISR counts;
- [x] DPC/ISR duration distributions and module attribution;
- [x] integrity/lost-event accounting;
- [x] read-only evidence export/provenance;
- [x] existing repeated baseline product remains available for diagnostic/steady analysis.

V1 work:

- [ ] wire the video-style deep baseline into the automatic workflow (default target: 10 minutes, configurable later);
- [ ] persist before values needed by final comparison: per-CPU DPC/ISR counts, total duration, tail duration and module attribution;
- [ ] surface quiet-condition warnings without making background-app closure a blind hard requirement.

LatencyMon is not a dependency; LatencyPilot uses its own ETW evidence.

---

## Phase 3 — Automatic GPU core search

**State: SIMPLIFIED V1 SOURCE + HOSTED CRITICAL-TEST CI COMPLETE; PHYSICAL PROOF OPEN**

Current source workflow:

```text
exact original/default GPU affinity
→ normal-user deterministic D3D12 calibration
→ frozen worker map/workload/seed
→ 5 s non-scored original warm-up/reference (benchmark only; no PresentMon/ETW)
→ every eligible physical core:
     journaled apply/restart + stored-state verify
     5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
     1 scored 30 s screening run
     exact rollback
→ rank by 1% low ↓ priority, then 0.1% low, AVG FPS, p99 diagnostic fallback
→ best three + every screening candidate within 1% 1%-low of the third-place cutoff:
     two independent deterministically shuffled re-test rounds
     each candidate gets fresh apply/restart + warm-up + one 30 s score + exact rollback
→ rank finalists from three transition-isolated scored observations; unstable 1% lows are unrankable
→ apply winner once
→ final benchmark-only warm-up → ETW verification capture
→ Keep only with clean target-only runtime GPU ISR placement
   otherwise exact RestoreOriginal
```

Source checklist:

- [x] D3D12 benchmark and GPU timestamp evidence;
- [x] controlled benchmark wall-period AVG / 1% / 0.1% / p99 statistics;
- [x] physical-core candidates from actual Windows topology, CPU0 allowed;
- [x] one scored screening run per candidate;
- [x] best three plus any screening candidate inside the 1% primary-noise cutoff receive two additional scored re-tests in separate fresh transition rounds;
- [x] finalist order is deterministically shuffled in each re-test round to reduce time/thermal ordering bias;
- [x] ranking is noise-aware: <=1% differences in 1% low / AVG / p99 are practical ties; 0.1% low only breaks a remaining tie when its relative difference exceeds 5%;
- [x] no SMT/hyperthread sibling refinement in v1;
- [x] no ABBA/BAAB confirmation loop;
- [x] Windows default is recovery/reference state, not a fixed minimum-improvement gate;
- [x] 5 s non-scored post-transition warm-up;
- [x] screening PresentMon/ETW outages degrade diagnostics instead of aborting the entire search;
- [x] healthy ETW proving wrong/off-target placement invalidates that screening candidate;
- [x] final Keep requires clean ETW + attributable GPU ISR + target-only placement;
- [x] exact rollback between candidates and on failed final verification/cancellation;
- [x] standalone pinned PresentMon 2.5.1 remains an independent cross-check, not a separately installed service dependency;
- [x] PresentMon frame-cadence parsing distinguishes `MsBetweenPresents` from `MsBetweenAppStart`;
- [x] failed PresentMon raw diagnostics use bounded retention;
- [x] progress/result UI ranks by 1% low and displays 0.1%/AVG/p99 context;
- [x] exact-final-HEAD hosted Tests green for this simplified revision;
- [ ] physical Gate A rerun on the owner machine;
- [ ] repeat whole search to establish practical reproducibility;
- [ ] Stop safely + supported failure/recovery physical exercise;
- [ ] rendered/taskbar/keyboard/accessibility inspection.

### GPU physical Gate A

Gate A closes only when the exact clean green revision proves on supported hardware:

1. all expected physical cores are screened;
2. top candidates receive two additional scored runs;
3. a stable finalist is selected by the documented low-FPS order;
4. exact rollback occurs between candidate blocks;
5. final stored state is correct;
6. final ETW proves GPU ISR target-only placement before Keep;
7. Stop/failure paths restore and verify the original state with zero unresolved journal state.

Product mutation IPC remains unarmed until this physical substrate passes.

---

## Phase 4 — Keep GPU winner and product mutation boundary

**State: INTERNAL FINAL-KEEP SOURCE IMPLEMENTED; PRODUCT ARMING GATED**

- [x] internal winner apply and exact stored-state verification;
- [x] final ETW target-only ISR placement is mandatory for Keep;
- [x] failure/cancellation rollback preserves exact original state ownership;
- [ ] Gate A physical proof;
- [ ] typed allowlisted mutation-specific Service IPC after Gate A;
- [ ] App → Service physical mutation authorization proof;
- [ ] normal-user GPU mutation arming only after those gates.

---

## Phase 5 — Automatic USB/input CPU selection

**State: READ-ONLY AUTOMATIC RECOMMENDATION SOURCE IMPLEMENTED; PHYSICAL EVIDENCE PENDING**

Available:

- [x] Raw Input identity;
- [x] PnP ancestry;
- [x] USB hub/port correlation;
- [x] exact xHCI controller identity;
- [x] Raw Input host timing metrics;
- [x] xHCI DPC/ISR attribution/readiness source.

V1 selection work after GPU winner is fixed:

- [x] run the post-GPU quiet ETW capture used for CPU headroom in Gate A after a verified GPU Keep;
- [x] exclude the entire physical core containing the GPU winner, including its SMT sibling;
- [x] rank available CPU headroom from total DPC + ISR duration, then p99 interrupt tail, then event count as context;
- [x] bind the selected CPU to the exact interrupt-owning xHCI controller, not blindly to the leaf mouse;
- [x] persist the selected input route/controller/CPU and transparent reason in the Gate A report; normal-user UI rendering remains Phase 9.

---

## Phase 6 — USB/xHCI apply and safety check

**State: MUTATION OPEN; BLOCKED UNTIL GPU SHARED-SUBSTRATE GATE PASSES**

- [ ] supported reversible xHCI/controller-affinity mutation;
- [ ] exact stored-state snapshot and journal integration;
- [ ] short post-apply ETW runtime-placement verification;
- [ ] Raw Input timing sanity check;
- [ ] rollback USB/xHCI while preserving the proven GPU winner if USB verification fails;
- [ ] representative high-polling hardware physical evidence.

This is sequencing, not scope deferral: automatic USB/xHCI is part of v1, but it reuses the same privileged mutation/recovery substrate only after that substrate is proven safely with GPU Gate A.

---

## Phase 7 — One reboot and post-login verification

**State: RECOVERY/REBOOT PRIMITIVES EXIST; COMBINED WORKFLOW OPEN**

- [x] durable journal survives interruption/restart;
- [x] recovery re-reads actual machine state;
- [x] GPU restart/reboot-required detection primitives;
- [ ] persist a combined GPU+xHCI pending-verification session;
- [ ] request one reboot when required;
- [ ] post-login verify stored GPU/xHCI policies;
- [ ] prove runtime GPU and xHCI interrupt placement;
- [ ] restore baseline if either managed state cannot be verified.

---

## Phase 8 — Final before/after evidence

**State: METRIC/REPORT PRIMITIVES EXIST; INTEGRATED V1 REPORT OPEN**

- [ ] repeat the same deep ETW baseline conditions after optimization;
- [ ] short GPU verification benchmark;
- [ ] show GPU CPU, AVG FPS, 1% low, 0.1% low and p99 context before/after where comparable;
- [ ] show xHCI CPU, DPC/ISR counts, total/tail durations and Raw Input host timing before/after;
- [ ] never label an unmeasured proxy as click-to-photon or network latency;
- [ ] export provenance and verification state with the result.

---

## Phase 9 — Normal-user v1 UX and release

**State: DEVELOPMENT UI + RELEASE FOUNDATIONS EXIST; FINAL ONE-BUTTON PRODUCT FLOW OPEN**

Target normal-user surface:

```text
Optimize Interrupt Affinity

✓ Hardware detected
✓ Baseline captured
✓ GPU cores tested
✓ Best GPU core selected and verified
✓ Input/xHCI CPU selected and verified
↻ Restart if required

Restore Windows Defaults
```

- [x] non-elevated WinUI shell;
- [x] development GPU Gate A progress and safe-stop experience;
- [x] self-contained Windows 11 x64 App/Service release source;
- [x] install/upgrade/uninstall recovery checks and signing hooks;
- [ ] integrated normal-user `Optimize Interrupt Affinity` orchestration;
- [ ] concise before/after result UI;
- [ ] prominent Restore Windows Defaults;
- [ ] physical accessibility/keyboard/text-scale/high-contrast pass;
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
- Owner-authorized historical ceiling remains 20 methods; the repository is already above that legacy target, so new direction work must consolidate or replace tests rather than grow the suite.
- Temporary TDD characterization tests must be removed once their behavior is represented in the canonical tests.
- Hardware validation and benchmark repetitions are evidence, not automated unit tests.

## Definition of done

### Repository/source complete

Every v1 source outcome above is implemented or explicitly blocked by its documented physical safety prerequisite, canonical docs agree with the source, and the exact final HEAD has a green hosted Tests run.

### True v1 / 100%

Only after the physical read-only closure, GPU mutation gates, automatic USB/xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall checks are recorded may LatencyPilot be described as v1 complete.
