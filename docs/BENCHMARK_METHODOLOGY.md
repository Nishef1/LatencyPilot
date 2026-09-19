# Benchmark Methodology

Status: **V0.10 benchmark contract**  
Last updated: 2026-09-19

LatencyPilot exists to distinguish measurable effects from placebo, ordinary run-to-run variation, workload drift and unsafe/unverified state. It is not a generic Windows tweak collection.

Current GPU auto-affinity authority: **ADR 0006**. The steady RealWorld evidence product and the deterministic GPU candidate-search method are intentionally different measurement shapes.

## 1. Evidence hierarchy

### 1.1 Quick diagnostic snapshot

```text
1 × 5-second DPC/ISR capture
```

Purpose: ETW integrity, attribution, per-CPU concentration, obvious tail events and hypothesis generation. It is not a benchmark verdict or health score.

### 1.2 Repeated steady baseline — `baseline-quality-v2`

```text
5 s settle
5 authoritative windows × 20 s
750 ms inter-window settle
```

This remains the authoritative steady/manual RealWorld contract. Each window requires clean capture integrity, >=95% requested duration, >=1,000 DPC, >=1,000 ISR and finite positive distribution statistics. Existing noise/drift/workload-stability rules remain valid for this product.

The automatic GPU-affinity workflow does **not** reinterpret its synthetic benchmark trials as these five equivalent RealWorld windows.

### 1.3 Automatic GPU affinity — `gpu-affinity-benchmark-v1`

The product question is:

> Among the tested valid logical-processor interrupt targets, which CPU gives the strongest repeatable controlled benchmark result?

Windows default is exact reference/recovery state. It is not an opponent that every forced CPU must beat by a fixed percentage.

## 2. Source hierarchy

- Microsoft documentation owns Windows API/resource/ETW semantics and supported behavior.
- Measured local evidence owns what happens on the tested machine/workload.
- Maintained tools such as PresentMon are telemetry/cross-check sources, not automatic truth.
- Community reports and tuning videos are hypotheses/workflow references until reproduced.

## 3. Provenance and integrity

Authoritative evidence records where applicable:

- exact clean source revision;
- schema/method version;
- Windows build and processor topology;
- target device and driver identity;
- benchmark process/frozen workload/seed/worker map;
- requested and actual interval;
- requested mutation and verified stored state;
- capture identity and integrity state.

Dirty or unverifiable source must not claim clean provenance. Missing evidence remains missing rather than being reconstructed from registry folklore.

## 4. DPC/ISR semantics

DPC/ISR counts are useful context but do not represent CPU cost by themselves. LatencyPilot also tracks execution duration and tail behavior. CPU0 concentration is evidence, not a universal CPU0-avoidance rule.

Stored interrupt configuration, allocated resources and runtime DPC/ISR behavior are separate evidence layers:

```text
stored policy ≠ allocated assignment ≠ runtime placement
```

## 5. GPU controlled benchmark

The built-in D3D12 benchmark performs one adaptive calibration and then freezes:

- worker mapping;
- command workload;
- simulation workload;
- seed;
- resolution;
- benchmark process identity.

Applying or rolling back GPU interrupt affinity restarts the display adapter. The benchmark intentionally keeps one authenticated benchmark process alive for the whole search and recreates its D3D12 renderer/device after **every** apply/rollback activation before the next warm-up or scored block. If a surviving control process still receives `DXGI_ERROR_DEVICE_REMOVED` (`0x887A0005`) or `DXGI_ERROR_DEVICE_RESET` (`0x887A0007`) from a trial, that evidence slot gets one bounded renderer recreation + retry; a second failure remains terminal. The non-scored warm-up and its scored run otherwise reuse the same recreated renderer/device. The frozen workload, seed and worker map therefore stay process-stable without reusing D3D12 resources across an adapter restart.

### 5.1 Controlled frame period

Each benchmark loop records its own wall-clock period. Because the loop includes the benchmark's synchronization policy, this is a **controlled comparison signal**. It is not claimed to be identical to an arbitrary game's end-to-end frame time.

For every scored run LatencyPilot derives:

