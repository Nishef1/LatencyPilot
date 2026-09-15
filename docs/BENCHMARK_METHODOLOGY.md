# Benchmark Methodology

Status: **V0.6 benchmark contract**  
Last updated: 2026-09-15

LatencyPilot exists to distinguish measurable improvement from placebo, ordinary run-to-run variation, drift, or a trade-off hidden by one headline number. It is an experimental optimization platform, not a collection of assumed Windows tweaks.

Where implementation and this contract disagree, resolve the discrepancy explicitly. Never silently lower the evidence bar to make an experiment pass.

## 1. Evidence hierarchy

### 1.1 Quick diagnostic snapshot

```text
1 × 5-second DPC/ISR capture
```

Purpose: ETW integrity, module/routine attribution, per-CPU concentration, obvious tail events and hypothesis generation.

It is **not** a benchmark verdict, stability proof, health score or optimization recommendation.

### 1.2 Repeated decision baseline — `baseline-quality-v2`

```text
LatencyPilot/service settle: 5 seconds
5 authoritative windows × 20 seconds
750 ms inter-window settle
```

The workload must already be warmed/repeatable unless startup/loading behavior is intentionally under test. Five windows are retained because inter-window variation and drift matter.

Each window requires:

```text
requested duration >= 20,000 ms
actual duration >= 95%
clean capture integrity
DPC count >= 1,000
ISR count >= 1,000
finite positive DPC p99
finite positive ISR p99
```

Across window-level p99 values:

```text
relative noise = (P90 - P10) / |median| <= 30%
early/late relative drift <= 20%
extreme-window deviation <= 50%
```

No inconvenient window is deleted. `Valid` means repeatable enough for the current method, not that the machine is globally healthy.

### 1.3 Controlled A/B experiment

A mutation is never accepted because one post-change number looks better.

```text
Environment/provenance snapshot
→ workload already warmed/stable
→ authoritative control evidence
→ candidate apply
→ actual-state verification
→ candidate evidence
→ balanced control/candidate rechecks
→ target + guardrail comparison
→ Keep or exact Revert
→ final-state verification
```

Use balanced/interleaved ordering where practical. GPU confirmation v1 uses a fixed eight-run `ABBA + BAAB` schedule.

Reboot-requiring experiments must persist exact experiment/rollback state across boots instead of pretending both sides were contiguous.

## 2. Source hierarchy

- Microsoft documentation is authoritative for Windows API/resource/ETW semantics and supported behavior.
- Measured local evidence is authoritative for whether a candidate helps a specific machine/workload.
- Maintained tools such as PresentMon are valid telemetry/prior-art sources, not automatic product truth.
- Community reports are hypotheses/anecdotes until independently reproduced.

A documented Windows policy says what LatencyPilot may set. Measurement decides whether that setting should be kept.

## 3. Provenance

Authoritative evidence records where applicable:

- exact LatencyPilot product version and clean source revision;
- protocol/evidence/method versions;
- Windows build;
- CPU topology;
- target device and driver identity;
- power/environment context;
- workload/scene identity;
- requested/actual interval;
- exact requested mutation and verified actual state;
- unique capture identity.

Do not collect unrelated personal data. Dirty/unverifiable source must not claim a clean exact revision.

## 4. Capture integrity and percentiles

Decision-grade ETW is not clean when loss is unavailable/non-zero, invalid latency/image evidence exists, or the bounded event limit is reached.

The canonical percentile estimator is `linear-n-minus-one-v1`:

```text
position = (n - 1) * p
value = lower + interpolation * (upper - lower)
```

A subsystem must not introduce a different estimator under the same metric name.

Protocol v6 exposes p99.9 only at >=10,000 samples for that individual distribution. This is an adequacy floor, not a formal confidence guarantee.

## 5. DPC/ISR reference semantics

```text
DPC >100 µs   Microsoft driver-duration reference
ISR >25 µs    Microsoft driver-duration reference
>1 ms         LatencyPilot diagnostic bucket
>3 ms         LatencyPilot diagnostic bucket
```

These are context lines, not the optimizer objective or a health score. CPU0 concentration is evidence, not a universal rule to avoid CPU0.

## 6. Metrics and verdicts

Every supported optimization domain defines:

- **primary metrics** — the effect being optimized;
- **guardrails** — collateral behavior that can block an automatic win;
- **context** — explanatory state that cannot independently make a candidate win.

The authoritative verdict set is exactly:

- `Improved`
- `Regressed`
- `Tradeoff`
- `NoMeasurableDifference`
- `Inconclusive`

