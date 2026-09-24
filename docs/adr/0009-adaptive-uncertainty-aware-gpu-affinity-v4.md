# ADR 0009 — Adaptive uncertainty-aware GPU affinity v4

Status: **Accepted and implemented in source** (2026-09-24)

Supersedes ADR 0008 for new GPU measurement/ranking evidence. ADR 0008 remains the historical v3 contract. ADR 0006 remains the product/safety authority.

## Decision

Keep v3's local Original → Candidate → Original controls, robust Original median/MAD, best-observed ranking under ordinary noise, exact rollback and separate Keep safety, but spend measurement time adaptively.

New raw evidence uses `gpu-affinity-benchmark-v4`. The report schema remains `latencypilot-gpu-auto-affinity-report-v3` because its data shape is unchanged.

Search flow:

1. 10 s representative screen for every physical core.
2. Refine at most four plausible physical-core hypotheses.
3. Retain the observed top two logical CPUs whenever at least two structurally valid candidates exist, then admit additional uncertainty-overlapping challengers up to a maximum of five.
4. Recheck each shortlisted CPU once for 10 s.
5. Advance the top two by median short-screen effect.
6. Confirm both with two shuffled 15 s local pairs.
7. Add one final 15 s round only when their lead remains inside measured uncertainty.

Bounded screening uncertainty is `max(1 percentage point, effect MAD, median(min(control movement, drift budget)))`. Extreme drift therefore cannot make an obvious loser look infinitely plausible, while a near-neutral noisy CPU still gets one recheck. Uncertainty may add challengers, but it never removes the observed runner-up before the second short comparison.

Automatic Keep is stricter than ranking: median 1%-low effect must be positive; both primary effects must be positive when only two pairs were needed, or at least two of three after an uncertainty extension; performance/interrupt guardrails and final target-only ISR placement must still pass.

“Original” means the exact pre-test Windows/driver GPU interrupt-affinity policy. It is not CPU 0.

Controlled frame evidence now requires finite positive periods, at least 120 frames, at least 20 controlled frames per requested second, and a scored QPC duration within 95%–110% of the requested interval. GPU-fence/worker waits are bounded to five seconds, trial controller slack to 15 seconds, and renderer recreation to 35 seconds. Kernel ETW is cropped to the scored QPC duration so post-score drain time is excluded.

## Rationale

The 2026-09-24 physical v3 run exposed the fixed-cut failure: a plausible noisy CPU was eliminated after one short pair, while two apparent screening leaders reversed materially during expensive finalist confirmation. The old 3 × 3 × 30 s tournament spent time confirming the wrong shortlist.

v4 reallocates that budget to one short uncertainty-aware recheck, then spends longer evidence only on two CPUs. No Bayesian, bootstrap or hidden weighted score is added.

Default scored candidate finalist time falls from 270 seconds (3 × 3 × 30 s) to 60 seconds (2 × 2 × 15 s), before controls/retries; the third round is paid only when needed.