- AVG FPS = inverse of mean valid frame period;
- 1% low FPS from the worst 1% of controlled frame periods;
- 0.1% low FPS from the worst 0.1%;
- frame-p99 as diagnostic/tie context.

This is LatencyPilot's documented methodology. It is intentionally understandable and is not described as bit-for-bit identical to AutoGpuAffinity's historical low calculation.

## 6. GPU candidate search

Candidate generation uses actual Windows processor topology and includes every eligible logical processor in the supported group. SMT siblings are distinct candidates because Windows interrupt affinity targets logical processors. No even/odd numbering assumption is made and CPU0 is not banned.

Current v1 sequence:

```text
exact original/default state
→ 5 s non-scored original warm-up/reference (benchmark only; no PresentMon/ETW)
→ collect 3 scored 30 s Original runs
   if no stable 3-run 1%-low cluster exists within ±3% of its median:
     collect one replacement run 4
   if four valid runs still do not form that preferred cluster:
     retain all four runs and use their observed per-metric variance as the noise floor; do not abort before CPU screening
→ each eligible logical CPU:
     journaled apply/restart + stored-state verify
     5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
     1 scored 30 s screening run
     exact rollback
→ 5 s non-scored Original warm-up
→ collect one fresh scored Original control after the full sweep
   if its 1% low leaves the Original repeatability band:
     discard the sweep and RestoreOriginal
→ rank valid screening candidates
→ shortlist the best three plus every additional candidate whose screening 1% low is within max(1%, observed Original cluster noise) of the third-place cutoff
→ shortlisted candidates:
     two independent re-test rounds are mandatory
     each finalist gets fresh apply/restart + stored-state verify, warm-up, scored run, exact rollback
     only finalists still lacking a stable 3-run cluster receive one replacement round
→ prefer the tightest stable 3-run cluster selected from at most 4 scored observations
   at most one scored observation is excluded when such a cluster exists
   if four valid runs still do not cluster, retain all four and use their observed variance in ranking/guardrail thresholds
→ 5 s non-scored Original warm-up
→ collect one fresh scored Original control after finalist re-tests
   if 1% low, AVG or frame-p99 leaves the Original repeatability band:
     discard finalist evidence and RestoreOriginal
→ test finalists in ranking order against Original/noise and guardrails; if the first fails, try the next clean finalist
→ apply the highest-ranked clean winner once
→ final benchmark-only warm-up → ETW placement-verification capture
→ Keep only when measured improvement clears the observed cluster-noise floor, comparable GPU-driver DPC/ISR p99 does not materially regress, and final runtime ISR placement is proved
   otherwise exact RestoreOriginal
```

There is no separate SMT/hyperthread refinement phase in v1 because eligible siblings are screened directly, and there is no ABBA/BAAB finalist loop.

### 6.1 Ranking order

Ranking is transparent and lexicographic; there is no hidden weighted score:

Ranking is noise-aware rather than raw-number lexicographic:

1. compare median **1% low**; differences <=1% are treated as practical ties;
2. if tied, compare median **AVG FPS** with the same 1% equivalence margin;
3. if tied, compare lower median **frame-p99** with a 1% equivalence margin;
4. if still tied, compare median **0.1% low** only when the relative difference exceeds 5%;
5. if still tied, use passive core-pressure/core/processor identity only as deterministic fallback.

The 1% comparison margin follows the practical repeatability scale expected from a well-behaved benchmark rather than pretending that tiny decimal differences identify a real winner.

`0.1% low` remains visible because it is useful for spotting severe tail spikes, but a 30 s run can contain only a small number of frames in the worst 0.1%; it therefore does not outrank the more stable 1% low/AVG/p99 signals.

An initial screening candidate has one 30 s scored observation. A finalist has three scored observations: its initial screen plus one fresh score in each of two separate re-test rounds. No finalist receives two scored re-tests back-to-back under one affinity activation.

### 6.2 Repeatability

Repeatability no longer uses raw `(max - min) / min` spread. A single multitasking spike can invalidate that statistic and its `min` denominator biases the reported variation toward the worst run.

LatencyPilot uses bounded robust sampling instead:

