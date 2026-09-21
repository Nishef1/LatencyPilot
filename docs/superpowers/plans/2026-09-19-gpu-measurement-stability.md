# GPU Measurement Stability Implementation Plan

> **Current handoff:** this file records the active measurement-stability work and replaces obsolete intermediate tactics from the original 2026-09-19 plan. ADR 0006 owns intended v1 behavior; `PROJECT_STATUS.md` owns actual completion/evidence state.

**Goal:** make the v1 GPU interrupt-affinity experiment trustworthy enough for physical Gate A without expanding the product into a general tuning suite.

**Current authority:** `docs/adr/0006-simple-auto-interrupt-affinity-v1.md`, including the 2026-09-20 time-local measurement amendment.

**Current source baseline before this documentation reconciliation:** `29f153b1bfcb15cc452f17a9ffb082be2358852a`; hosted Tests run `35573555174` / run number `1449` passed on that exact source HEAD. Hosted CI is software-contract evidence only and does not close physical Gate A.

## Global constraints

- Work on `main` for owner-directed implementation.
- Keep `ServiceBoundary.MutationAvailable = false` until the physical arming gate is satisfied.
- Preserve exact journal-owned rollback/recovery behavior.
- Do not add MSI-mode, power-plan, NIC/RSS, audio, HAGS or BIOS mutation to the v1 automatic path.
- Do not expand single-CPU GPU affinity into multi-processor/MSI-X search until runtime interrupt topology is observable and physically validated.
- Keep the permanent test surface consolidated and within the owner-approved cap.
- Raw measurements, normalized decision evidence, uncertainty and final machine state must remain distinguishable.

## Current measurement design

The source now uses this sequence:

```text
exact Original state
→ deterministic D3D12 calibration with fixed low synthetic CPU work
→ Original warm-up/reference
→ establish Original repeatability/noise
→ screen every eligible logical CPU in blocks of at most four
   → apply/restart/verify
   → 5 s transition warm-up
   → one scored screening run
   → exact rollback + Original verification
   → Original block control after each full block when candidates remain
→ final screening Original control
→ normalize rankable candidate decision metrics against time-local Original controls
→ preserve raw candidate/control trials unchanged
→ carry control movement as uncertainty
→ if effective 1%-low variability >15%: skip finalists and RestoreOriginal
→ otherwise bounded finalist confirmation and noise-aware ranking
→ final benchmark-only warm-up + ETW placement verification
→ Keep only with attributable target-only GPU ISR proof; otherwise RestoreOriginal
```

Within each small screening block, current normalization interpolates the local Original level by candidate position between the surrounding controls. This is intentionally simpler than adding new timing/provenance infrastructure. Do not replace it with a more complex time-weighted model unless owner-hardware evidence shows residual within-block ordering bias that materially changes decisions.

## Decisions already closed in source

### 1. Automatic MSI mutation is excluded from v1

The automatic workflow no longer includes MSI mutation. Conservative MSI-related source may remain for recovery/future/manual work, but it is not part of the v1 automatic sequence.

### 2. Benchmark calibration is GPU-dominant

Synthetic CPU simulation is fixed at the existing minimum (`1000` iterations per worker). GPU command-batch calibration remains adaptive. Process identity, seed, resolution, worker map and workload stay frozen across candidate comparisons.

The 2026-09-19 owner run confirmed that this removed the former intentional multi-millisecond CPU-pressure confounder, but substantial time/order drift still remained.

### 3. Ordinary gradual drift is normalized, not treated as an automatic structural failure

The older tactic that stopped the whole screen when an Original control left a fixed repeatability band has been superseded. Current source captures time-local Original controls around bounded candidate groups, normalizes decision aggregates back to the session baseline and carries measured movement into uncertainty/Keep thresholds.

Structural evidence failures still fail closed. Effective 1%-low variability above 15% still blocks finalist confirmation and automatic Keep.

### 4. PresentMon correlation uses the benchmark QPC domain

The standalone pinned PresentMon console is a best-effort independent cross-check. Current source launches it with `--qpc_time`, parses `CPUStartQPC`, and crops rows against the benchmark artifact's `StartedAtQpc` / `EndedAtQpc` interval. `FrameTime` or legacy `MsBetweenPresents` may provide cadence; `MsBetweenAppStart` is not silently substituted as equivalent.

Missing/empty PresentMon evidence remains visible diagnostic evidence and does not manufacture frame samples. Final Keep depends on ETW placement proof, not PresentMon availability.

