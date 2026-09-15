# Benchmark Methodology

Status: **V0.5 benchmark contract**
Last updated: 2026-09-15

LatencyPilot exists to distinguish measurable improvement from placebo, ordinary run-to-run variation, drift, or a trade-off hidden by one headline number. It is an experimental optimization platform, not a collection of assumed Windows tweaks.

This document defines the minimum methodology for benchmark-backed recommendations. Where implementation and this contract disagree, the discrepancy must be resolved explicitly; the product must not silently lower the evidence bar.

## 1. Evidence hierarchy

LatencyPilot intentionally separates three evidence levels.

### 1.1 Quick diagnostic snapshot

The current quick snapshot is:

```text
1 × 5-second DPC/ISR capture
```

Its purpose is limited to:

- confirming ETW capture integrity;
- checking module/routine attribution;
- locating per-CPU concentration;
- identifying obvious long-tail events;
- generating a fast hypothesis for what deserves controlled testing.

A quick snapshot is **not** a benchmark verdict, stability proof, system-health score, or optimization recommendation. Repeating several snapshots can strengthen a hypothesis, but does not silently convert those snapshots into a controlled baseline.

### 1.2 Repeated decision baseline

The implemented decision-grade Phase 2 baseline is `baseline-quality-v2`:

```text
LatencyPilot/service settle: 5 seconds
5 authoritative windows × 20 seconds
750 ms inter-window settle between completed windows
= 100 seconds of authoritative DPC/ISR measurement
```

The 5-second pre-sequence delay only lets LatencyPilot and the service settle. It does **not** claim to warm a game, compile shaders, stabilize clocks, fill application caches, or otherwise prepare the workload. For real-world and before/after scenarios, the workload must already be at a warmed and repeatable point before the user starts the decision baseline, unless startup/loading behavior is deliberately the workload under test.

Five separate windows are retained instead of one 100-second aggregate because LatencyPilot needs inter-window variation and early/late drift evidence, not only a larger sample pool.

### 1.3 Controlled A/B experiment

A mutation is not accepted because its post-change number looks better once. The intended experiment structure is:

```text
Environment snapshot
    ↓
Workload warm-up / stabilization
    ↓
Baseline measurement(s)
    ↓
Candidate mutation
    ↓
Applied-state verification
    ↓
Candidate measurement(s)
    ↓
Baseline re-check / drift check
    ↓
Statistical + practical comparison
    ↓
Guardrail evaluation
    ↓
Keep or Revert
```

Where practical, confirmation should use a balanced or interleaved order such as:

```text
A1 → B1 → B2 → A2
```

or a randomized/balanced equivalent such as ABBA/BAAB. This reduces the chance that temperature, background activity, clocks, cache state, or simple passage of time is mistaken for a candidate effect.

For reboot-requiring mutations, persist the complete experiment and rollback state across boots and use a reboot-aware ordering rather than pretending the two sides were contiguous.

## 2. Source hierarchy and independent judgment

LatencyPilot uses different sources for different questions.

- **Microsoft documentation** is authoritative for Windows API contracts, ETW semantics, resource descriptors, interrupt-policy meanings, driver guidance, and supported behavior.
- **Measured local evidence** is authoritative for whether a specific candidate helps a specific machine and workload.
- **Maintained tools/projects** such as PresentMon and AutoGpuAffinity are useful prior art for metrics and experimental design, but are not copied as product truth.
- **Community reports** can identify hypotheses and failure modes. They are anecdotes unless independently reproduced.

For example, Microsoft documents the 100 µs DPC and 25 µs ISR driver-duration guidance and the semantics of interrupt-affinity policies. Those values remain useful reference lines. They do **not** prove that staying below them means a user has no latency problem, and the default Windows interrupt placement is not assumed to be the latency optimum for every machine.

Likewise, repeated reports that graphics interrupts concentrate on CPU 0 can justify testing non-default candidates. They do **not** justify a universal rule that CPU 0 is bad.

References used when revising this contract include:

- Microsoft: DPC/ISR ETW measurement and event-loss guidance;
- Microsoft: interrupt affinity and WDF interrupt policy documentation;
- Intel/GameTechDev PresentMon metric definitions;
- `valleyofdoom/AutoGpuAffinity`, which performs per-candidate GPU-affinity measurements with a configurable 30-second benchmark interval and workload/cache settling;
- older AutoGpuAffinity documentation recommending repeated 30-second trials and whole-session reproducibility checks.

