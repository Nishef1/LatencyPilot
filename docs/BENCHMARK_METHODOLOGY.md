# Benchmark Methodology

Status: **V0.3 benchmark contract**  
Last updated: 2026-09-13

LatencyPilot exists to distinguish measurable improvement from placebo, noise, drift, or a trade-off hidden by a single headline number.

This document defines the minimum methodology for benchmark-backed recommendations.

## 1. Principles

1. A before/after percentage alone is not evidence.
2. Baseline variability must be measured.
3. Tail latency matters.
4. Target metrics and guardrail metrics are distinct.
5. Repeated runs are preferred over a single pair.
6. Raw or auditable measurements must be preserved.
7. Invalid and inconclusive experiments are valid outcomes.
8. A primary improvement can still produce a trade-off verdict.
9. GitHub-hosted CI is not a physical-hardware benchmark environment.
10. The benchmark must not claim to measure a physical quantity it does not actually observe.
11. Measurement-engine observer overhead is itself a validity concern; avoid introducing avoidable allocation, GC, logging or synchronous I/O pressure into the measured window.

## 2. Experiment structure

Conceptually:

```text
Environment snapshot
    ↓
Warm-up
    ↓
Baseline runs
    ↓
Candidate mutation
    ↓
Verification
    ↓
Candidate runs
    ↓
Baseline validation / drift check
    ↓
Statistical comparison
    ↓
Guardrail evaluation
    ↓
Verdict
```

Where safe and practical, prefer interleaved sequences such as:

```text
A1 → B1 → B2 → A2
```

rather than only:

```text
A → B
```

For changes requiring reboot, use a reboot-aware design and persist the complete experiment state across boots.

## 3. Environment capture

Record enough context to make the result interpretable, including where applicable:

- Windows edition/build;
- LatencyPilot version;
- CPU model and topology;
- GPU model and driver version;
- target device identity and driver version;
- power mode/profile if relevant;
- workload identity/version;
- experiment timestamps;
- benchmark duration;
- applied mutation and verified actual state.

Do not collect unnecessary personal data.

## 4. Warm-up

Warm-up exists to reduce one-time effects such as:

- process startup;
- shader/cache initialization;
- initial JIT or code-path initialization;
- workload loading;
- initial thermal/clock transitions.

Warm-up measurements should not silently mix into the authoritative measurement window unless the benchmark explicitly defines them as part of the workload.

## 5. Baseline noise floor

Before interpreting a candidate delta, LatencyPilot must estimate normal baseline variability for the relevant metrics.

At minimum, compare repeated baseline windows/runs and quantify expected variation.

A candidate difference smaller than normal baseline variation should not be marketed as an improvement.

Possible classifications:

```text
NoMeasurableDifference
Inconclusive
```

rather than `Improved`.

### 5.1 `baseline-quality-v1`

The first implemented repeated-baseline quality gate is deliberately conservative and versioned separately from the later A/B experiment verdict model.

Current App capture protocol:

```text
5 sequential windows
× 5 seconds each
+ 750 ms spacing between completed windows
```

Window ordering is defined by the explicit contiguous `WindowNumber` sequence (`1..N`). `StartedAtUtc` is provenance, not a monotonic clock. A wall-clock adjustment from NTP, VM synchronization or manual time correction must not by itself invalidate an otherwise contiguous in-process capture sequence. Future persisted/reconstructed evidence that needs stronger temporal guarantees should add an explicit monotonic/sequence field rather than treating UTC wall clock as monotonic.

For each window, the current quality gate records DPC p99 and ISR p99 together with event counts and capture-integrity provenance.

A window is not clean when any of the following is true:

- ETW loss count is unavailable;
- ETW reports one or more lost events;
- one or more latency events are invalid;
- one or more image-attribution events are invalid;
- the configured event safety limit is reached.

A metric value is analyzable for a window only when:

- the window itself is capture-integrity clean;
- at least 20 events exist for that metric family in the window;
- p99 exists, is finite, and is positive.

For eligible window-level p99 values:

```text
median = P50(values)
relative noise floor = (P90(values) - P10(values)) / abs(median)
```

The baseline is inconclusive when the relative noise floor is greater than 30%.

Drift is estimated by comparing the median of the early half of eligible windows with the median of the late half:

