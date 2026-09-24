# ADR 0008 — Noise-tolerant ranked GPU affinity v3

Status: **Superseded by ADR 0009 for new evidence** (2026-09-24)

Superseded ADR 0007 for v3 GPU measurement/ranking evidence. ADR 0007 remains the historical v2 contract. ADR 0006 remains the product/safety authority.

## Decision

LatencyPilot separates two questions:

1. **Which structurally valid CPU is the best observed option?**
2. **Should that option be kept as the active GPU interrupt-affinity setting?**

Ordinary Windows benchmark noise may lower confidence in the first answer. It must not turn otherwise valid candidate evidence into “no winner.” Structural evidence failures can still make evidence unrankable. Keep remains a separate safety/performance decision.

New evidence uses:

```text
method = gpu-affinity-benchmark-v3
report = latencypilot-gpu-auto-affinity-report-v3
```

Historical v1/v2 evidence is never reinterpreted as v3.

## Measurement shape

The existing local paired control remains because it cheaply compensates for time-local drift:

```text
Original before → Candidate → Original after
```

For higher-is-better metrics:

```text
reference = sqrt(originalBefore × originalAfter)
effect    = candidate / reference - 1
```

For lower-is-better frame p99:

```text
effect = reference / candidate - 1
```

Raw observations remain unchanged.

## Original variability

Before candidate mutation:

1. run the existing 5 s non-scored warm-up;
2. capture at least three 10 s scored Original observations;
3. use median plus relative median absolute deviation (MAD / median) as the robust center/noise estimate;
4. when the first three are noisy, extend to at most five scored observations;
5. retain all valid observations.

Broad but structurally valid variability does **not** stop candidate testing. It lowers later selection confidence.

## Pair drift

Local Original movement is still measured and a high-drift pair gets one fresh retry.

If the retry is still noisy but benchmark identity, stored state, metrics and other structural evidence are valid, the second pair remains rankable. Its larger control movement is persisted as uncertainty evidence. There is no “two noisy candidates stop the search” rule.

Structural failures — wrong state, invalid artifact identity, non-finite metrics, contradictory healthy placement evidence, failed rollback/recovery — remain fail-closed.

## Search and finalists

Stage A still screens one eligible logical representative per physical core. Stage B refines at most the top three physical-core hypotheses. Stage C advances at most the top three logical CPUs.

Finalists run three 30 s rounds with deterministic order shuffling. Persisted finalist evidence includes:

- median paired 1%-low effect;
- median paired AVG/frame-p99 effects;
- 1%-low effect MAD;
- positive-pair count;
- local noise guide;
- raw median Original and Candidate FPS/ms for user reporting.

Every structurally valid finalist is ranked by median paired 1%-low effect. A one-percentage-point margin remains a **practical-tie** indicator, not a reason to delete rank 1.

## Confidence

Selection confidence is explanatory metadata, never a ranking gate.

The source reports `High`, `Medium`, or `Low` from simple evidence already collected: winner lead over runner-up, finalist effect MAD, Original robust variability, positive-pair consistency and practical-tie state.

No Bayesian model, bootstrap simulation or hidden weighted score is introduced.

## Keep remains separate

The best observed CPU can exist even when Original is retained.

Automatic Keep currently requires:

- positive median 1%-low effect for the best observed CPU;
- no material median AVG/frame-p99 regression under the bounded Keep guardrail;
- no supported material GPU-driver interrupt-tail regression;
- final exact stored-state verification;
- final clean kernel-ETW target-only GPU ISR placement proof.

If Keep is not recommended or final placement cannot be proved, exact Original is restored **and the report still preserves the best observed CPU and confidence**.

## User-facing result

The normal result surface must show, where evidence exists:

- best observed CPU;
- confidence;
- actual median Original → Candidate values;
- absolute improvement in FPS/ms;
- paired percentage effect;
- practical-tie state;
- whether the best observed CPU was kept or Original was restored.

Ranking and terminal machine state are distinct facts.

## Rationale

This is intentionally smaller than a general statistical framework. Robust medians/MAD, repeated shuffled finalist rounds and local paired controls address real PC noise without demanding an isolated laboratory or turning a small sample into false precision.

References:

- NIST/SEMATECH e-Handbook, randomized block designs: https://www.itl.nist.gov/div898/handbook/pri/section3/pri332.htm
- Google Benchmark, repeated benchmarks and aggregate statistics: https://google.github.io/benchmark/user_guide.html
- AutoGpuAffinity, practical per-core GPU-affinity benchmark precedent: https://github.com/valleyofdoom/AutoGpuAffinity

## Consequences

Positive:

- a noisy but valid full run still produces a useful ranked answer;
- confidence communicates uncertainty;
- user-visible gain is expressed in FPS/ms as well as percentage effect;
- final mutation safety remains independent from ranking.

Tradeoffs:

- a low-confidence rank is explicitly a best estimate, not proof of superiority;
- physical Gate A must be rerun because v2 physical evidence cannot validate v3 ranking semantics.
