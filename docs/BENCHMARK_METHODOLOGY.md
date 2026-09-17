# Benchmark Methodology

Status: **V0.9 benchmark contract**  
Last updated: 2026-09-17

> **GPU auto-affinity authority amendment (2026-09-17).** The automatic GPU candidate-search method is `gpu-affinity-benchmark-v1`. The existing `baseline-quality-v2` + `workload-stability-v1` contract remains authoritative for steady `RealWorld` five-window evidence, but is **not** the readiness gate for deterministic synthetic GPU candidate search. For automatic GPU affinity, system-wide CPU-busy drift is context rather than a standalone rejection; benchmark/GPU identity, frozen-workload identity, ETW integrity, repeated-side frame-p99 repeatability, ISR attribution/placement and device/sleep/reset events own validity. Passive processor pressure is ordering/context only, every eligible physical core is actively screened within the v1 bound, CPU0 remains eligible, and the winning physical core receives SMT-sibling refinement. The Windows original/default affinity is a **reference and exact recovery state, not a minimum-improvement winner gate**: among decision-grade repeatable forced-CPU candidates, the product selects the best observed CPU by transparent frame-tail ranking, then confirms that finalist. If no candidate remains decision-grade/repeatable, exact original state is restored rather than guessing. PresentMon evidence is collected through LatencyPilot's pinned standalone PresentMon 2.5.1 console collector; no separately installed PresentMon Service/API is required for this method. The benchmark uses a separate `latencypilot-gpu-benchmark-v1` evidence family and does not pollute `latencypilot-evidence-v9`. `Run GPU Gate A` is development-only; `Auto-optimize GPU` remains blocked until Gate D. Where older GPU-specific text conflicts with this amendment, this amendment and the reconciled GPU auto-affinity spec take precedence.

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

A built-in benchmark that intentionally moves through different scenes/phases is a different experiment shape. Its internal phases must **not** be treated as five equivalent `RealWorld` windows merely to obtain optimizer eligibility. Such a benchmark may still be useful diagnostic evidence, but decision-grade use requires repeated **whole-run** benchmark executions (or matched phase-to-phase runs) under fixed settings. The automatic GPU-affinity workflow satisfies that requirement through the separate `gpu-affinity-benchmark-v1` whole-run method; it does not reinterpret scripted phases as `RealWorld` windows.

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
first-half/second-half relative drift <= 20%
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
→ authoritative reference evidence
→ candidate apply
→ actual-state verification
→ candidate evidence
→ fresh candidate rechecks
→ balanced reference/candidate confirmation
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

A documented Windows policy says what LatencyPilot may set. Measurement decides which candidate is best for the defined experiment and whether the evidence is safe enough to keep.

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

There are two intentionally separate GPU evidence paths:

```text
steady RealWorld five-window evidence
= comparison/readiness product using baseline-quality-v2 + workload-stability-v1

gpu-affinity-benchmark-v1
= deterministic automatic synthetic candidate-search method
```

Automatic candidate search actively screens every eligible physical core when the machine is within the v1 bounded set (maximum 16), never hard-bans CPU0, treats passive DPC+ISR pressure only as deterministic ordering/context, and retains original/default Windows affinity as reference/recovery state. Candidate changes must not alter calibrated worker mapping, draw count, simulation work, seed, resolution or other frozen workload parameters.

Each candidate transition is journal-owned and may restart the display adapter. Therefore every state transition is followed by a **5-second non-scored warm-up** before decision-grade capture. Candidate screening uses two scored whole-run captures after that warm-up. A candidate is rankable only when its evidence is decision-grade, ISR attribution/placement is valid and repeated frame-p99 spread stays within the 20% repeatability bound.

The ranking rule is deliberately transparent: lower **median run-level frame-p99** wins, with passive pressure/topology identifiers used only as deterministic tie-breaks. There is no hidden weighted score. `Inconclusive` candidates are excluded from ranking. The best up-to-three physical-core candidates are then measured again from a fresh apply/restart + warm-up and ranked again. The winner of that fresh finalist re-screen receives SMT-sibling refinement when applicable.