```text
relative drift = abs(lateMedian - earlyMedian) / abs(overallMedian)
```

The baseline is inconclusive when relative drift is greater than 20%.

Any eligible window whose metric value is more than 50% away from the overall median is explicitly reported as an extreme window. It is **not silently removed**; its presence makes that metric quality inconclusive under this method.

The overall repeated baseline is `Valid` only when all required windows exist, every capture is clean, and both DPC p99 and ISR p99 pass sample-adequacy, noise, drift and extreme-window checks. Otherwise the quality result is `Inconclusive` with explicit reasons.

These 20-event/30%-noise/20%-drift/50%-extreme thresholds are quality-gate policy values, not confidence intervals and not claims of statistical significance. They may be revised only by versioning/documenting the interpretation so historical evidence remains understandable.

Background-load and thermal/power warnings remain separate open work until LatencyPilot has authoritative, sufficiently low-overhead evidence for those signals. Absence of those warnings must not be represented as proof that background/thermal state was stable.

## 6. Metrics

Metrics are categorized as:

### Primary / target metrics

Measurements the experiment is specifically intended to improve.

### Guardrail metrics

Measurements that detect collateral regressions elsewhere.

### Context metrics

Measurements that help explain the run but do not directly determine success.

Every optimization domain must document its metric set before automatic recommendations are enabled.

## 7. Distribution reporting and percentile definition

Do not rely on averages alone.

For latency-like distributions, retain or calculate as applicable:

- sample count;
- mean;
- p50;
- p90;
- p95;
- p99;
- p99.9 when sample count supports it;
- maximum;
- standard deviation where meaningful;
- median absolute deviation or another robust dispersion measure where useful;
- outlier information without silently deleting valid tail events.

Percentiles must satisfy ordering invariants such as:

```text
p50 <= p90 <= p95 <= p99 <= p99.9 <= max
```

LatencyPilot currently uses one canonical deterministic estimator, implemented by `LatencyPilot.Benchmarking.Statistics.Percentiles`.

For an ascending sorted sample vector of length `n` and percentile fraction `p` in `[0, 1]`:

```text
position = (n - 1) * p
lower = floor(position)
upper = ceil(position)
value = samples[lower] + (samples[upper] - samples[lower]) * (position - lower)
```

When `lower == upper`, that sample is returned directly. This is the **linear-n-minus-one-v1** interpretation for current results. Service observation summaries, repeated-baseline quality and benchmark comparisons must call this same implementation rather than defining local nearest-rank variants.

The Phase 2 observation protocol has an additional presentation/evidence-adequacy rule: `p99.9` is omitted (`null`) when that distribution has fewer than **1,000 samples**. This is a conservative minimum chosen so the named 99.9th-percentile tail is not prominently reported when the sample set contains fewer than roughly one expected observation in the top 0.1%. It is a product adequacy policy, not a statistical-confidence interval. p50/p95/p99/max remain available according to their existing contracts. The raw estimator can still mathematically calculate p99.9 for deterministic tests; the Service decides whether the result is adequate to expose as observation evidence.

If this estimator or the p99.9 adequacy policy changes later, the method/protocol interpretation must change with it so historical results remain interpretable.

## 8. Sample adequacy

Do not calculate or emphasize extreme percentiles from obviously inadequate sample counts.

The benchmark implementation must define minimum sample rules for each metric family.

If evidence is insufficient, return `Inconclusive` or omit that derived tail statistic instead of extrapolating confidence.

A percentile can be mathematically calculated from a small sample while still being statistically inadequate for an authoritative decision. Calculation availability and evidence adequacy are separate concepts.

## 9. Statistical comparison

Latency data may be skewed, multimodal, and heavy-tailed. Do not assume a normal distribution without evidence.

Where appropriate, LatencyPilot may use bootstrap/resampling confidence intervals for deltas or summary statistics.

A confidence interval that crosses a no-effect boundary should not be labeled a confirmed improvement merely because the point estimate is favorable.

The exact statistical method must be versioned/documented so historical results remain interpretable. A new layer must not silently introduce a different percentile/noise interpretation for the same named metric.

## 10. Drift detection

An experiment can become invalid if the environment changes materially between baseline and candidate runs.