- 1% low is the primary repeatability signal.
- Start with three scored observations.
- Evaluate every 3-observation combination and select the **tightest** cluster whose members are each within **±3% of that cluster's median 1% low**.
- If no preferred cluster exists, collect one replacement observation and re-evaluate. No fifth score is collected.
- Three valid runs are sufficient for a preferred cluster; four scored attempts are the hard maximum.
- If a preferred cluster exists, at most one scored observation may be excluded and the excluded sample remains in the audit trail.
- If four valid runs still do not form a ±3% 1%-low cluster, **do not discard the benchmark evidence**. Median 1% low / 0.1% low / AVG / frame-p99 are computed from all four runs, and each metric's maximum median-relative deviation becomes observed noise for later comparisons.
- AVG FPS and frame-p99 remain decision guardrails; 0.1% low remains rare-tail diagnostic/regression context rather than the outlier detector.
- Original-control drift bands are noise-aware per metric: each allowed band is `max(3%, observed Original noise for that metric)`.
- Final AVG / frame-p99 / 0.1%-low regression guardrails are also noise-aware, using the larger of the documented minimum margin and observed Original/finalist run-to-run noise.
- When both Original and finalist provide at least three scored runs with enough attributable samples, GPU-driver DPC/ISR p99 is computed per run. Median tail regression is compared against a noise-aware limit of `max(10%, Original run-to-run tail noise, finalist run-to-run tail noise)`; a regression beyond that limit rejects that finalist without preventing the next ranked finalist from being considered.
- The final winner must beat `max(1%, observed Original 1%-low noise, observed finalist 1%-low noise)` before guardrails and final ETW placement verification are considered.

The ±3% band is a **preferred-cluster rule**, not a universal claim about Windows variance and no longer a pre-screen abort gate. A noisy but otherwise valid Original baseline can therefore continue into CPU screening; its instability makes the Keep threshold harder to clear rather than preventing the optimizer from testing candidates at all. Screening remains one scored run per eligible logical CPU; robust replacement sampling is applied only to Original and finalists where evidence drives Keep/Restore.

### 6.3 Adaptive finalist cutoff

The initial screen always advances at least the best three rankable logical CPUs. It also advances every additional core whose **screening 1% low is within max(1%, observed Original cluster noise) of the third-place screening value**. A fresh Original warm-up precedes the scored control after the sweep; if the scored control leaves the Original repeatability band, the sweep is discarded rather than ranking measurements taken across a moving environment. A second fresh Original warm-up precedes the scored control after finalist re-tests; that scored control must remain comparable in 1% low, AVG and frame-p99 before any finalist is eligible for Keep. The shortlist is intentionally uncapped; if many logical CPUs are effectively tied, extra re-tests are preferable to manufacturing a winner from noise.

## 7. Screening evidence and external collectors

Screening must remain robust enough to complete the bounded CPU search.

### Controlled benchmark evidence — required

- valid artifact/session identity;
- frozen-workload continuity;
- valid D3D12 timestamp evidence;
- valid controlled frame periods;
- exact stored affinity verified around candidate capture.

### PresentMon — independent best-effort cross-check

LatencyPilot uses the pinned standalone PresentMon 2.5.1 console collector; a separately installed PresentMon Service/API is not a Gate A prerequisite.

PresentMon's documented timing fields are not interchangeable:

- `MsBetweenPresents` = time between Present() calls;
- `MsBetweenAppStart` = start of the current frame until CPU work begins for the next frame.

Therefore the console CSV parser accepts the current `FrameTime` field or legacy `MsBetweenPresents` for present cadence; it does **not** substitute `MsBetweenAppStart` as the same metric. Failed raw CSV may be retained for owner diagnosis, but retention is bounded.

PresentMon GPU-active/busy metrics remain context; D3D12 timestamps are the direct GPU-work timing source for the built-in DX12 workload.

### Kernel ETW during screening — best-effort unless it proves failure

If kernel ETW is unavailable/dirty during a screening block, the absence is explicit context and benchmark-period ranking may continue under verified stored state.

If ETW is healthy and attribution proves the requested candidate is not the effective target, that candidate is invalid and cannot be ranked.

## 8. Final GPU Keep validity

Final Keep is intentionally stricter than screening.

