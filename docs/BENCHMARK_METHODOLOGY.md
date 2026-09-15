# Benchmark Methodology

Status: **V0.7 benchmark contract**  
Last updated: 2026-09-15

LatencyPilot exists to distinguish measurable improvement from placebo, ordinary run-to-run variation, workload drift, isolated workload spikes, or a trade-off hidden by one headline number. It is an experimental optimization platform, not a collection of assumed Windows tweaks.

Where implementation and this contract disagree, reconcile the discrepancy explicitly. Never lower the evidence bar merely to make an experiment pass.

## 1. Evidence hierarchy

### 1.1 Quick diagnostic snapshot

```text
1 × 5-second DPC/ISR capture
```

Purpose: ETW integrity, module/routine attribution, per-CPU concentration, obvious tail events and hypothesis generation. It is **not** a benchmark verdict, stability proof, health score or optimization recommendation.

### 1.2 Repeated decision baseline — `baseline-quality-v2`

```text
LatencyPilot/service settle: 5 seconds
5 authoritative windows × 20 seconds
750 ms inter-window settle
```

The workload must already be warmed/repeatable unless startup/loading behavior is intentionally under test. For `RealWorld`, this means one **steady** scene, action loop or workload pattern that is intended to remain comparable across all five windows.

A built-in benchmark that intentionally moves through different scenes/phases is a different experiment shape. Its internal phases must **not** be treated as five equivalent `RealWorld` windows merely to obtain optimizer eligibility. Such a benchmark may still be useful diagnostic evidence, but decision-grade use requires repeated **whole-run** benchmark executions (or matched phase-to-phase runs) under fixed settings. That repeated-run workflow is not currently an optimizer candidate source, so Gate A continues to require the steady-state `RealWorld` baseline below. This follows the broader benchmark discipline of fixed settings plus comparison across repeated result sets rather than assuming every interval inside one scripted workload has identical activity.

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

Across window-level DPC/ISR p99 values:

```text
relative noise = (P90 - P10) / |median| <= 30%
early/late relative drift <= 20%
extreme-window deviation <= 50%
```

No inconvenient window is deleted. `Valid` means repeatable enough for this latency-quality method, not that the machine is globally healthy or optimizer-ready.

### 1.3 Optimizer workload readiness — `workload-stability-v1`

A valid `baseline-quality-v2` result is necessary but **not sufficient** for optimizer candidate planning. Candidate planning additionally analyzes activity across the **same five windows**:

- DPC event rate;
- ISR event rate;
- system CPU busy percentage when that context is available for all windows.

For each available activity signal:

```text
early/late relative activity drift <= 25%
maximum single-window relative deviation from median <= 50%
```

A changing early/late activity level or one isolated extreme workload window makes the repeated workload `Changing` and therefore ineligible for optimization. Missing/invalid/incomplete five-window evidence is `Insufficient` and also ineligible.

The shared GPU readiness contract therefore requires both:

```text
baseline-quality-v2 = valid
AND
workload-stability-v1 = stable
```

The App uses this same readiness boundary before preparing GPU candidates. A clean latency distribution from a materially changing workload is not candidate evidence.

`workload-stability-v1` is an optimizer-readiness method, not a new definition silently embedded in evidence-v8. A future serialized workload-stability field requires a new evidence schema version.

### 1.4 Controlled A/B experiment

A mutation is never accepted because one post-change number looks better.

```text
Environment/provenance snapshot
→ workload already warmed and stable
→ authoritative control evidence
→ candidate apply
→ actual-state verification
→ candidate evidence
→ balanced control/candidate rechecks
→ target + guardrail comparison
→ Keep or exact Revert
→ final-state verification
```

Use balanced/interleaved ordering where practical. GPU confirmation v1 uses fixed eight-run `ABBA + BAAB` ordering.

Reboot-requiring experiments must persist exact experiment/rollback state across boots instead of pretending both sides were contiguous.

## 2. Source hierarchy

- Microsoft documentation is authoritative for Windows API/resource/ETW semantics and supported behavior.
- Measured local evidence is authoritative for whether a candidate helps a particular machine/workload.
- Maintained tools such as PresentMon are telemetry/prior-art sources, not automatic product truth.
- Community reports are hypotheses/anecdotes until reproduced.

A documented Windows policy says what LatencyPilot may set. Measurement decides whether the candidate should be kept.

## 3. Provenance

Authoritative evidence records where applicable:

- exact LatencyPilot version and clean source revision;
- protocol/evidence/method versions;
- Windows build and CPU topology;
- target device/driver identity;
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