Screening/finalist/refinement may nominate a finalist; none can directly Keep a candidate. The final decision remains balanced confirmation against exact original state. Original comparison is retained as context and safety evidence; it is **not** a fixed minimum-improvement threshold that can prevent selection of the best valid forced-CPU candidate.

Internal execution owns:

```text
Prepare journal
→ Apply + activate/restart
→ BeginMeasurement
→ 5 s non-scored transition warm-up
→ synchronized benchmark + ETW + raw PresentMon evidence
→ runtime ISR-placement proof
→ exact rollback
```

If setup/capture/interpretation fails after mutation ownership is acquired, rollback remains in the same owned failure scope. If exact restoration cannot be proven, recovery remains unresolved. A confirmation failure must not discard a failed exact-original verification after rollback; the original failure and restoration-verification failure are both retained as recovery evidence.

Public protocol v6 exposes no mutation command.

## 8. GPU synchronized evidence

The automatic benchmark combines a deterministic D3D12 workload with kernel ETW and raw PresentMon evidence. D3D12 timestamp queries provide direct GPU-work timing and are the calibration source rather than relying on HWS-sensitive PresentMon GPU-active metrics alone. Per ADR 0005, the primary ranking signal is the benchmark's own wall-clock frame periods (AVG FPS, frame-p99, 1% low, 0.1% low) — the automated equivalent of a manual per-core decision table. PresentMon raw frame intervals remain useful as an independent cross-check and for guardrails when available; LatencyPilot derives authoritative percentiles with its canonical estimator instead of trusting an external precomputed percentile ordering.

For Gate A automatic affinity, the authoritative collector is the pinned **standalone PresentMon 2.5.1 console executable**, not a separately installed PresentMon shared Service/API. LatencyPilot accepts only its controlled packaged/cache path and verifies the official release SHA-256 before use. It invokes the upstream-supported process-targeted V2 CSV surface and records the executable version/path as provenance. A missing or hash-invalid collector is non-authoritative and fails closed. The older service/API readers may continue to exist for other observation paths, but Gate A must not depend on `Program Files\Intel\PresentMon*` or `PresentMonAPI2.dll` being installed.

PresentMon's own current documentation warns that `msGPUActive`/related GPU execution metrics may read late or high when Hardware-Accelerated GPU Scheduling is enabled, and that some CPU-frame-derived metrics are less accurate for OpenGL/Vulkan. Therefore PresentMon GPU-busy evidence is **context only** for `gpu-affinity-benchmark-v1`; it cannot independently validate, invalidate, or select a candidate. Direct D3D12 timestamp evidence remains the GPU-work timing source for the built-in DX12 benchmark.

A benchmark trial requires stable source/GPU/driver/topology/benchmark-process/frozen-workload identity, valid benchmark frame-period evidence with D3D12 timestamp evidence, expected stored state before/after a Candidate trial, and continuity. Standalone-PresentMon raw frames and clean ETW capture are best-effort guardrails per ADR 0005: their absence is recorded as explicit context and never as a silent pass, and never blocks video-primary ranking. The session records one original-state warm-up, then two decision-grade original reference controls. Every candidate apply/restart, finalist re-screen, SMT refinement and confirmation state transition also receives its own 5-second non-scored warm-up before scored evidence. Missing optional PresentMon fields stay missing. Background applications are context unless they actually break GPU/benchmark comparability.

Because applying a GPU interrupt-affinity candidate restarts the display adapter, the controlled benchmark recreates its D3D12 renderer before every trial while preserving the same process, frozen workload, worker map and seed. This prevents a stale `DXGI_ERROR_DEVICE_REMOVED` renderer from being mistaken for benchmark evidence failure.

Durable cross-trial GPU identity uses the exact PnP display-device identity plus the matching single DXGI hardware adapter/name. Gate A does not require a PresentMon graphics-device ID for identity continuity. Multi/hybrid-GPU routing remains fail-closed until the workload-to-adapter route can be proven directly without guessing.

`latencypilot-gpu-benchmark-v1` is repeated **whole-run** evidence. It is not `baseline-quality-v2` five-window RealWorld evidence and must never be converted into that schema merely to reuse an eligibility gate.