A primary improvement plus a material guardrail regression is `Tradeoff`, not an automatic improvement. Missing/dirty/undersampled/unverified evidence is `Inconclusive`.

Do not replace the vector of raw/derived metrics with an opaque composite score.

## 7. GPU candidate search

GPU affinity search is staged rather than blindly testing every logical CPU.

Screening candidates are generated from processor topology and repeated measured DPC+ISR pressure. One logical sibling per physical core is selected under the current v1 group-0 boundary; CPU0 is not hard-excluded.

Screening is bounded and may only nominate `ConfirmFinalist`. It may not produce a terminal Keep recommendation.

Every applied candidate must have an exact original snapshot, journal ownership, actual stored-state verification, activation/restart result and exact rollback path.

## 8. GPU synchronized evidence collection

The current internal collector executes kernel ETW and PresentMon concurrently under one linked deadline.

A run requires:

```text
requested duration: 30–60 seconds, integral milliseconds
ETW requested duration identity matches
PresentMon process/window identity matches
common ETW/PresentMon overlap >=95% of request
clean ETW capture
expected stored original/candidate state verified before and after
exact session/workload/environment/source identity
unique capture ID
```

### Primary metric

Current GPU confirmation primary evidence is the raw DPC-duration distribution from the synchronized ETW capture. It retains at least 1,000 valid samples per run.

### PresentMon guardrails

Where the installed PresentMon API exposes complete raw frames, named guardrails can include:

- CPU frame time;
- CPU busy/wait;
- GPU busy/wait;
- GPU latency;
- display latency;
- dropped-frame observations;
- other explicitly modeled frame metrics.

Optional metrics are omitted as a whole when incomplete; they are never synthesized. A dynamic aggregate is not reinterpreted as hundreds of frame samples.

Process/API/window mismatch, changed workload identity, missing required samples or insufficient overlap makes the run unusable.

## 9. GPU effective ISR-placement validity

Stored affinity policy is **not** proof that the GPU interrupt actually executed on the selected processor.

For a Candidate run, the same ETW capture is analyzed against the exact display-adapter driver/module identity:

- at least one **attributed GPU ISR** must be observed on the candidate logical processor;
- **zero attributed GPU ISR** may be observed on off-target logical processors;
- unresolved ISR attribution remains unresolved and never counts as successful placement;
- missing/ambiguous GPU driver identity cannot be promoted to placement proof.

This is a validity rule, not an arbitrary performance threshold: it answers whether the intended effective placement was actually observed in the run used for the decision.

Physical Gate A must still demonstrate this behavior on the owner’s supported GPU. Hosted CI proves only deterministic source contracts.

## 10. `gpu-affinity-confirmation-v1`

Confirmation requires exactly eight completed runs in the fixed schedule:

```text
A B B A B A A B
```

where `A` is exact captured original state and `B` is the finalist candidate.

Baseline and runs share session, workload, environment and exact source revision. Each run has a unique capture ID and explicitly verified stored/effective state evidence appropriate to its role.

Every requested interval is equal and >=30 s; actual common interval must reach >=95%. Every required primary/guardrail distribution has >=1,000 samples per run unless a higher versioned requirement applies.

Per-run statistic rules currently include:

- nonnegative latency/duration distributions: p99;
- higher-is-better throughput distributions: adverse lower tail (p01);
- dropped-frame ratio: arithmetic mean over [0,1] observations so rare drops are not hidden.

The four Original and four Candidate statistics remain separate. Each side uses median, P10–P90 relative noise, first-two/last-two median drift and extreme deviation. Noise >30%, drift >20% or >50% extreme deviation makes the metric/run set inconclusive.

The effective practical threshold is the maximum of the configured threshold and measured noise/drift from both sides. This is a conservative practical-noise rule, **not** a confidence interval or significance claim.

Only a clean confirmed `Improved` result without a material guardrail regression recommends `KeepCandidate`. Every other verdict recommends exact restoration.

Results retain raw deltas, statistic identity, sample counts, noise/drift, thresholds, run evidence and explicit reasons.

## 11. Execution-layer ownership

Interpretation is not authorization. The execution layer owns mutation/journal/final-state transitions.

Current internal GPU sequence:

```text
Screen candidate
→ Prepare journal
→ Apply + activate
→ BeginMeasurement
→ synchronized evidence
→ exact rollback

one finalist only
→ ABBA+BAAB
→ AwaitingDecision after final Candidate block
→ re-read candidate + driver identity
→ Keep OR exact RestoreOriginal
```

If `BeginMeasurement`, capture or interpretation throws after candidate ownership is acquired, rollback remains in the same owned failure scope. If rollback cannot prove exact restoration, recovery remains unresolved rather than reporting success.

