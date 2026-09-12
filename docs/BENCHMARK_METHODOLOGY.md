# Benchmark Methodology

Status: **V0.1 benchmark contract**

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

## 6. Metrics

Metrics are categorized as:

### Primary / target metrics

Measurements the experiment is specifically intended to improve.

### Guardrail metrics

Measurements that detect collateral regressions elsewhere.

### Context metrics

Measurements that help explain the run but do not directly determine success.

Every optimization domain must document its metric set before automatic recommendations are enabled.

## 7. Distribution reporting

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

## 8. Sample adequacy

Do not calculate or emphasize extreme percentiles from obviously inadequate sample counts.

The benchmark implementation must define minimum sample rules for each metric family.

If evidence is insufficient, return `Inconclusive` instead of extrapolating confidence.

## 9. Statistical comparison

Latency data may be skewed, multimodal, and heavy-tailed. Do not assume a normal distribution without evidence.

Where appropriate, LatencyPilot may use bootstrap/resampling confidence intervals for deltas or summary statistics.

A confidence interval that crosses a no-effect boundary should not be labeled a confirmed improvement merely because the point estimate is favorable.

The exact statistical method must be versioned/documented so historical results remain interpretable.

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

Drift should produce `InvalidExperiment` or `Inconclusive` when attribution is no longer trustworthy.

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

CPU0 concentration must be reported as an observation, not automatically classified as a fault.

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

Authoritative result classes:

### ConfirmedImprovement

The target metric(s) improve beyond the measured noise/uncertainty, no material guardrail regression invalidates the benefit, and the experiment is valid.

### ConfirmedRegression

The candidate measurably worsens the target or causes a clearly unacceptable guardrail regression.

### TradeOff

At least one meaningful target improves while another important target/guardrail measurably worsens.

### NoMeasurableDifference

The observed delta is small enough to be indistinguishable from measured normal variation or otherwise fails the practical-effect threshold.

### Inconclusive

Evidence is insufficient or uncertainty remains too high to classify the candidate reliably.

### InvalidExperiment

The benchmark cannot support attribution due to drift, failed verification, workload failure, state mismatch, insufficient integrity, or another validity failure.

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

## 20. Synthetic statistical tests

The Benchmarking test suite must include generated datasets for:

- identical A/B distributions;
- known positive shift;
- known negative shift;
- heavy-tailed distributions;
- isolated extreme spikes;
- multimodal distributions;
- low sample count;
- baseline drift;
- primary improvement plus guardrail regression.

Expected verdicts must be asserted.

## 21. Golden telemetry tests

For small approved trace fixtures:

```text
fixture input
→ parser
→ normalized events
→ aggregation
→ expected JSON/result
```

Parser updates must not silently change expected metrics. Any intended semantic change requires fixture expectation review and documentation.

## 22. Benchmark performance vs benchmark correctness

LatencyPilot's own parser/statistics performance should be measured in `perf/`, but CI timing on hosted VMs is not a substitute for real hardware experiments.

Correctness tests may gate pull requests. Small hosted-runner performance deltas should generally be tracked rather than treated as authoritative hardware regressions.

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