## 9. GPU effective ISR-placement validity

Stored affinity policy is not proof of effective placement.

When kernel ETW is healthy, candidate evidence additionally requires the same ETW capture to show:

- exact display-driver/module attribution;
- at least one attributed GPU ISR on the candidate logical processor;
- zero attributed GPU ISR on off-target processors;
- unresolved ISR attribution remains unresolved rather than counting as success.

Per ADR 0005, when kernel ETW is unavailable there is no placement proof
either way: the trial stays rankable on benchmark frame periods under
verified stored state but is explicitly flagged placement-unverified.

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

`A` is exact captured original state; `B` is the ranked finalist candidate. Each scheduled role is preceded by a fresh 5-second non-scored warm-up after the required state transition; only the eight role captures are scored.

Every scored run shares benchmark/session/environment/source/frozen-workload identity, has a unique capture identity and satisfies the method's duration/sample/integrity rules. Before pooled interpretation, each side with two or more accepted trials must satisfy:

```text
run-level frame-p99 spread = (max p99 - min p99) / min p99 <= 20%
```

A non-finite/non-positive run-level p99 or spread above 20% makes the confirmation `Inconclusive`. Missing/inconsistent ISR attribution, invalid placement/state, dirty ETW, identity drift or other decision-grade failure also makes it `Inconclusive`; exact original state is restored rather than guessing.

A finalist that remains decision-grade and repeatable through balanced confirmation may be kept even when its delta versus the Windows default is inside the generic ±3% comparison threshold or the default reference happens to measure faster. That comparison remains visible context. This is intentional: the product question for Auto GPU Affinity is **which tested CPU is the best valid forced interrupt target**, not whether forced affinity beats Windows' default scheduling policy by a fixed margin. If no valid/repeatable finalist exists, restore exact original state.

## 11. Input/USB measurement contract

Raw Input characterizes **host-observable** report dispatch behavior and does **not** establish physical switch-to-photon latency. Exact USB route evidence uses documented USB hub interfaces/IOCTLs; names, VID/PID or registry hints alone are not port proof. A future system-changing xHCI experiment must reuse the journal/recovery discipline and remains physically gated.

## 12. Network/RSS measurement contract

Authoritative RSS state comes from supported Windows networking surfaces, currently `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData`. Missing provider fields remain unavailable; they are not reconstructed from registry folklore. Internet path latency is uncontrolled and supplemental only. A future RSS mutation must snapshot exact authoritative original state and reuse the durable journal/recovery path after the shared physical safety prerequisite is proven.

## 13. Workload profiles and Pareto policy

Profiles are versioned, transparent definitions. Cross-subsystem relations are exactly `Dominates`, `Dominated`, `Equivalent`, `Tradeoff`, or `Inconclusive`. Unlike metrics are never collapsed into a weighted score.

## 14. Global Restore Baseline

A `Kept` experiment is terminal for one experiment decision but still represents an active managed machine change. Global restore discovers retained changes newest-first, fails closed on unresolved/unknown mutation kinds, delegates to narrow supported recovery executors and verifies exact restoration.

## 15. Drift and invalid experiments

For steady `RealWorld`, workload/system activity drift remains part of the existing method. For `gpu-affinity-benchmark-v1`, broad CPU-busy movement alone is not a terminal invalidation because the benchmark itself creates controlled multicore load. Benchmark contamination follows identity/integrity/control-drift rules with one bounded retry; accepted repeated candidate blocks additionally must pass the 20% run-level frame-p99 repeatability bound before ranking. A second retryable contamination failure, invalid attribution/placement or a repeatability failure becomes `Inconclusive` and is not rankable.

Do not manufacture a favorable verdict around invalid evidence.

## 16. Statistical significance and practical significance

Latency distributions can be skewed, multimodal and heavy-tailed. Do not assume normality without evidence. A statistically detectable delta may still be practically irrelevant. Metric-family thresholds stay explicit and auditable. For GPU auto-affinity candidate ranking, practical transparency comes from raw run-level p99 values, median ranking, bounded fresh finalist re-screen and balanced confirmation rather than a hidden composite score.

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