A subsystem must not introduce another estimator under the same metric identity.

Protocol v6 exposes p99.9 only with >=10,000 samples for that individual distribution. This is an adequacy floor, not a confidence guarantee.

## 5. DPC/ISR reference semantics

```text
DPC >100 µs   Microsoft driver-duration reference
ISR >25 µs    Microsoft driver-duration reference
>1 ms         LatencyPilot diagnostic bucket
>3 ms         LatencyPilot diagnostic bucket
```

These are context lines, not an optimizer objective or health score. CPU0 concentration is evidence, not a universal CPU0-avoidance rule.

## 6. Metrics and verdicts

Every supported optimization domain defines:

- **primary metrics** — target effect;
- **guardrails** — collateral behavior that can block an automatic win;
- **context** — explanatory state that cannot independently make a candidate win.

The authoritative verdict set is exactly:

- `Improved`
- `Regressed`
- `Tradeoff`
- `NoMeasurableDifference`
- `Inconclusive`

Primary improvement plus material guardrail regression is `Tradeoff`. Missing, dirty, undersampled or unverified evidence is `Inconclusive`.

Do not replace the metric vector with an opaque composite score.

## 7. GPU candidate and execution contract

Candidate search is bounded and generated from measured repeated DPC+ISR processor pressure. One logical sibling per physical core is selected under the current single-group v1 boundary; CPU0 is not hard-excluded.

Screening may only nominate `ConfirmFinalist`; it cannot directly Keep a candidate.

Internal execution owns:

```text
Prepare journal
→ Apply + activate
→ BeginMeasurement
→ synchronized evidence
→ exact rollback
```

After one finalist is nominated, confirmation owns a fixed eight-run balanced sequence. If setup/capture/interpretation fails after mutation ownership is acquired, rollback remains in the same owned failure scope. If exact restoration cannot be proven, recovery remains unresolved.

Public protocol v6 exposes no mutation command.

## 8. GPU synchronized evidence

The internal collector executes kernel ETW and PresentMon concurrently under one deadline.

A run requires:

```text
requested duration: 30–60 seconds, integral milliseconds
ETW requested-duration identity matches
PresentMon process/window identity matches
common ETW/PresentMon overlap >=95%
clean ETW capture
expected stored state verified before and after
same session/workload/environment/source identity
unique capture ID
```

Primary evidence is raw DPC-duration samples with >=1,000 valid samples per required run.

Where PresentMon exposes complete raw frame evidence, named guardrails may include CPU frame time, CPU/GPU busy/wait, GPU/display latency and dropped-frame observations. Missing optional metrics are omitted, never synthesized; an aggregate is never inflated into fake raw samples.

## 9. GPU effective ISR-placement validity

Stored affinity policy is not proof of effective placement.

Candidate evidence additionally requires the same ETW capture to show:

- exact display-driver/module attribution;
- at least one attributed GPU ISR on the candidate logical processor;
- zero attributed GPU ISR on off-target processors;
- unresolved ISR attribution remains unresolved rather than counting as success.

This is a validity contract. Physical Gate A must still prove it on supported hardware.

## 10. `gpu-affinity-confirmation-v1`

Confirmation requires exactly:

```text
A B B A B A A B
```

`A` is exact captured original state; `B` is the finalist candidate.

Every run shares session/workload/environment/source identity, has a unique capture identity, equal >=30 s requested duration, >=95% actual common interval and >=1,000 samples for every required metric.

Per-side statistics retain four Original and four Candidate observations. Noise >30%, drift >20%, >50% extreme deviation, state mismatch or guardrail regression prevents a confirmed automatic Keep.

Only a clean confirmed `Improved` result without material guardrail regression recommends `KeepCandidate`; everything else recommends exact restoration.

## 11. Input/USB measurement contract

Raw Input characterizes **host-observable** report dispatch behavior:

- raw report intervals;
- median/p95/p99 interval;
- observed report rate;
- tail jitter;
- long gaps;
- burst/coalescing patterns visible to the application.

It does **not** establish physical switch-to-photon latency. End-to-end hardware claims require appropriate external instrumentation.

Exact USB route evidence uses documented USB hub interfaces/IOCTLs. A Raw Input/PnP route is promoted to an exact hub/port only when the driver-key match is unique and controller ancestry is compatible. Names, VID/PID or registry hints alone are not port proof. Microsoft documents that `IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME` returns the driver registry key for the device connected to a selected hub port.

The xHCI readiness layer requires exact controller identity, host timing evidence and controller/module-attributed DPC/ISR evidence. A future system-changing xHCI experiment must reuse the journal/recovery discipline and remains physically gated.