Public protocol v6 still exposes no mutation command.

## 12. Input measurement contract

Raw Input may characterize **host-observable** report-arrival behavior:

- report intervals;
- interval jitter;
- long gaps;
- burst/coalescing patterns visible to the application.

It does not establish physical switch-to-photon latency. Hardware end-to-end claims require appropriate external instrumentation.

The Phase 4 source must identify the exact Raw Input/PnP route and, where Windows exposes it authoritatively, the hub/port/xHCI relationship. Names, VID/PID or registry hints alone are not enough to claim a port/controller mapping.

## 13. USB/xHCI experiment contract

A future supported xHCI experiment must define:

- exact controller identity and authoritative input route;
- exact original controller state;
- target timing/DPC/ISR metrics;
- relevant GPU/network/audio/stability guardrails;
- journaled apply/verification/revert/recovery;
- physical proof that the selected controller and route are the ones actually being measured.

Source readiness may be implemented before physical arming, but no generic device-registry writer is allowed.

## 14. Network measurement contract

Internet path latency is uncontrolled and cannot be the sole authoritative primary signal for NIC/RSS tuning.

Prefer a controlled local peer and retain raw observations for:

- RTT distribution;
- jitter;
- loss;
- throughput guardrail;
- NDIS/vendor-driver DPC/ISR distribution;
- RSS processor/queue/indirection evidence;
- CPU utilization/context.

Internet observations may be supplemental and must be labeled as such.

RSS state should come from supported Windows networking surfaces such as StandardCimv2 `MSFT_NetAdapterRssSettingData`; missing provider fields remain unavailable rather than being inferred from registry folklore.

## 15. Drift and invalid experiments

An experiment becomes inconclusive when the environment changes materially, including:

- control-side drift;
- workload/scene mismatch;
- power-state change;
- major background-load change;
- device/driver identity change;
- workload crash/termination;
- authoritative thermal/clock change where available;
- dirty/lost ETW evidence;
- unverified stored/effective applied state.

Do not manufacture a favorable verdict around invalid evidence.

## 16. Statistical comparison and practical significance

Latency distributions may be skewed, multimodal and heavy-tailed. Do not assume normality without evidence.

Future versioned methods may add bootstrap/resampling confidence intervals. A confidence interval crossing the no-effect boundary must not be called a confirmed improvement merely because a point estimate is favorable.

A statistically detectable delta may still be practically irrelevant. Metric-family practical thresholds remain explicit and auditable.

Historical evidence must not silently change meaning after method upgrades.

## 17. Workload profiles and cross-subsystem decisions

Profiles may select which already-authoritative metrics are primary/guardrails for General, Competitive/Gaming and Audio-sensitive workloads. A profile may not hide raw metrics, disable a guardrail silently or replace uncertainty with an unexplained score.

Cross-subsystem decisions are Pareto/trade-off aware. Incomparable improvements remain a `Tradeoff`; missing evidence remains `Inconclusive`.

## 18. Persistence and auditability

An authoritative experiment should retain enough evidence to audit later:

- experiment ID;
- run role;
- schema/protocol/method versions;
- workload identity/version;
- requested/actual interval;
- raw-sample reference or auditable representation;
- sample counts/statistics;
- environment context;
- validity reasons;
- requested mutation and verified actual state;
- source/product version.

Historical records are not rewritten merely to fit newer interpretation logic.

## 19. Observer effect

Avoid unnecessary per-event heap allocation, high-volume logging, synchronous file I/O, frequent UI redraw and avoidable GC pressure during authoritative measurement. Optimize observer overhead only when profiling justifies it; do not change metric semantics merely to make the observer faster.

## 20. Permanent-test policy

The default/target permanent suite is **10** tests. Every test source file must stay at or below **1200 lines**. The owner authorizes up to **20** permanent tests only when genuinely necessary to stay under that file-size boundary or to preserve a materially safer durable separation.

This is not a coverage target. Consolidate high-value scenario matrices, delete temporary/obsolete tests, and retire lower-value cases when a stronger invariant needs the budget.

Hardware validation is separate from automated-test count.

## 21. Evidence boundary and phase closure

Hosted test-only CI may prove deterministic contracts. It does **not** prove physical GPU/USB/NIC behavior, App/Service launch, installer/package correctness, signing or accessibility.

Phase 2 still requires owner-local read-only closure. Product mutation still follows Gate A → Gate B → Gate C → Gate D in `PROJECT_STATUS.md`.

The governing rule remains: **measure the machine; never assume the tweak.**