Potential drift indicators include:

- baseline A1 vs A2 divergence;
- significant thermal or clock-state change;
- workload mismatch;
- background load spike;
- device/driver state change;
- power-state change;
- unexpected process or benchmark termination.

Until a separate persisted invalid-experiment state is intentionally introduced, a run that cannot support attribution because of drift or failed validity checks produces an `Inconclusive` verdict with explicit validity reasons. Do not invent a sixth verdict in one layer only.

## 11. DPC/ISR analysis

Where ETW data permits, collect/derive:

- DPC/ISR duration;
- event count;
- per-CPU distribution;
- module/driver attribution;
- function attribution where resolvable;
- interrupt vector/message information where available;
- total duration by driver and CPU;
- time-windowed spikes;
- p50/p95/p99/p99.9/max as sample counts permit.

Current observation presentation distinguishes documented driver guidance from local diagnostic buckets:

- DPC `> 100 µs` and ISR `> 25 µs` are presented as Microsoft driver guidance thresholds;
- `> 1 ms` and `> 3 ms` are retained as useful local tail-count buckets only. They are **not** represented as official Windows pass/fail, severity or user-impact boundaries.

A single threshold exceedance is evidence to investigate in context, not automatic proof that a driver caused a user-visible problem. CPU0 concentration likewise must be reported as an observation, not automatically classified as a fault.

## 12. GPU experiment metrics

For GPU interrupt-affinity experiments, potential primary metrics include:

- GPU-driver DPC/ISR tail behavior;
- total DPC/ISR tail behavior;
- per-CPU interrupt/DPC concentration;
- frame-time distribution;
- PresentMon CPU/GPU timing metrics where the workload supports them;
- displayed/presented frame behavior where meaningful.

Potential guardrails include:

- Raw Input interval/jitter;
- USB/xHCI DPC behavior;
- NDIS/network DPC behavior;
- audio glitches/underruns where measurable;
- CPU-core contention;
- stability/errors;
- power/thermal context where available.

A workload must be repeatable enough for the selected metrics to be meaningful.

## 13. Input measurements

Raw Input can characterize report arrival behavior such as:

- report intervals;
- interval jitter;
- burst/coalescing behavior;
- missing/irregular reports as observable by the application.

Raw Input alone does **not** establish physical switch-to-photon latency. LatencyPilot must not label it as such.

Hardware-level end-to-end latency claims require appropriate external measurement hardware and methodology.

## 14. Network measurements

Internet path latency is uncontrolled and should not be the sole primary metric for NIC/RSS tuning.

Prefer controlled or local measurements where possible, combined with Windows/NDIS/RSS telemetry.

Possible metrics include:

- RTT distribution;
- jitter;
- packet loss;
- throughput guardrail;
- NDIS DPC/ISR distribution;
- RSS processor distribution;
- CPU utilization/context.

## 15. Verdict model

The authoritative `ExperimentVerdict` values are exactly:

### `Improved`

The target metric(s) improve beyond the configured/measured practical-noise boundary, no material guardrail regression invalidates the benefit, and the experiment is valid enough to classify.

### `Regressed`

The candidate measurably worsens the target, or a target that is otherwise neutral is accompanied by a material guardrail regression.

### `Tradeoff`

At least one meaningful target improves while another important target/guardrail measurably worsens.

### `NoMeasurableDifference`

The observed delta is small enough to be indistinguishable from the configured/measured normal variation or otherwise fails the practical-effect threshold.

### `Inconclusive`

Evidence is insufficient, invalid, drifted, unverified, or uncertainty remains too high to classify the candidate reliably.

Validity reasons such as failed apply verification, workload mismatch, drift or capture-integrity failure are recorded separately from the five-value verdict. Changing the verdict set requires an intentional domain/schema decision, not documentation-only terminology.

## 16. Practical significance

Statistical confidence alone is not enough. A tiny but statistically detectable change may have no practical value.

Each metric family may define a practical-effect threshold based on measurement resolution, noise, and user-visible relevance.

Do not hide this threshold.

## 17. Composite scores

A composite score may be presented as a convenience but must never replace the underlying metrics or authoritative vector of results.

A score must not transform:

```text
GPU latency improved
Network jitter regressed
Input unchanged
```