## 12. Network/RSS measurement contract

Authoritative RSS state comes from supported Windows networking surfaces, currently `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData` through the Windows provider. Missing provider fields remain unavailable; they are not reconstructed from registry folklore.

Read-only evidence may include:

- RSS enabled/support state;
- MSI/MSI-X provider fields;
- queue/interrupt counts;
- profile and processor range;
- indirection/RSS processor arrays;
- exact/unique provider→PnP correlation when available;
- vendor-driver DPC/ISR attribution, kept distinct from generic NDIS evidence.

Internet path latency is uncontrolled and cannot be the authoritative primary signal for NIC tuning. Controlled local observations retain RTT, jitter, loss, throughput and CPU guardrails. Internet observations are supplemental only.

A future RSS mutation must snapshot exact authoritative original state, use a supported narrow candidate and reuse the durable journal/recovery path after the shared physical safety prerequisite is proven.

## 13. Workload profiles and Pareto policy

Profiles are versioned, transparent definitions for Competitive/Gaming, General and Audio-sensitive workloads. They may select already-authoritative primary metrics/guardrails and disable a subsystem explicitly, but may not hide raw evidence or silently remove guardrails.

Cross-subsystem relations are exactly:

```text
Dominates
Dominated
Equivalent
Tradeoff
Inconclusive
```

Unlike metrics are never collapsed into a weighted score. Incomparable wins remain `Tradeoff`; missing evidence remains `Inconclusive`.

## 14. Global Restore Baseline

A `Kept` experiment is terminal for one experiment decision but still represents an active managed machine change.

Global restore:

- discovers retained changes newest-first;
- fails closed if unresolved state exists;
- fails closed on unknown mutation kinds;
- delegates to supported subsystem-specific recovery executors;
- verifies exact restoration before terminalizing the restored state;
- never becomes a generic privileged writer.

Upgrade/uninstall must keep recovery tooling installed while retained or unresolved managed state exists.

## 15. Drift and invalid experiments

An experiment becomes inconclusive when the environment changes materially, including:

- control-side latency drift;
- workload activity drift or isolated workload spike;
- scene/workload identity mismatch;
- power-state change;
- major background-load change;
- device/driver identity change;
- workload crash/termination;
- authoritative thermal/clock change where available;
- dirty/lost ETW evidence;
- unverified stored/effective state.

Do not manufacture a favorable verdict around invalid evidence.

## 16. Statistical significance and practical significance

Latency distributions can be skewed, multimodal and heavy-tailed. Do not assume normality without evidence.

Future versioned methods may add bootstrap/resampling intervals. A confidence interval crossing the no-effect boundary must not be called confirmed improvement merely because the point estimate is favorable.

A statistically detectable delta may still be practically irrelevant. Metric-family thresholds stay explicit/auditable. Historical evidence never silently changes meaning after a method upgrade.

## 17. Persistence and auditability

An authoritative experiment should retain enough evidence to audit later:

- experiment ID and run role;
- schema/protocol/method identities;
- workload identity/version;
- requested/actual interval;
- raw-sample reference or auditable representation;
- sample counts/statistics;
- environment context;
- validity reasons;
- requested mutation and verified actual state;
- source/product version.

Historical records are not rewritten to fit newer interpretation logic.

## 18. Observer effect

Avoid unnecessary per-event heap allocation, high-volume logging, synchronous file I/O, frequent UI redraw and avoidable GC pressure during authoritative measurement. Optimize observer overhead only when profiling justifies it; do not change metric semantics merely to make the observer faster.

## 19. Permanent-test policy

- Default/target permanent suite: **10** tests.
- Current durable suite: **17** tests.
- Every test source file: **<=1200 lines**.
- Owner-authorized maximum: **20**, only for the file-size limit or materially safer durable subsystem separation.
- Current separate USB, input, NIC/RSS, profile/Pareto, restore, workload-readiness and GPU runtime-placement contracts intentionally use that authorization.
- Temporary/obsolete tests are removed rather than accumulated.

Hardware validation is separate from automated-test count.

## 20. Evidence and phase boundary

Hosted test-only CI can prove deterministic/source contracts. It does **not** prove physical GPU/USB/NIC behavior, WinUI/Service runtime, installer/package correctness, signing or accessibility.

Phase 2 still requires owner-local read-only closure. Product mutation still follows Gate A → Gate B → Gate C → Gate D in `PROJECT_STATUS.md`. USB/NIC mutation source remains deferred until the shared Gate A substrate is physically credible.

The governing rule remains: **measure the machine; never assume the tweak.**