These references inform the methodology; LatencyPilot's own versioned evidence contract remains authoritative for LatencyPilot results.

## 3. Environment and provenance

Record enough context to interpret and reproduce an authoritative run, including where applicable:

- exact LatencyPilot product version and clean source revision;
- observation protocol version;
- evidence schema and analysis-method version;
- Windows edition/build and, where available, servicing revision;
- CPU model and topology;
- GPU/device identity and driver version;
- power source, plan and configured power mode;
- workload identity/version/scene or action loop;
- timestamps and requested/actual capture duration;
- applied mutation plus verified actual state;
- unique capture RequestIds.

Do not collect unnecessary personal data.

Evidence exported from a dirty or unverifiable source tree must not claim an exact clean source revision.

## 4. Warm-up versus settle

Warm-up reduces one-time effects such as:

- process startup;
- shader compilation;
- cache initialization;
- initial JIT/code-path initialization;
- level/scene loading;
- initial thermal/clock transitions.

A **workload warm-up** belongs to the benchmark definition and may require tens of seconds or longer depending on the workload.

A **LatencyPilot settle delay** only reduces observer-side transition effects around beginning a sequence. These concepts must not be conflated in code or UI.

Warm-up samples should not silently enter the authoritative window unless the benchmark explicitly defines startup behavior as part of the workload.

## 5. Capture integrity

A capture is not clean when any of the following is true:

- ETW loss count is unavailable;
- ETW reports one or more lost events;
- one or more latency events are invalid;
- one or more image-attribution events are invalid;
- the bounded event safety limit is reached.

Microsoft's ETW guidance explicitly requires monitoring lost events because a consumer or buffer configuration that cannot keep up can lose evidence. LatencyPilot therefore fails closed for decision-grade use rather than estimating around missing events.

Contributor-list truncation is reported separately. Raw aggregate integrity and the completeness of displayed contributor lists are different concepts.

## 6. `baseline-quality-v2`

The Phase 2 repeated baseline quality method is versioned independently of later A/B experiment verdict logic.

A complete v2 baseline requires exactly five contiguous `WindowNumber` records (`1..5`). `StartedAtUtc` is provenance, not a monotonic sequence clock; an NTP or manual wall-clock correction must not reorder an in-process sequence. If persisted/reconstructed experiments later require a stronger ordering guarantee, add an explicit monotonic sequence field.

Each window must satisfy all of the following:

```text
requested duration >= 20,000 ms
actual duration >= 95% of requested duration
capture integrity = clean
DPC sample count >= 1,000
ISR sample count >= 1,000
DPC p99 exists, finite, positive
ISR p99 exists, finite, positive
```

The 1,000-event rule is a product adequacy threshold for using a window-level p99 in the current stability screen. It is not a confidence interval and does not claim that 1,000 samples are sufficient for every tail statistic.

For each metric family, eligible window-level p99 values are summarized as:

```text
median = P50(window p99 values)
relative noise floor = (P90 - P10) / abs(median)
relative drift = abs(lateMedian - earlyMedian) / abs(overallMedian)
```

The metric is inconclusive when:

- P10–P90 relative spread exceeds 30%;
- early/late relative drift exceeds 20%;
- any eligible window deviates by more than 50% from the overall median;
- any required window is missing, short, dirty or undersampled.

Extreme windows are reported, never silently deleted.

The overall baseline is `Valid` only when all five captures are clean and both DPC p99 and ISR p99 pass duration, sample-adequacy, noise, drift and extreme-window checks. Otherwise the baseline is `Inconclusive` with explicit reasons.

A `Valid` baseline means **repeatable enough for the current comparison method**. It does not mean the machine is healthy, fast, optimally configured, or within a universal latency target.

## 7. Distribution reporting and canonical percentile rule

Do not rely on averages alone. For latency-like distributions, retain or calculate as applicable:

- sample count;
- mean;
- p50;
- p90;
- p95;
- p99;
- p99.9 when adequately sampled;
- maximum;
- standard deviation where meaningful;
- median absolute deviation or another robust dispersion measure where useful;
- outlier/tail information without silently deleting valid events.

The current observation response exposes a narrower Phase 2 subset: count, p50, p95, p99, conditionally p99.9 and max. The broader list remains a future comparison/analytics requirement; do not misrepresent the narrower response as the final statistical model.

Percentiles use the canonical deterministic `linear-n-minus-one-v1` estimator implemented by `LatencyPilot.Benchmarking.Statistics.Percentiles`:

