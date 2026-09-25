# Benchmark Methodology

Status: **V0.12 benchmark contract**  
Last updated: 2026-09-25

LatencyPilot exists to distinguish measurable effects from placebo, ordinary run-to-run variation, workload drift and unsafe/unverified state. It is not a generic Windows tweak collection.

Current GPU measurement/search/ranking authority: **ADR 0011 — Restart-canonicalized observer-isolated GPU affinity v6**. ADR 0010 remains the historical v5 contract, ADR 0009 remains historical v4, and ADR 0006 remains authoritative for the broader v1 product scope, safety, mutation/recovery ownership and product sequencing.

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

This remains the authoritative steady/manual RealWorld contract. It is intentionally separate from the synthetic GPU candidate-search method.

### 1.3 Automatic GPU affinity — `gpu-affinity-benchmark-v6`

The product question is:

> Which structurally valid logical CPU is the best observed GPU interrupt-affinity option under the controlled workload, how large is the measured gain, and how uncertain is that ranking?

A separate question asks whether that CPU is safe/useful enough to Keep.

Windows/driver Original remains the exact reference/recovery state and is not CPU 0. CPU 0 is an explicit candidate. Historical `gpu-affinity-benchmark-v1` through `v5` evidence remains historical and is never reinterpreted as v6.

## 2. Source hierarchy

- Microsoft documentation owns Windows API/resource/ETW semantics and supported behavior.
- Measured local evidence owns what happens on the tested machine/workload.
- Maintained tools such as PresentMon are telemetry/cross-check sources, not automatic truth.
- Community reports and tuning videos are hypotheses/workflow references until reproduced.

## 3. Provenance and integrity

New benchmark artifacts may include optional `MeasurementWarnings` from an inspection of graphics-hook modules loaded in the benchmark process. Inspection runs outside the scored interval. These warnings are preserved in Gate A trial context and terminal reasons so an unstable Original/pair result can name an observed RTSS/NVIDIA interception path and advise a fresh session after the owner disables it. A loaded hook is interference context, not proof that it caused a measured regression; unavailable inspection remains unknown. Historical artifacts without this field are unchanged. This diagnostic context does not relax qualification, pair drift, ranking or final ISR-placement gates.

Authoritative GPU evidence records where applicable:

- exact source revision and source eligibility state;
- method/report schema version;
- Windows build and processor topology;
- target device and driver identity;
- benchmark process/frozen workload/seed/representative worker map + exact physical-core worker affinity masks;
- requested and actual interval;
- requested mutation and verified stored state;
- raw trial/capture identity;
- pair identity/attempt/verdict/effects/control movement/drift budget;
- finalist pair membership, robust noise/MAD context and Keep recommendation;
- requested/validated processors and realized execution order;
- final recommendation and terminal state.

Dirty or unverifiable source must not claim clean provenance. Missing evidence remains missing rather than being reconstructed from registry folklore.

## 4. DPC/ISR semantics

DPC/ISR counts are useful context but do not represent CPU cost by themselves. LatencyPilot also tracks execution duration and tail behavior. CPU0 concentration is evidence, not a universal CPU0-avoidance rule.

Stored interrupt configuration, allocated resources and runtime DPC/ISR behavior are separate evidence layers:

```text
stored policy ≠ allocated assignment ≠ runtime placement
```

A registry policy is therefore not runtime proof.

## 5. Controlled GPU benchmark

The built-in normal-user D3D12 benchmark performs one startup calibration and freezes:

- representative worker mapping and exact physical-core worker masks;
- command workload;
- simulation workload;
- seed;
- resolution;
- benchmark process identity.

Applying or rolling back GPU interrupt affinity restarts/activates the display adapter. The benchmark process remains alive for the search, while the D3D12 renderer/device is recreated after each transition before the next warm-up/scored observation. Frozen workload identity stays constant across candidates.

For Full and Selected-CPU paired searches, v6 also performs one **pre-score canonicalization restart** while the exact captured Original affinity policy is still active. The Original policy and driver version must verify before and after that in-place restart; the renderer is then recreated before any benchmark warm-up or scored Original observation. This gives the initial Original estimate the same post-restart device/renderer history that later Original controls receive after candidate rollback. If the restart cannot complete in place, exact Original cannot be re-verified, or renderer recreation fails, scored search evidence does not begin.

D3D12 GPU timestamps remain queue-owned evidence. PresentMon is not reinterpreted as direct GPU execution timing.

### 5.1 Controlled frame-period statistics

Each scored benchmark loop records its own controlled wall-clock frame period. For a valid scored observation LatencyPilot derives:

- AVG FPS = inverse of mean valid frame period;
- 1% low FPS from the worst 1% of controlled frame periods;
- 0.1% low FPS from the worst 0.1%;
- frame-p99 latency.

These are controlled comparison signals for the LatencyPilot workload, not claims about arbitrary-game click-to-photon latency.

## 6. Initial Original variability estimate

Before the first candidate mutation in Full or Selected-CPU paired search:

1. verify the exact captured Original affinity policy and driver version;
2. perform one real in-place display-adapter restart while that exact Original policy remains stored;
3. re-verify exact Original + driver identity and recreate the D3D12 renderer;
4. run a 5-second non-scored Original warm-up;
5. capture three scored Original observations of exactly 10 seconds;
6. compute median values and relative MAD (`median absolute deviation / median`);
7. if robust 1%-low variability is above the preferred 3% guide, extend to observation four and then five;
8. stop after at most five valid scored observations.

The canonicalization restart is comparison-state preparation, not a candidate mutation. It is deliberately omitted from Original-only diagnostics, whose contract remains no device restart and no affinity mutation.

All valid observations remain in the estimate and audit trail. A noisy but structurally valid Original does **not** block candidate testing. It lowers selection confidence.

## 7. Paired local-control screening

Capture a fresh 10-second Original control `O0` and screen candidates in a chained sequence:

```text
O0 → C1 → O1 → C2 → O2 → C3 → O3 ...
```

For each candidate, verify exact state, apply/restart/verify the candidate, run the bounded warm-up, capture the candidate, restore exact Original, and capture the adjacent `OriginalAfter`.

### 7.1 Local reference and effect

For higher-is-better metrics:

```text
LocalReference = sqrt(OriginalBefore × OriginalAfter)
LocalEffect    = Candidate / LocalReference - 1
```

For lower-is-better frame p99:

```text
LocalEffect = LocalReference / Candidate - 1
```

Raw observations remain authoritative. No pseudo-normalized FPS is manufactured.

### 7.2 Local drift and retry

Local Original movement remains a useful noise signal. If the first pair exceeds the existing bounded drift guide, reacquire a fresh Original control and retry once.

If the retry is still noisy **but structurally valid**, it remains rankable and its drift is persisted as uncertainty. Noise does not create an `Inconclusive` candidate and there is no consecutive-noise early-stop rule.

Structural evidence failures still fail closed immediately: invalid identity, non-finite controlled metrics, wrong stored state, healthy contradictory placement, failed rollback/recovery, or equivalent contract failure.

## 8. Full-search candidate strategy

### Stage A — physical-core representatives

Screen one eligible logical representative per eligible physical core with a 10-second local pair.

### Stage B — uncertainty-aware sibling refinement

A one-pair point estimate is not trusted as a hard cut. Keep at most four physical-core hypotheses whose observed effect plus bounded uncertainty can still overlap the leader, then screen their eligible siblings.

### Stage C — adaptive shortlist and recheck

Across all valid logical-CPU screens, retain the observed top four whenever four structurally valid CPUs exist. When fewer than four exist, retain all of them. After that guaranteed recheck set, admit at most one additional CPU whose median paired 1%-low effect plus bounded uncertainty remains within one percentage point of the current leader, for a hard maximum of five.

Bounded uncertainty is the maximum of one percentage point, effect MAD when repeated short evidence exists, and median local-control movement capped at that pair's drift budget.

Every shortlisted CPU receives one additional 10-second local pair. Clear losers do not.

Then rank shortlisted CPUs by the median of their short-screen evidence and advance only the top two logical CPUs.

## 9. Adaptive finalist confirmation

The top two finalists run two shuffled 15-second local pairs.

After the second round, compare their lead against measured uncertainty. If the lead exceeds uncertainty, confirmation stops. If they remain close, both receive one additional 15-second round. Three rounds are the maximum, not the default.

Persist the same auditable aggregates as v3. Automatic Keep additionally requires positive-pair consistency: both pairs positive when only two were required, or at least two of three when an uncertainty extension was needed.

Structurally valid finalists remain rankable even when their observations disagree.

### Physical-core worker symmetry

The synthetic workload keeps one worker per selected physical core, but each worker is allowed on the complete logical-processor mask of that physical core rather than being pinned to the first SMT sibling. The exact masks are frozen, included in workload identity, persisted in raw artifacts, and verified against captured Windows topology before evidence can be decision-grade. This removes a fixed-sibling contention asymmetry without changing the number of workers or recalibrating per candidate.

## 10. Ranking, confidence and practical ties

Finalists are ordered by median paired 1%-low effect. Rank 1 is the **best observed CPU**.

A difference of at most one percentage point remains a practical tie. A practical tie lowers confidence and must be disclosed, but it does not erase rank 1.

Selection confidence is explanatory metadata derived from already-collected evidence: lead over runner-up, effect MAD, Original robust variability, positive-pair consistency and practical-tie state. It is not a Keep gate and no hidden weighted score is used.

Keep is evaluated separately. A positive median primary effect and bounded performance guardrails are required before the final runtime-placement check; final target-only ISR proof remains mandatory.

0.1% low remains diagnostic tail context.

## 11. Interrupt-tail guardrails

When both Original and candidate/finalist evidence provide enough attributable samples across enough runs, GPU-driver DPC/ISR p99 tails may be compared as guardrails.

Tail guardrails must be noise-aware. Missing/insufficient optional tail evidence is not silently converted to zero and cannot be called a pass. Final Keep still requires explicit runtime placement proof independently of these comparative tail guardrails.

