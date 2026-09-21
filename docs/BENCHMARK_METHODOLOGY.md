# Benchmark Methodology

Status: **V0.11 benchmark contract**  
Last updated: 2026-09-21

LatencyPilot exists to distinguish measurable effects from placebo, ordinary run-to-run variation, workload drift and unsafe/unverified state. It is not a generic Windows tweak collection.

Current GPU auto-affinity authority: **ADR 0006**, including its 2026-09-20 time-local measurement amendment. The steady RealWorld evidence product and the deterministic GPU candidate-search method are intentionally different measurement shapes.

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

> Among the tested valid logical-processor interrupt targets, which CPU gives the strongest repeatable controlled benchmark result after measured time-local background movement is accounted for?

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

D3D12 GPU timestamps remain queue-owned evidence. The benchmark queries the command queue's timestamp frequency and converts resolved timestamp deltas using that queue frequency; it does not reinterpret PresentMon telemetry as direct GPU execution timing.

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
     retain all four runs and use observed per-metric variance as the noise floor
→ screen every eligible logical CPU in bounded blocks of at most four candidates:
     for each candidate:
       journaled apply/restart + stored-state verify
       5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
       1 scored 30 s screening run
       exact rollback + Original-state verification
     after every full four-candidate block when candidates remain:
       5 s non-scored Original warm-up
       collect one fresh scored Original block control
→ 5 s non-scored Original warm-up
→ collect one fresh scored Original control after the completed sweep
→ use the Original controls to measure time-local background movement around each candidate block
→ normalize rankable candidate decision metrics back to the session Original baseline
   raw candidate/control trials remain unchanged in the audit trail
   local control movement remains explicit uncertainty and raises Keep thresholds
→ if effective 1%-low variability = max(Original noise, time-local control drift) exceeds 15%:
     keep normalized screening diagnostics
     skip exhaustive finalist confirmation
     verify exact Original and RestoreOriginal
→ otherwise rank normalized valid screening candidates
→ shortlist the best three plus candidates within min(3%, max(1%, effective screening variability)) of the third-place cutoff, capped at five finalists
→ shortlisted candidates:
     two independent re-test rounds are mandatory
     each finalist gets fresh apply/restart + stored-state verify, warm-up, scored run, exact rollback
     only finalists still lacking a stable 3-run cluster receive one replacement round
→ prefer the tightest stable 3-run cluster selected from at most 4 scored observations
   at most one scored observation is excluded when such a cluster exists
   if four valid runs still do not cluster, retain all four and use their observed variance in decision thresholds
→ 5 s non-scored Original warm-up
→ collect one fresh scored Original control after finalist re-tests
   merge this phase's control movement into time-local uncertainty
→ test finalists in ranking order against Original/noise/time-local uncertainty and guardrails; if the first fails, try the next clean finalist
→ apply the highest-ranked clean winner once
→ final benchmark-only warm-up → ETW placement-verification capture
→ Keep only when measured improvement clears the full noise/uncertainty floor,
   comparable GPU-driver DPC/ISR p99 does not materially regress,
   and final runtime ISR placement is proved
   otherwise exact RestoreOriginal