```text
position = (n - 1) * p
lower = floor(position)
upper = ceil(position)
value = samples[lower] + (samples[upper] - samples[lower]) * (position - lower)
```

Expected ordering:

```text
p50 <= p90 <= p95 <= p99 <= p99.9 <= max
```

No subsystem may silently introduce a different estimator for a metric with the same name.

## 8. p99.9 adequacy

Protocol v6 exposes p99.9 only when a distribution has at least:

```text
10,000 samples
```

The old 1,000-sample floor was deliberately rejected after review because a nominal p99.9 based on roughly one expected top-0.1% observation is too fragile to present prominently as decision evidence.

Ten thousand samples still do **not** establish a formal confidence guarantee; they provide roughly ten expected samples in the top 0.1% and are a stricter product adequacy floor. More demanding experiment layers may require more samples or resampling confidence intervals.

Calculation availability and evidence adequacy are separate concepts. The raw percentile estimator can calculate a mathematical p99.9 from a small vector; the observation protocol decides whether that statistic is adequate to expose.

## 9. Microsoft guidance and local diagnostic buckets

Current DPC/ISR presentation may retain:

- DPC `>100 µs`: Microsoft driver-duration guidance reference;
- ISR `>25 µs`: Microsoft driver-duration guidance reference;
- `>1 ms`: LatencyPilot local diagnostic bucket;
- `>3 ms`: LatencyPilot local diagnostic bucket.

These are **context/reference lines**, not the optimizer objective and not a Windows health score.

A single threshold exceedance is not proof of user-visible impact. Conversely, having no exceedance in a five-second snapshot is not proof that a system is consistently clean.

A global exceedance percentage can also be misleading when the denominator changes because another module emits many short events. Therefore decisions should use the underlying distributions, module attribution, per-CPU concentration, repeated-window behavior, and workload-specific guardrails rather than ranking candidates by one global exceedance rate.

## 10. CPU concentration and affinity hypotheses

Per-CPU DPC/ISR concentration is observation evidence. It becomes a candidate-selection signal only after repeated measurement.

Rules:

- CPU 0 is not automatically faulty or excluded.
- Default Windows policy remains the control candidate unless the experiment explicitly defines another control.
- A candidate core is never accepted solely because it is not CPU 0.
- Processor groups, physical cores and SMT siblings must be represented correctly.
- Prefer physical-core-aware screening instead of blindly iterating every logical processor as if all candidates were independent.
- Candidate application must be verified before measurement.
- Original affinity/MSI state must be snapshotted exactly and rollback must be available.

Microsoft policy semantics define what LatencyPilot is allowed to set. Measurement decides whether a setting should be kept.

## 11. Candidate search strategy

Exhaustively running every possible candidate at decision-grade duration may be unnecessarily slow. The optimizer should use a staged search once mutation is enabled.

### Screening

Use topology and prior observation to remove invalid/duplicate candidates and cheaply identify plausible finalists. Screening must not itself be represented as final proof.

Possible signals include:

- physical-core identity and SMT relationships;
- existing DPC/ISR concentration;
- module-specific graphics activity;
- obvious contention;
- hardware/resource constraints.

### Confirmation

Confirm the control and a small finalist set with longer, balanced repeated measurements. A practical starting design for GPU affinity is approximately 30 seconds per authoritative A/B run, with repeated/interleaved ordering. The exact duration and count must be versioned when implemented and may be extended when sample adequacy or variance demands it.

AutoGpuAffinity's 30-second candidate interval and repeated-trial guidance are useful evidence that single five-second candidate runs are too weak, but LatencyPilot does not inherit its ranking formula blindly.

## 12. Primary, guardrail and context metrics

Every optimization domain must define three groups before automatic recommendations are enabled.

### Primary metrics

What the experiment is actually trying to improve.

### Guardrail metrics

Signals that prevent a local optimization from causing a worse overall system.

### Context metrics

Useful explanatory state that should not independently turn a candidate into a winner.

A candidate with a primary improvement plus a material guardrail regression is a `Tradeoff`, not silently an improvement.

## 13. GPU experiment contract

DPC/ISR evidence alone is not sufficient for a final GPU-affinity recommendation.

Potential primary metrics include:

- GPU-driver DPC/ISR p99 and adequately sampled p99.9;
- total DPC/ISR tail behavior;
- accumulated DPC/ISR duration;
- per-CPU concentration;
- frame-time distribution;
- PresentMon CPU busy/wait and GPU busy/wait;
- GPU latency and display latency where available;
- displayed/presented FPS and dropped-frame behavior where meaningful.

