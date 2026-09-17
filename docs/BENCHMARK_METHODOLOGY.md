# Benchmark Methodology

Status: **V0.9 benchmark contract**  
Last updated: 2026-09-18

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

> Among the tested valid physical-core interrupt targets, which CPU gives the strongest repeatable controlled benchmark result?

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

Applying GPU interrupt affinity may restart the display adapter. The benchmark recreates its D3D12 renderer after the restart but preserves the frozen workload/process identity.

### 5.1 Controlled frame period

Each benchmark loop records its own wall-clock period. Because the loop includes the benchmark's synchronization policy, this is a **controlled comparison signal**. It is not claimed to be identical to an arbitrary game's end-to-end frame time.

For every scored run LatencyPilot derives:

- AVG FPS = inverse of mean valid frame period;
- 1% low FPS from the worst 1% of controlled frame periods;
- 0.1% low FPS from the worst 0.1%;
- frame-p99 as diagnostic/tie context.

This is LatencyPilot's documented methodology. It is intentionally understandable and is not described as bit-for-bit identical to AutoGpuAffinity's historical low calculation.

## 6. GPU candidate search

Candidate generation uses actual Windows processor topology. One eligible logical representative is selected for each physical core; no even/odd numbering assumption is made and CPU0 is not banned.

Current v1 sequence:

```text
exact original/default state
→ 5 s non-scored original warm-up/reference
→ each eligible physical core:
     journaled apply/restart + stored-state verify
     5 s non-scored warm-up
     1 scored screening run
     exact rollback
→ rank valid screening candidates
→ best up to three:
     fresh apply/restart + stored-state verify
     5 s non-scored warm-up
     2 additional scored runs
     exact rollback
→ rank finalists from all three scored observations
→ apply winner once
→ final ETW placement-verification capture
→ Keep only when final runtime ISR placement is proved
   otherwise exact RestoreOriginal
```

There is no active SMT sibling-refinement phase in v1 and no ABBA/BAAB finalist loop.

### 6.1 Ranking order

Ranking is transparent and lexicographic; there is no hidden weighted score:

1. higher **median 1% low**;
2. higher median **0.1% low**;
3. higher median **AVG FPS**;
4. lower median **frame-p99** only as final deterministic diagnostic/tie context;
5. passive pressure/core/processor identity only as deterministic fallback.

An initial screening candidate has one scored observation. A finalist has three scored observations: its initial screen plus two fresh re-tests.

### 6.2 Repeatability

For finalists with repeated observations, 1% low is the primary stability signal. Current v1 bound:

```text
relative 1%-low spread = (max - min) / min <= 20%
```

Non-finite/non-positive ranking metrics or spread above the bound makes that finalist unrankable. If no finalist remains valid/repeatable, exact Original is retained rather than inventing a winner.

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