```

The Original block controls are **measurement controls, not abort triggers for ordinary gradual drift and not candidate observations**. They let the optimizer separate a candidate's measured effect from background movement that occurs as a long search proceeds. For each rankable screening candidate, the decision metric is normalized by the relevant local Original level relative to the session Original baseline. The raw scored observation remains unchanged and visible for diagnostics.

This normalization is deliberately bounded. Measured control movement is carried forward as uncertainty and therefore raises the minimum improvement required for Keep. If effective 1%-low variability exceeds the existing 15% exhaustive-confirmation budget, LatencyPilot stops before expensive finalist re-tests and restores Original. Structural evidence failures are never normalized away.

There is no separate SMT/hyperthread refinement phase in v1 because eligible siblings are screened directly, and there is no ABBA/BAAB finalist loop.

### 6.1 Ranking order

Ranking is transparent and lexicographic; there is no hidden weighted score. Ranking uses the optimizer's **decision aggregates** after time-local normalization where applicable, not shuffled raw collection order:

1. compare median **1% low**; differences <=1% are treated as practical ties;
2. if tied, compare median **AVG FPS** with the same 1% equivalence margin;
3. if tied, compare lower median **frame-p99** with a 1% equivalence margin;
4. if still tied, compare median **0.1% low** only when the relative difference exceeds 5%;
5. if still tied, use passive core-pressure/core/processor identity only as deterministic fallback.

The 1% comparison margin follows the practical repeatability scale expected from a well-behaved benchmark rather than pretending that tiny decimal differences identify a real winner.

`0.1% low` remains visible because it is useful for spotting severe tail spikes, but a 30 s run can contain only a small number of frames in the worst 0.1%; it therefore does not outrank the more stable 1% low/AVG/p99 signals.

An initial screening candidate has one 30 s scored observation. A finalist has three scored observations: its initial screen plus one fresh score in each of two separate re-test rounds. No finalist receives two scored re-tests back-to-back under one affinity activation.

### 6.2 Repeatability and uncertainty

Repeatability no longer uses raw `(max - min) / min` spread. A single multitasking spike can invalidate that statistic and its `min` denominator biases the reported variation toward the worst run.

LatencyPilot uses bounded robust sampling instead:

- 1% low is the primary repeatability signal.
- Start with three scored observations.
- Evaluate every 3-observation combination and select the **tightest** cluster whose members are each within **±3% of that cluster's median 1% low**.
- If no preferred cluster exists, collect one replacement observation and re-evaluate. No fifth score is collected.
- Three valid runs are sufficient for a preferred cluster; four scored attempts are the hard maximum.
- If a preferred cluster exists, at most one scored observation may be excluded and the excluded sample remains in the audit trail.
- If four valid runs still do not form a ±3% 1%-low cluster, **do not discard the benchmark evidence**. Median 1% low / 0.1% low / AVG / frame-p99 are computed from all four runs, and each metric's maximum median-relative deviation becomes observed repeatability noise for later comparisons.
- AVG FPS and frame-p99 remain decision guardrails; 0.1% low remains rare-tail diagnostic/regression context rather than the outlier detector.
- Time-local Original-control movement is tracked separately from within-state repeatability noise. The optimizer carries both into decision thresholds rather than pretending they are the same phenomenon.
- Final AVG / frame-p99 / 0.1%-low regression guardrails are noise-aware, using the larger of the documented minimum margin, observed Original/finalist run-to-run noise and relevant time-local uncertainty.
- When both Original and finalist provide at least three scored runs with enough attributable samples, GPU-driver DPC/ISR p99 is computed per run. Median tail regression is compared against a noise-aware limit of `max(10%, Original run-to-run tail noise, finalist run-to-run tail noise)`; a regression beyond that limit rejects that finalist without preventing the next ranked finalist from being considered.
- The final winner's 1%-low gain must clear `max(1%, Original 1%-low noise, finalist 1%-low noise, time-local control uncertainty)` before guardrails and final ETW placement verification are considered.

The ±3% band is a **preferred-cluster rule**, not a universal claim about Windows variance and not a whole-sweep hard-abort threshold. A noisy but otherwise valid Original baseline can therefore continue into CPU screening; its instability makes the Keep threshold harder to clear. Gradual control movement during screening is normalized and recorded as uncertainty. Robust replacement sampling is applied only to Original and finalists where evidence drives Keep/Restore.

### 6.3 Adaptive finalist cutoff

The initial screen advances at least the best three rankable logical CPUs when effective background variability remains within the 15% exhaustive-confirmation budget. The finalist equivalence band is **min(3%, max(1%, effective screening variability))**, where effective screening variability is the larger of Original 1%-low repeatability noise and measured time-local 1%-low control movement. The shortlist is capped at five candidates.

A fresh Original block control is taken after each group of four candidates when more candidates remain, and a final screening control closes the last block. These controls normalize screening decision evidence and quantify uncertainty; ordinary gradual movement does not automatically invalidate the block. If the resulting effective variability exceeds 15%, LatencyPilot retains the screening diagnostics and restores Original without expensive finalist re-tests because the environment is too variable for a trustworthy automatic Keep decision. A second fresh Original control after finalist re-tests contributes additional time-local uncertainty before Keep evaluation.

## 7. Screening evidence and external collectors

Screening must remain robust enough to complete the bounded CPU search while preserving structural integrity.

### Controlled benchmark evidence — required

- valid artifact/session identity;
- frozen-workload continuity;
- valid D3D12 timestamp evidence;
- valid controlled frame periods;
- exact stored affinity verified around candidate capture.

### PresentMon — independent best-effort cross-check

LatencyPilot uses the pinned standalone PresentMon 2.5.1 console collector; a separately installed PresentMon Service/API is not a Gate A prerequisite.

PresentMon's documented timing fields are not interchangeable:

- `FrameTime` is the current CPU frame-time metric in the capture schema;
- `MsBetweenPresents` = time between Present() calls;
- `MsBetweenAppStart` = start of the current frame until CPU work begins for the next frame.

Therefore the console CSV parser accepts the current `FrameTime` field or legacy `MsBetweenPresents` for present cadence; it does **not** substitute `MsBetweenAppStart` as the same metric. Failed raw CSV may be retained for owner diagnosis, but retention is bounded.

When PresentMon returns `NoSwapChains` or otherwise provides no usable target frames, that state remains explicit diagnostic evidence. It does not fabricate guardrail samples and does not invalidate an otherwise valid scored trial when benchmark-owned controlled frame periods are present. Without benchmark-owned frame periods, the legacy PresentMon-dependent path remains fail-closed.

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

Mutation ownership starts before state is changed and remains owned until exact Keep or Revert terminalization. Before every candidate write, stored affinity and display-driver version must still match the exact session-original snapshot. Transaction preparation repeats that same comparison inside the mutation lock, and the existing immediate-prewrite reread rejects any later TOCTOU drift before registry write.

- External affinity/driver drift before apply → refuse before write.
- Candidate failure after apply → exact rollback + original verification.
- Cancellation before Keep → exact rollback + original verification.
- Ordinary time-local Original-control movement → normalize decision metrics, record uncertainty, continue while structural evidence remains valid and effective variability stays inside the finalist-confirmation budget.
- Effective 1%-low variability >15% after the completed screen → skip finalist confirmation, verify exact Original, RestoreOriginal.
- Invalid benchmark/session provenance, state divergence, failed mutation ownership, healthy ETW proving wrong placement or another structural integrity failure → fail closed; do not normalize it away.
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

Development Gate A result presentation distinguishes three evidence layers:

1. raw scored trial history;
2. authority-selected decision aggregates/rank after time-local normalization where applicable;
3. verified terminal machine state.

A diagnostic comparison candidate is not labelled as kept when the final recommendation is RestoreOriginal. Raw shuffled measurement order must never be reinterpreted in the UI as rank.

Do not label a proxy as network latency, click-to-photon latency or another quantity that was not measured.

## 12. Future/non-v1 methods

MSI-mode mutation, NIC/RSS automatic mutation, audio affinity, power-plan mutation and cross-subsystem/Pareto automatic optimization are future/non-v1 work and do not enter the automatic v1 decision path. Existing conservative source/recovery code may remain where it supports exact restoration or future experiments, but it cannot silently enter v1 automation.

## 13. Auditability and observer effect

Authoritative experiments retain enough data to audit the decision later. Historical evidence is not rewritten to fit new interpretation logic. Time-local normalization creates new decision aggregates while retaining the raw candidate/control trials that produced them.

During authoritative capture avoid unnecessary per-event allocation, synchronous high-volume file I/O, frequent UI redraw and avoidable GC pressure. Benchmark progress reporting remains low frequency after calibration.

## 14. Permanent-test policy

The permanent suite is behavior-focused and must not grow one test per implementation detail. Temporary TDD characterization tests are removed after the behavior is represented in canonical tests. Physical benchmark repetitions and Gate A runs are evidence, not unit tests.

## Audit-closure one-at-a-time workflow (updated 2026-09-21)

LatencyPilot treats v1 automatic optimization as a one-at-a-time experiment pipeline: **Original measurement → GPU affinity → primary-input/xHCI → final verification → report**. A stage that is NotReady, structurally invalid, requires recovery attention, or requires reboot stops the pipeline; ordinary bounded time-local GPU background movement is handled inside the GPU measurement stage by normalization + uncertainty rather than being mislabeled as a structural failure.

MSI-mode mutation remains outside the v1 automatic path under ADR 0006. Existing MSI inspection/recovery code is not proof of automatic applicability and must not be inserted into v1 ordering without a new accepted authority change.

xHCI affinity requires one explicit primary Raw Input identity, one exact USB route, clean capture evidence, and reversible journal ownership.

`Restore original settings` replays retained LatencyPilot changes newest-first from exact snapshots. It does **not** claim to restore Windows defaults. Public mutation remains fail-closed (`MutationAvailable = false`) until exact-revision physical GPU/xHCI validation is recorded; hosted CI proves source contracts only.