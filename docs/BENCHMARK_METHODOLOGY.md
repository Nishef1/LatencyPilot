# Benchmark Methodology

Status: **V0.9 benchmark contract**  
Last updated: 2026-09-17

> **GPU auto-affinity authority amendment (2026-09-16, reconciled 2026-09-17).** The automatic GPU candidate-search method is now `gpu-affinity-benchmark-v1`, defined by `docs/superpowers/specs/2026-09-16-gpu-auto-affinity-benchmark-design.md` and implemented by `docs/superpowers/plans/2026-09-16-gpu-auto-affinity-benchmark.md`. The existing `baseline-quality-v2` + `workload-stability-v1` contract remains authoritative for steady `RealWorld` five-window evidence, but is **not** the readiness gate for the new deterministic synthetic candidate search. For automatic GPU affinity, system-wide CPU-busy drift is context rather than a standalone rejection; benchmark/GPU identity, frozen-workload identity, ETW integrity, control drift, repeated-side frame-p99 repeatability and device/sleep/reset events own validity. Passive processor pressure is ordering/context only, every eligible physical core is actively screened within the v1 bound, CPU0 remains eligible, the winning physical core receives SMT-sibling refinement, and original/default Windows affinity remains a real control candidate. The benchmark uses a separate `latencypilot-gpu-benchmark-v1` evidence family and does not pollute `latencypilot-evidence-v9`. `Run GPU Gate A` is development-only; `Auto-optimize GPU` remains blocked until Gate D. Where older GPU-specific text below conflicts with this amendment, this amendment and the 2026-09-16 spec/plan take precedence.

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

A built-in benchmark that intentionally moves through different scenes/phases is a different experiment shape. Its internal phases must **not** be treated as five equivalent `RealWorld` windows merely to obtain optimizer eligibility. Such a benchmark may still be useful diagnostic evidence, but decision-grade use requires repeated **whole-run** benchmark executions (or matched phase-to-phase runs) under fixed settings. The automatic GPU-affinity workflow now satisfies that requirement through the separate `gpu-affinity-benchmark-v1` whole-run method; it does not reinterpret scripted phases as `RealWorld` windows.

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

For the steady `RealWorld` evidence product, a valid `baseline-quality-v2` result is necessary but **not sufficient** for optimizer-readiness interpretation. The same five windows additionally analyze activity using DPC event rate, ISR event rate and system CPU busy when complete evidence exists. This contract remains unchanged for steady/manual evidence and historical `gpu-affinity-v1` eligibility.

The automatic synthetic GPU-affinity benchmark does **not** weaken or reuse this CPU-busy hard gate. It uses `gpu-affinity-benchmark-v1` readiness instead, because the benchmark intentionally creates deterministic multicore CPU/GPU activity and must judge comparability from its own frozen workload, target GPU/process identity, ETW integrity and repeated control behavior.

`latencypilot-evidence-v9` continues to serialize steady-workload quality/readiness. Automatic benchmark evidence uses its own schema and method identity.

### 1.4 Controlled A/B experiment

A mutation is never accepted because one post-change number looks better.

```text
Environment/provenance snapshot
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

There are now two intentionally separate GPU evidence paths:

```text
steady RealWorld five-window evidence
= comparison/readiness product using baseline-quality-v2 + workload-stability-v1