into an unexplained `94/100` recommendation.

The raw target/guardrail outcome remains authoritative.

## 18. Workload profiles

Profiles may prioritize metrics differently for:

- general responsiveness;
- competitive gaming;
- audio/DAW;
- streaming/content creation;
- networking.

Profiles must not alter raw data. They influence recommendation weighting only.

## 19. Persistence

Each authoritative benchmark run should retain enough information to audit the verdict later, including:

- experiment ID;
- run role (baseline/candidate/validation);
- metric definition version;
- workload definition/version;
- capture interval;
- raw sample reference or sufficient summary representation;
- sample count;
- statistical outputs;
- environment context;
- validity flags;
- application version.

Historical results should not silently change meaning when analysis algorithms evolve. Store algorithm/schema versions.

## 20. Synthetic statistical scenarios under the permanent-test cap

The repository intentionally caps permanent automated tests at 10. The benchmark suite therefore does **not** create one permanent test method for every dataset shape.

High-value statistical scenarios should be consolidated into data/scenario matrices inside durable contract tests where that remains readable. Relevant scenarios over the lifetime of the project include:

- identical or near-identical A/B distributions;
- known positive shift;
- known negative shift;
- heavy-tailed distributions;
- isolated extreme spikes;
- multimodal distributions;
- low sample count;
- baseline drift;
- primary improvement plus guardrail regression.

The Stage C repeated-baseline gate intentionally uses one permanent scenario test to cover stable, drifted and capture-integrity-failed baselines rather than consuming multiple permanent slots. The same contract also verifies that missing/gapped window numbers fail while a backwards UTC wall-clock adjustment does not invalidate an otherwise contiguous capture sequence.

Not all scenarios must occupy independent permanent slots at the same time. When a later recovery/mutation risk is more important, merge or retire a lower-value scenario/test rather than violating the cap. Temporary investigative tests may be used during implementation and deleted before finalization.

## 21. Golden telemetry fixtures

A small approved trace fixture can be valuable when parser/attribution semantics become a sufficiently high-blast-radius risk:

```text
fixture input
→ parser
→ normalized events
→ aggregation
→ expected result
```

A golden fixture is not automatically an additional permanent test. Under the 10-test rule it must either share an existing durable contract or replace a lower-value permanent test. Parser updates must not silently change an approved fixture expectation; an intended semantic change requires explicit expectation review and methodology documentation.

Physical ETW/hardware validation remains separate from synthetic golden data.

## 22. Benchmark performance vs benchmark correctness

LatencyPilot's own capture/parser/statistics performance is part of measurement validity because the observer can perturb the machine it is measuring. The capture path should avoid avoidable per-event heap allocation, synchronous file I/O and high-volume diagnostic logging, and should keep bounded intermediate materialization where exact evidence semantics permit it.

Performance work must preserve the authoritative event meaning, attribution rules and statistical estimator. Do not trade exactness or silently change percentile semantics merely to reduce allocations.

Measure LatencyPilot's own allocation/GC/CPU overhead in `perf/` only when profiling shows a meaningful need. Do not create a speculative performance-test subsystem merely because one may be useful later. Physical or controlled profiling evidence should guide deeper optimization, especially when value-type copies, pooling or streaming aggregation could introduce new trade-offs.

GitHub Actions timing is not a benchmark signal. Hosted Actions runs the permanent correctness suite and compiles the Windows App/Service hosts as a buildability gate; publish/package/release and performance evidence remain owner-local or physical as appropriate.

## 23. User-facing presentation

For each experiment, the UI should expose at minimum:

```text
What changed?
Was it actually applied and verified?
What workload was measured?
How many runs/samples?
What changed in the target metrics?
What changed in guardrails?
How large was normal baseline noise?
What uncertainty remains?
What is the verdict?
Can it be reverted?
```

The user must be able to make a different keep/revert decision than the profile recommendation when a trade-off exists.

## 24. Rule for new optimization domains

A new optimizer cannot become automatic until its benchmark specification defines:

- applicability;
- controlled variable;
- target metrics;
- guardrails;
- workload;
- repetition strategy;
- noise/drift handling;
- validity conditions;
- verdict rules;
- recovery/revert behavior.

If those are not known, the feature remains observational or experimental.