After selecting the ranked winner, LatencyPilot applies it one final time and runs a fresh verification capture. Keep requires all of:

- exact stored candidate state verified before/after the capture;
- kernel ETW integrity complete;
- zero reported ETW event loss;
- attributable GPU ISR samples exist;
- target processor matches the selected finalist;
- target ISR count > 0;
- resolved off-target ISR count = 0.

Missing ETW is **not** sufficient for Keep. If final placement cannot be proved, LatencyPilot restores and verifies exact Original.

For a single-adapter WDDM system, display-KMD attribution is preferred; a labelled `dxgkrnl` dispatch fallback is accepted only where the existing conservative attribution method can identify the single display target without guessing.

## 9. Failure, cancellation and rollback

Mutation ownership starts before state is changed and remains owned until exact Keep or Revert terminalization.

- Candidate failure after apply → exact rollback + original verification.
- Cancellation before Keep → exact rollback + original verification.
- Final placement failure → exact rollback + original verification.
- Keep failure → rollback is attempted and both failures are preserved if rollback also fails.
- Unknown/diverged state stays recovery-owned; the journal is never edited away to make validation pass.

## 10. Input/USB measurement contract

Raw Input characterizes **host-observable** report timing and does not establish physical click-to-photon latency.

Device routing must use authoritative topology:

```text
Raw Input device → PnP ancestry → USB hub/port → exact xHCI controller
```

Names, VID/PID or loose registry hints are not sufficient route proof.

After the GPU winner is fixed, v1 CPU selection for input/xHCI uses available interrupt headroom. The decision should prioritize DPC duration, ISR duration and tail spikes; DPC/ISR count is visible context/tie information rather than the sole optimizer truth. The GPU winner CPU is excluded by default.

System-changing xHCI affinity is part of the v1 workflow but remains unarmed until GPU Gate A physically proves the shared mutation/recovery substrate.

## 11. Before/after evidence

The automatic v1 workflow should compare like-for-like baseline/final conditions where feasible and show raw named metrics rather than a synthetic overall score.

Expected final user-facing evidence includes:

- GPU selected CPU;
- AVG / 1% low / 0.1% low and p99 context;
- input/xHCI selected CPU;
- per-CPU/controller DPC/ISR count, total duration and tail behavior;
- host Raw Input timing where captured;
- verification/recovery state.

Do not label a proxy as network latency, click-to-photon latency or another quantity that was not measured.

## 12. Future/non-v1 methods

NIC/RSS automatic mutation, audio affinity and cross-subsystem/Pareto automatic optimization are future work and do not gate v1. Existing read-only or policy source may remain but cannot silently enter the v1 decision path.

## 13. Auditability and observer effect

Authoritative experiments retain enough data to audit the decision later. Historical evidence is not rewritten to fit new interpretation logic.

During authoritative capture avoid unnecessary per-event allocation, synchronous high-volume file I/O, frequent UI redraw and avoidable GC pressure. Benchmark progress reporting remains low frequency after calibration.

## 14. Permanent-test policy

The permanent suite is behavior-focused and must not grow one test per implementation detail. Temporary TDD characterization tests are removed after the behavior is represented in canonical tests. Physical benchmark repetitions and Gate A runs are evidence, not unit tests.

## Audit-closure one-at-a-time workflow (2026-09-19)

LatencyPilot now treats automatic optimization as a one-at-a-time experiment pipeline: **Original measurement → GPU affinity → conservative MSI → primary-input/xHCI → final verification → report**. A stage that is NotReady, inconclusive, or requires reboot stops the pipeline; intent is never treated as activation proof.

MSI mutation is deliberately narrow: `MSISupported` may be enabled only when authoritative stored state makes the target applicable. `MessageNumberLimit` and interrupt priority are preserved/observed, not tuned automatically. xHCI affinity requires one explicit primary Raw Input identity, one exact USB route, clean capture evidence, and reversible journal ownership.

`Restore original settings` replays retained LatencyPilot changes newest-first from exact snapshots. It does **not** claim to restore Windows defaults. Public mutation remains fail-closed (`MutationAvailable = false`) until exact-revision physical GPU/MSI/xHCI validation is recorded; hosted CI proves source contracts only.