## 12. Screening collectors

### Controlled benchmark evidence — required

A rankable scored trial requires, among other structural checks:

- valid artifact/session identity;
- frozen-workload continuity;
- finite positive controlled frame-period statistics;
- exact expected stored affinity verified before/after the capture.

### PresentMon — best-effort independent cross-check

LatencyPilot uses a pinned standalone PresentMon console collector; a separately installed PresentMon Service/API is not a Gate A prerequisite.

For the pinned console path, process liveness alone is not treated as capture readiness. LatencyPilot waits until the uniquely named PresentMon ETW session is visible through the existing TraceEvent session-query API. A scored trial then runs a benchmark-owned **unscored** observer-active settle for at least 2 s and at most 4 s. A startup transient is defined relative to the observed settle cadence as `max(10 ms, 2 × initial-settle median frame period)`. Any frame at or above that threshold, including pending in-flight contexts drained before acceptance, resets a 500 ms quiet-tail requirement. Only after the quiet tail does the scored QPC window open. Warm-up trials with no external observer skip this settle. This moves observer startup outside the score; it does not delete or rewrite scored outliers. A bounded timeout or permission/query failure leaves PresentMon as explicit unavailable cross-check evidence rather than delaying indefinitely or fabricating readiness.

PresentMon cadence fields are not treated as interchangeable. Current `FrameTime`/supported cadence data may be used as cross-check evidence; unrelated start-offset semantics are not silently substituted.

If PresentMon is unavailable or yields no usable target rows, that state remains explicit context. It does not fabricate samples and does not invalidate otherwise valid benchmark-owned controlled-frame evidence.

### Kernel ETW during screening — best-effort unless it proves contradiction

If screening ETW is unavailable/dirty, the absence remains explicit context and ranking may continue when the benchmark/stored-state evidence is valid.

If healthy ETW proves resolved GPU ISR activity contradicts the requested candidate placement, that candidate is invalid.

## 13. Final Keep validity

A performance winner is not enough.

Before Keep:

1. apply the selected finalist once more;
2. verify exact stored candidate state;
3. run the final non-scored warm-up;
4. capture final kernel ETW;
5. require clean ETW integrity and zero lost events;
6. require attributable GPU ISR samples;
7. require requested target-only runtime placement with zero resolved off-target ISR;
8. verify terminal stored state.

If final runtime placement cannot be proved, exact Original is restored.

## 14. Diagnostic scopes

### Selected CPUs / Custom

Runs the real Original qualification and local paired 10-second screening only for the selected logical processors.

It is diagnostic-only:

- no machine-wide sibling/finalist tournament;
- no Keep;
- exact Original restored at the end;
- cannot close physical Gate A.

### Original diagnostics

Performs the bounded Original-only measurement path with no affinity mutation or device restart. It diagnoses pre-search variability only and cannot prove candidate benefit or restart stability.

## 15. UI/evidence semantics

Presentation distinguishes:

```text
raw observation
≠ paired derived effect
≠ best-observed rank
≠ selection confidence
≠ Keep recommendation
≠ verified terminal machine state
```

The UI consumes persisted rank rather than re-ranking shuffled execution order.

For the best observed CPU it shows, where available:

- CPU id;
- `High` / `Medium` / `Low` confidence;
- median Original → Candidate 1% low and AVG FPS;
- median Original → Candidate frame-p99 ms;
- absolute FPS/ms improvement;
- direct percentage change from the displayed median Original/Candidate values;
- separate drift-adjusted paired percentage effect used for ranking;
- MAD/noise context in details;
- practical-tie state;
- whether the CPU was kept or exact Original was restored.

Direct pair evidence remains available with Original before, Candidate, Original after, paired effect, control movement/noise guide, attempt and verdict.

A non-Keep CPU may still be the valid best observed CPU. `GateAClosureEligible` remains source/evidence eligibility only.

## 16. Physical evidence boundary

Hosted CI can verify software contracts and compilation. It cannot prove:

- actual GPU device restart/activation behavior;
- physical runtime ISR placement;
- whole-search reproducibility;
- Stop-safely/failure recovery on owner hardware;
- real WinUI rendering/accessibility;
- LocalSystem/package/signing behavior.

Physical Gate A therefore remains a separate requirement on one exact clean green `main` revision.

## 17. Interpretation rules

- Structurally valid evidence is ranked; ordinary noise lowers confidence rather than deleting rank 1.
- Do not call a low-confidence rank proof of superiority.
- Historical v1/v2/v3/v4/v5 aggregates are not reinterpreted as v6.
- Do not treat registry state as runtime placement proof.
- Do not hide noisy attempts or structural failures.
- Do not turn optional missing telemetry into zero or success.
- Do not lengthen warm-up or invent isolation machinery merely to force a cleaner result.
- Keep may restore Original even when the report has a best-observed CPU.

See ADR 0011 and `docs/PHASE3_PHYSICAL_VALIDATION.md` for the authoritative v6 procedure.