### 5. Warm-up remains evidence-gated

Do **not** replace the 5 s post-transition warm-up with an arbitrary longer delay yet. The benchmark also performs a symmetric 1 s unscored observer-settle immediately before every scored QPC window so collector/JIT/page-in startup is kept outside the score.

The old physical run suggested transition behavior could still be non-steady, but it predates the combined time-local normalization + QPC PresentMon + observer-settle state. The next exact-revision hardware run must show whether residual warm-up instability still contaminates decisions before a bounded steady-state detector is designed.

If that evidence remains problematic, prefer a bounded observable stability gate over a longer blind sleep. Any threshold must be justified by owner-hardware data and must keep total runtime bounded.

### 6. Runtime interrupt topology remains the prerequisite for broader affinity policy search

Stored registry configuration and allocated ConfigMgr resources do not by themselves prove runtime interrupt-vector topology or MSI-X queue behavior. Future multi-processor/MSI-X work must first add read-only evidence that clearly separates stored policy, allocated resources and observed runtime placement. No mutation expansion is authorized by this plan.

## Completed evidence

- `38106c2c0199d8b55395636df167fc3537841171` — benchmark CPU simulation fixed at the existing minimum while GPU calibration remains adaptive.
- 2026-09-19 owner development run — safe `RestoreOriginal`, exact Original verified/restored with zero unresolved recovery state; severe temporal drift remained and motivated local controls.
- `d0c8b3f09fa547edb3bee7e94c79bc1d9cba34b4` — time-local screening controls and normalization implemented.
- `4171b14486401dc26eef9586c48507b5755b8668` — PresentMon correlation moved into the benchmark QPC time domain.
- Current benchmark source includes a 1 s observer-settle before every scored QPC window.
- Gate A result presentation now consumes persisted decision ranks/metrics, labels non-Keep comparisons as diagnostic/comparison-only, and preserves restored Original as terminal truth.
- Source HEAD `29f153b1bfcb15cc452f17a9ffb082be2358852a` passed hosted Tests run `35573555174` (`#1449`) before this documentation reconciliation.

## Still open

### Physical Gate A

One exact clean green revision on the owner Windows 11 machine must still prove:

1. every expected eligible logical CPU is screened;
2. intermediate/final Original controls are captured and persisted;
3. normalization removes measured local background level without changing raw trial history;
4. local movement appears as uncertainty rather than candidate benefit;
5. >15% effective variability skips finalists and restores Original;
6. otherwise the bounded shortlist receives the documented re-tests;
7. final Keep, if any, clears repeatability/uncertainty/frame/interrupt guardrails;
8. final ETW proves target-only GPU ISR placement;
9. exact rollback and terminal state verification end with `unresolved=0`;
10. PresentMon QPC cross-check either produces valid rows or reports a precise bounded diagnostic failure.

### Reproducibility and recovery

After the first authoritative run:

- repeat the whole search to test practical reproducibility;
- exercise **Stop safely**;
- exercise one supported failure/recovery path and verify exact Original plus zero unresolved ownership;
- inspect the rendered result surface in relevant Windows theme/text-scale/keyboard states.

### Warm-up follow-up, only if hardware evidence still requires it

If the new exact-revision run still shows a large warm-up→score transition or systematic residual within-block ordering effect:

1. quantify it from the preserved trial/control evidence;
2. identify the smallest observable stability signal that tracks the problem;
3. design a bounded warm-up extension/steady-state gate with a hard maximum runtime;
4. validate it physically before calling the measurement method closed.

Do not add GPU clock locking or power mutation. Read-only clock/temperature/power telemetry may be considered only if it materially explains unresolved contamination and can be added without turning the v1 path into a vendor-specific dependency.

## Immediate execution ladder

1. Reconcile this handoff and `PROJECT_STATUS.md` with the current source/ADR, then require hosted Tests on the resulting exact documentation/source HEAD.
2. Run physical Gate A on that exact clean green revision and inspect local controls, normalized aggregates, uncertainty, terminal state, runtime and PresentMon diagnostics.
3. Repeat the whole search, then run Stop safely + one supported failure/recovery exercise.
4. Only if the new physical evidence still shows transition contamination, design the smallest bounded steady-state warm-up gate.
5. Only after physical Gate A closes, continue to product mutation arming and xHCI apply/verify work; runtime interrupt-topology evidence remains a prerequisite for any future broader MSI-X search.