Potential guardrails include:

- Raw Input interval/jitter when the profile cares about input;
- USB/xHCI DPC behavior;
- NDIS/network DPC behavior;
- audio glitch/underrun evidence when available;
- CPU contention;
- crashes/device resets/errors;
- power/thermal context when authoritative telemetry is available.

PresentMon provides per-frame timing, CPU/GPU busy/wait, GPU latency, display latency and related metrics. LatencyPilot should use those metrics when they actually apply to the workload, rather than inventing an opaque FPS/latency score.

## 14. Input measurements

Raw Input can characterize application-observable report arrival behavior such as:

- report intervals;
- interval jitter;
- burst/coalescing behavior;
- missing/irregular reports visible to the application.

Raw Input alone does not establish physical switch-to-photon latency. Do not label it that way. Hardware end-to-end claims require appropriate external measurement hardware and methodology.

## 15. Network measurements

Internet path latency is uncontrolled and should not be the sole primary signal for NIC/RSS tuning.

Prefer controlled/local peers where possible, combined with Windows/NDIS/RSS telemetry. Potential signals include:

- RTT distribution;
- jitter;
- loss;
- throughput guardrail;
- NDIS DPC/ISR distribution;
- RSS processor distribution;
- CPU utilization/context.

## 16. Drift and invalid experiments

An experiment may become inconclusive when the environment changes materially between sides. Examples include:

- A1/A2 baseline disagreement;
- workload/scene mismatch;
- power-state change;
- major background-load change;
- device/driver state change;
- benchmark crash/termination;
- thermal/clock change when authoritative telemetry shows it;
- dirty/lost ETW evidence.

Do not invent a favorable verdict around invalid evidence. Until a separate persisted invalid-state type is introduced, use `Inconclusive` plus explicit validity reasons.

## 17. Statistical comparison

Latency distributions may be skewed, multimodal and heavy-tailed. Do not assume normality without evidence.

Where appropriate, later comparison versions may use bootstrap/resampling confidence intervals for deltas or summary statistics. A confidence interval crossing the no-effect boundary must not be called a confirmed improvement simply because the point estimate is favorable.

The statistical method, practical-effect threshold and noise interpretation must be versioned. Historical evidence must not silently change meaning when the implementation evolves.

## 18. Verdict model

### Implemented GPU confirmation interpretation — `gpu-affinity-confirmation-v1`

Screening chooses a finalist for confirmation; it never produces a Keep recommendation. The confirmation interpreter requires the already-valid five-window `baseline-quality-v2` result and exactly eight completed runs in `ABBA + BAAB` order. Baseline and runs must share session, workload, environment and exact source-revision provenance. Each run has its own capture identity and explicitly verified actual original/candidate state and capture integrity. An Original run means the exact captured original was verified, not merely that a default policy was requested.

Every requested interval is equal and at least 30 seconds; actual duration must reach 95% of the request. Each primary and guardrail distribution requires at least 1,000 samples per run (or the higher configured minimum). Missing/incompatible metrics, reused capture IDs, dirty captures, changed environment/workload/source, unverified placement or a partial/reordered sequence yield `Inconclusive` and `RestoreOriginal`. The collection layer must supply all required workload guardrails and derive every identity/verification field from actual evidence; the interpreter is not an authorization or hardware-verification mechanism.

For nonnegative latency/duration samples the per-run statistic is p99; higher-is-better throughput distributions use p01 to preserve adverse low-throughput tails. The named dropped-frame-ratio metric uses its arithmetic mean over [0,1] observations, so rare dropped frames do not disappear below a p99 cutoff. A PresentMon dynamic aggregate is one aggregate observation, not hundreds of raw frame samples: the current aggregate reader cannot by itself satisfy the per-frame confirmation contract.

The four Original and four Candidate run statistics are retained separately. Each side uses its median, `(P90-P10)/median` noise, first-two/last-two median drift and extreme deviation. Noise >30%, drift >20% or any run >50% from its side's median makes evidence inconclusive. A zero median is stable only when every run statistic on that side is zero.

The effective practical threshold is the maximum of the configured primary/guardrail threshold and measured noise/drift from both sides. A positive effect must exceed this threshold to count as improved; a negative effect beyond it is regressed. This is a conservative versioned practical-noise rule, **not a confidence interval or statistical-significance claim**. Zero-to-zero is unchanged; a new adverse value from a zero control is a regression with an undefined percentage, preserving its raw delta. Undefined favorable relative changes remain inconclusive.