gpu-affinity-benchmark-v1
= deterministic automatic synthetic candidate-search method
```

Automatic candidate search actively screens every eligible physical core when the machine is within the v1 bounded set (maximum 16), never hard-bans CPU0, treats passive DPC+ISR pressure only as deterministic ordering/context, refines both SMT siblings of the winning physical core when present, and retains original/default Windows affinity as the control. Candidate changes must not alter calibrated worker mapping, draw count, simulation work, seed, resolution or other frozen workload parameters.

Screening may only nominate a finalist; it cannot directly Keep a candidate. The final decision remains a balanced confirmation against original state.

Internal execution owns:

```text
Prepare journal
→ Apply + activate
→ BeginMeasurement
→ synchronized benchmark + ETW + raw PresentMon evidence
→ runtime ISR-placement proof
→ exact rollback
```

If setup/capture/interpretation fails after mutation ownership is acquired, rollback remains in the same owned failure scope. If exact restoration cannot be proven, recovery remains unresolved. A confirmation failure must not discard a failed exact-original verification after rollback; the original failure and restoration-verification failure are both retained as recovery evidence.

Public protocol v6 exposes no mutation command.

## 8. GPU synchronized evidence

The automatic benchmark combines a deterministic D3D12 workload with kernel ETW and raw PresentMon evidence. D3D12 timestamp queries provide direct GPU-work timing and are the calibration source rather than relying on HWS-sensitive PresentMon GPU-active metrics alone. PresentMon raw frame intervals remain useful for frame p99/1% low and guardrails; LatencyPilot derives authoritative percentiles with its canonical estimator instead of trusting external precomputed percentile ordering.

The authoritative PresentMon compatibility boundary for this method is **PresentMon 2.5.1 or later plus API 3.3+**. The installed binary/product version and API version are evidence. PresentMon 2.5.1 is the first accepted 2.5 release because upstream withdrew the 2.5.0 binary after a shared-service compatibility conflict and 2.5.1 fixed both percentile ordering and an API backwards-compatibility regression (upstream release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.5.1). API 3.3 is the version exposed by the current 2.5.1 shared-service binary and retains the frame-query ABI used here; a missing/unparseable binary version or older API is non-authoritative rather than silently accepted.

PresentMon's own current documentation warns that `msGPUActive`/related GPU execution metrics may read late or high when Hardware-Accelerated GPU Scheduling is enabled, and that some CPU-frame-derived metrics are less accurate for OpenGL/Vulkan. Therefore PresentMon GPU-busy evidence is **context only** for `gpu-affinity-benchmark-v1`; it cannot independently validate, invalidate, or select a candidate. Direct D3D12 timestamp evidence remains the GPU-work timing source for the built-in DX12 benchmark.

A benchmark trial requires stable source/GPU/driver/topology/benchmark-process/frozen-workload identity, clean ETW capture, valid D3D12 timestamp evidence, expected stored state before/after a Candidate trial, and comparable repeated control behavior. The screening sequence first records one original-state warm-up whole-run after benchmark startup, then records two decision-grade original controls; the warm-up is retained as evidence but is not used as the control reference. Missing optional PresentMon fields stay missing. Background applications are context unless they actually break GPU/benchmark comparability.

Because applying a GPU interrupt-affinity candidate restarts the display adapter, the controlled benchmark recreates its D3D12 renderer before every trial while preserving the same process, frozen workload, worker map and seed. This prevents a stale `DXGI_ERROR_DEVICE_REMOVED` renderer from being mistaken for benchmark evidence failure.

PresentMon's numeric graphics-device identifier is query-local and may be reassigned when the display adapter is restarted. The DXGI LUID may also be recreated by a driver/device restart on the observed Windows system. Both remain useful for the per-capture PresentMon/DXGI correlation, but neither is part of the durable cross-trial key. Cross-trial identity uses the exact PnP device instance together with the adapter name; a change in either remains a hard invalidation.

`latencypilot-gpu-benchmark-v1` is repeated **whole-run** evidence. It is not `baseline-quality-v2` five-window RealWorld evidence and must never be converted into that schema merely to reuse an eligibility gate.

## 9. GPU effective ISR-placement validity

Stored affinity policy is not proof of effective placement.

Candidate evidence additionally requires the same ETW capture to show:

- exact display-driver/module attribution;
- at least one attributed GPU ISR on the candidate logical processor;
- zero attributed GPU ISR on off-target processors;
- unresolved ISR attribution remains unresolved rather than counting as success.

For a single-adapter WDDM system, the verifier first uses resolved ISR addresses in the
display KMD module (for example, `nvlddmkm.sys`). If that stream is absent, it may use
resolved `dxgkrnl.sys` ISR dispatch evidence as an explicitly labelled WDDM fallback;
the fallback is refused when more than one display adapter is present because the
shared graphics-kernel stream cannot then be attributed to one GPU without guessing.

The automatic Gate A backend uses this same resolved ISR event set for both
placement and ISR-duration comparisons, including original/control trials. It
must not verify placement with `dxgkrnl` and then measure an empty KMD ISR stream.
The ISR module/mode must remain consistent across compared trials; a source
change invalidates comparison. DPC duration remains explicitly KMD-attributed.
Each trial report retains the ISR module/mode, DPC/ISR sample counts and unresolved
ISR count. Each candidate retains the exact comparison reason. Insufficient
samples remain `Inconclusive` and restore original; they do not establish that
the original state is faster. These are additive report fields; historical
reports without them remain historical evidence and are not rewritten.

The WDDM dispatch/KMD distinction follows Microsoft's description of the
[DirectX graphics kernel subsystem](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/directx-graphics-kernel-subsystem)
and [display interrupt callback](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dispmprt/nc-dispmprt-dxgkddi_interrupt_routine).
The single-adapter ETW attribution restriction is LatencyPilot's conservative
measurement rule, not a claim that module names identify a GPU on multi-adapter systems.

This is a validity contract. Physical Gate A must still prove it on supported hardware.

## 10. `gpu-affinity-confirmation-v1`

Confirmation requires exactly:

```text
A B B A B A A B
```

`A` is exact captured original state; `B` is the finalist candidate.

Every run shares benchmark/session/environment/source/frozen-workload identity, has a unique capture identity and satisfies the method's duration/sample/integrity rules. Per-side noise/drift/state mismatch or guardrail regression prevents a confirmed automatic Keep.

Before pooled comparison, each side with two or more accepted trials must satisfy:

```text
run-level frame-p99 spread = (max p99 - min p99) / min p99 <= 20%
```

A non-finite/non-positive run-level p99 or spread above 20% makes the comparison `Inconclusive`. The original state is restored rather than averaging unstable runs into an apparent winner.

Only a clean confirmed `Improved` result without material guardrail regression recommends `KeepCandidate`; everything else recommends exact restoration.

## 11. Input/USB measurement contract

Raw Input characterizes **host-observable** report dispatch behavior and does **not** establish physical switch-to-photon latency. Exact USB route evidence uses documented USB hub interfaces/IOCTLs; names, VID/PID or registry hints alone are not port proof. A future system-changing xHCI experiment must reuse the journal/recovery discipline and remains physically gated.

## 12. Network/RSS measurement contract

Authoritative RSS state comes from supported Windows networking surfaces, currently `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData`. Missing provider fields remain unavailable; they are not reconstructed from registry folklore. Internet path latency is uncontrolled and supplemental only. A future RSS mutation must snapshot exact authoritative original state and reuse the durable journal/recovery path after the shared physical safety prerequisite is proven.

## 13. Workload profiles and Pareto policy

Profiles are versioned, transparent definitions. Cross-subsystem relations are exactly `Dominates`, `Dominated`, `Equivalent`, `Tradeoff`, or `Inconclusive`. Unlike metrics are never collapsed into a weighted score.

## 14. Global Restore Baseline

A `Kept` experiment is terminal for one experiment decision but still represents an active managed machine change. Global restore discovers retained changes newest-first, fails closed on unresolved/unknown mutation kinds, delegates to narrow supported recovery executors and verifies exact restoration.

## 15. Drift and invalid experiments

For steady `RealWorld`, workload/system activity drift remains part of the existing method. For `gpu-affinity-benchmark-v1`, broad CPU-busy movement alone is not a terminal invalidation because the benchmark itself creates controlled multicore load. Benchmark contamination follows identity/integrity/control-drift rules with one bounded retry; accepted repeated sides additionally must pass the 20% run-level frame-p99 repeatability bound before pooled comparison. A second retryable contamination failure or a repeatability failure becomes `Inconclusive`.

Do not manufacture a favorable verdict around invalid evidence.

## 16. Statistical significance and practical significance

Latency distributions can be skewed, multimodal and heavy-tailed. Do not assume normality without evidence. A statistically detectable delta may still be practically irrelevant. Metric-family thresholds stay explicit and auditable.

## 17. Persistence and auditability

An authoritative experiment retains enough evidence to audit later: experiment/run role, schema/method identities, workload identity/version, requested/actual interval, raw-sample reference or auditable representation, sample counts/statistics, environment context, validity reasons, requested mutation/verified actual state and source/product version. Historical records are not rewritten to fit newer interpretation logic.

## 18. Observer effect

Avoid unnecessary per-event heap allocation, high-volume logging, synchronous file I/O, frequent UI redraw and avoidable GC pressure during authoritative measurement. After synthetic benchmark warm-up/calibration, measured trials freeze workload parameters and keep progress emission low-frequency.

## 19. Permanent-test policy

- Default/target permanent suite: **10** tests.
- Current durable suite: **20** tests.
- Every test source file: **<=1200 lines**.
- Owner-authorized maximum: **20**, only for the file-size limit or materially safer durable subsystem separation.
- Temporary/obsolete tests are removed rather than accumulated.
