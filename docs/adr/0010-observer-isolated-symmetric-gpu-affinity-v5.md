# ADR 0010 — Observer-isolated symmetric GPU affinity v5

Status: **Accepted and implemented in source** (2026-09-24)

Supersedes ADR 0009 for new GPU measurement/ranking evidence. ADR 0009 remains the historical v4 contract. ADR 0006 remains the product/safety authority.

## Decision

Keep v4's local `Original → Candidate → Original` controls, robust Original median/MAD, best-observed ranking under ordinary noise, exact rollback, adaptive 15-second finalist confirmation and separate Keep safety, but close three defects exposed by the 2026-09-24 owner v4 evidence.

New raw evidence uses `gpu-affinity-benchmark-v5`. The report schema remains `latencypilot-gpu-auto-affinity-report-v3`; its persisted result shape is still sufficient.

### 1. Recheck the observed top four before the finalist cut

After representative/sibling screening, retain the observed top four structurally valid logical CPUs whenever four are available. A fifth CPU may join only when bounded uncertainty still overlaps the leader.

Every shortlisted CPU gets one additional 10-second local pair. Rank the repeated short evidence by median paired 1%-low effect and advance only the top two.

This spends two extra short rechecks relative to v4 when enough candidates exist, but prevents one noisy short screen from sending only false leaders into the much more expensive finalist stage.

### 2. Isolate observer startup from the scored QPC window

External PresentMon/kernel-ETW observers are still used for independent context, interrupt evidence and final placement safety. Their startup must not become part of the benchmark-owned frame-period score.

A scored trial whose controller declares `ObserverActive=true` performs an unscored pre-score settle:

- minimum observer-active settle: 2 seconds;
- maximum settle: 4 seconds;
- the transient marker threshold is `max(10 ms, 2 × median frame period from the initial 2 s settle)`, so the gate scales with normal workload cadence instead of assuming every supported GPU renders below 10 ms;
- the scored window opens only after a 500 ms quiet tail following the latest such marker;
- pending in-flight frame contexts are drained and inspected before the quiet boundary is accepted;
- failure to reach the quiet boundary by the hard deadline invalidates the trial instead of silently scoring contaminated startup.

Warm-up or standalone trials without external observers do not pay this observer-settle cost.

This is **not outlier deletion**. No scored frame is removed or rewritten. The scored QPC boundary simply opens after observer startup has stabilized.

### 3. Remove fixed-SMT-worker asymmetry

The benchmark still owns one CPU worker per selected physical core, but that worker is no longer pinned permanently to the first logical sibling. Its affinity is the complete logical-processor mask of that physical core.

The exact physical-core masks are:

- frozen for the complete benchmark session;
- persisted in the raw artifact;
- included in frozen-workload identity;
- validated by Gate A against the fresh Windows topology.

This prevents a candidate such as CPU 2 from being forced to share the exact logical processor with a benchmark worker while sibling CPU 3 sees only physical-core sharing.

## Search flow

1. 10 s representative screen for every physical core.
2. Refine at most four plausible physical-core hypotheses.
3. Retain the observed top four logical CPUs when available; admit at most one additional uncertainty-overlapping challenger.
4. Recheck each shortlisted CPU once for 10 s.
5. Advance the top two by median short-screen effect.
6. Confirm both with two shuffled 15 s local pairs.
7. Add one final 15 s round only when their lead remains inside measured uncertainty.
8. Rank every structurally valid finalist; confidence describes uncertainty and never erases rank 1.
9. Keep remains stricter than ranking and still requires positive benefit/guardrails plus final target-only runtime ISR placement proof.

Bounded screening uncertainty remains `max(1 percentage point, effect MAD, median(min(control movement, drift budget)))`.

## Evidence that changed the decision

The owner v4 run from source revision `ad75eae2762b128253305d63b6ae33e11fbbd9c1` showed two independent problems:

- short-screen leaders CPU 2/3 reversed materially during repeated finalist confirmation while CPU 4/14 had positive short evidence but did not receive the same recheck authority;
- scored trials repeatedly contained a large early frame-period transient around the same relative point after observer startup, while non-observer warm-up trials did not show the same phase-locked pattern.

The same audit also found that one worker per physical core had been pinned to the first logical sibling, creating asymmetric direct logical-CPU contention between SMT siblings.

v5 fixes the measurement design instead of filtering bad-looking scored samples after the fact.

## Non-goals

v5 does not add Bayesian ranking, bootstrap probabilities, a hidden weighted score, p-value winner gates, or automatic deletion of frame-time outliers. It does not treat graphics-hook warnings such as NVIDIA/RTSS injection as proof of causation; those remain explicit interference context.