Results retain raw metric deltas, statistic/percentile identity, original/candidate sample counts, noise/drift, effective thresholds, the run evidence and explicit five-way verdict. Only `Improved` without a guardrail regression recommends `KeepCandidate`; all other verdicts recommend exact restoration. A recommendation never terminalizes a journal: actual keep/rollback verification belongs to the execution layer. Physical calibration, full guardrail selection and the real measurement/execution loop remain required before arming.

The authoritative verdict set remains exactly:

### `Improved`

Target metric(s) improve beyond the configured/measured practical-noise boundary, the experiment is valid, applied state is verified, and no material guardrail regression invalidates the benefit.

### `Regressed`

The candidate measurably worsens the target, or a target that is otherwise neutral is accompanied by a material guardrail regression.

### `Tradeoff`

At least one meaningful target improves while another important target/guardrail measurably worsens.

### `NoMeasurableDifference`

The observed delta is indistinguishable from normal variation or below the documented practical-effect threshold.

### `Inconclusive`

Evidence is insufficient, dirty, drifted, undersampled, unverified, or too uncertain to classify reliably.

Quick diagnostic snapshots do not receive one of these experiment verdicts.

## 19. Practical significance

Statistical confidence alone is insufficient. A tiny detectable delta may have no useful effect.

Each metric family may define a practical-effect threshold based on resolution, normal variation and user-visible relevance. Keep that threshold visible/auditable.

## 20. Composite scores

A composite score may be a convenience but must never replace the authoritative vector of raw/derived results.

Do not collapse:

```text
GPU latency improved
Network jitter regressed
Input unchanged
```

into an unexplained `94/100` recommendation.

## 21. Workload profiles

Profiles may prioritize metrics differently for:

- general responsiveness;
- competitive gaming;
- audio/DAW;
- streaming/content creation;
- networking.

Profiles affect recommendation weighting, not raw measurements.

## 22. Persistence and auditability

Each authoritative benchmark run should eventually retain enough information to audit the verdict later, including:

- experiment ID;
- run role (baseline/candidate/validation/control);
- schema/protocol/method versions;
- workload definition/version;
- requested/actual capture interval;
- raw-sample reference or sufficient auditable representation;
- sample counts;
- statistical outputs;
- environment context;
- validity flags;
- mutation requested state and verified actual state;
- application/source version.

Historical results must not silently change interpretation after algorithm upgrades.

## 23. Observer effect

LatencyPilot itself can perturb the system it measures. The capture path should avoid unnecessary:

- per-event heap allocation;
- high-volume logging;
- synchronous file I/O;
- frequent UI redraws;
- avoidable GC pressure.

Detailed UI is intentionally not redrawn between authoritative repeated-baseline windows. Optimize the observer only when profiling identifies meaningful overhead; do not change event semantics or percentile meaning merely to make the observer faster.

## 24. Permanent-test cap

The repository intentionally caps permanent automated tests at 10. High-value statistical scenarios should therefore be consolidated inside durable contract tests rather than consuming one test method per edge case.

The existing repeated-baseline contract test should cover stable, drifted, dirty, too-short, undersampled and sequence-invalid evidence where readable. Temporary investigative tests may be created, run and deleted during implementation.

The test cap must never be used as a reason to weaken an important invariant. If a future recovery/mutation invariant is more important, merge or retire a lower-value permanent case rather than exceeding the cap casually.

## 25. Phase boundary

Phase 2 closes only when the read-only measurement substrate has physical evidence that:

- current App and Service build/run together on exact clean source;
- protocol v6 capture works with no stale-service mismatch;
- quick snapshot integrity/attribution works as diagnostic evidence;
- evidence schema v8 carries exact provenance and purpose;
- both real-world and controlled-idle `baseline-quality-v2` sequences can be physically exercised and verified;
- required device evidence, cleanup/recovery and accessibility checks pass;
- no mutation path is exposed.

ADR 0004 supersedes strict source-development sequencing: internal Phase 3 work may overlap the remaining Phase 2 physical record after the required Real-world baseline exists. Public mutation still follows the separate Gate A → B → C → D physical/authorization sequence in `PROJECT_STATUS.md`. The first mutation implementation must preserve the same principle that motivated this revision: measure the machine, do not assume the tweak.
