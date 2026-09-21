# Benchmark Methodology

Status: **V0.12 benchmark contract**  
Last updated: 2026-09-21

LatencyPilot exists to distinguish measurable effects from placebo, ordinary run-to-run variation, workload drift and unsafe/unverified state. It is not a generic Windows tweak collection.

Current GPU measurement/search/ranking authority: **ADR 0007 — Paired local-control GPU affinity v2**. ADR 0006 remains authoritative for the broader v1 product scope, safety, mutation/recovery ownership and product sequencing.

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

### 1.3 Automatic GPU affinity — `gpu-affinity-benchmark-v2`

The product question is:

> Which logical CPU, if any, shows a repeatable local paired improvement for GPU interrupt affinity on the current machine under LatencyPilot's controlled workload, while preserving guardrails and final runtime placement proof?

Windows default is the exact reference/recovery state. It is not an opponent that a forced CPU must beat by a fixed folklore percentage.

Historical `gpu-affinity-benchmark-v1` evidence remains historical and is never reinterpreted as v2.

## 2. Source hierarchy

- Microsoft documentation owns Windows API/resource/ETW semantics and supported behavior.
- Measured local evidence owns what happens on the tested machine/workload.
- Maintained tools such as PresentMon are telemetry/cross-check sources, not automatic truth.
- Community reports and tuning videos are hypotheses/workflow references until reproduced.

## 3. Provenance and integrity

Authoritative GPU evidence records where applicable:

- exact source revision and source eligibility state;
- method/report schema version;
- Windows build and processor topology;
- target device and driver identity;
- benchmark process/frozen workload/seed/worker map;
- requested and actual interval;
- requested mutation and verified stored state;
- raw trial/capture identity;
- pair identity/attempt/verdict/effects/control movement/drift budget;
- finalist pair membership and decision floor;
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

- worker mapping;
- command workload;
- simulation workload;
- seed;
- resolution;
- benchmark process identity.

Applying or rolling back GPU interrupt affinity restarts/activates the display adapter. The benchmark process remains alive for the search, while the D3D12 renderer/device is recreated after each transition before the next warm-up/scored observation. Frozen workload identity stays constant across candidates.

D3D12 GPU timestamps remain queue-owned evidence. PresentMon is not reinterpreted as direct GPU execution timing.

### 5.1 Controlled frame-period statistics

Each scored benchmark loop records its own controlled wall-clock frame period. For a valid scored observation LatencyPilot derives:

- AVG FPS = inverse of mean valid frame period;
- 1% low FPS from the worst 1% of controlled frame periods;
- 0.1% low FPS from the worst 0.1%;
- frame-p99 latency.

These are controlled comparison signals for the LatencyPilot workload, not claims about arbitrary-game click-to-photon latency.

## 6. Initial Original qualification

Before the first candidate mutation:

1. run a 5-second non-scored Original warm-up;
2. collect scored Original observations of exactly 10 seconds;
3. after three observations, require a three-run 1%-low cluster within the preferred ±3% median-relative band;
4. if absent, collect observation four and re-evaluate every three-run combination;
5. after observation four exists, the existing bounded recovery band may accept the tightest three-run cluster up to ±6%;
6. if still absent, collect observation five and re-evaluate under the same preferred-then-bounded-recovery rule;
7. if no eligible three-run cluster exists after five scored observations, verify/retain exact Original and stop before any candidate mutation.

Every scored observation remains in the audit trail. The accepted cluster qualifies the measurement substrate and yields the accepted Original 1%-low noise. It is **not** reused as a local candidate control.

## 7. Paired local-control screening

After qualification, capture a fresh 10-second Original control `O0` and screen candidates in a chained sequence:

```text
O0 → C1 → O1 → C2 → O2 → C3 → O3 ...
```

For each candidate:

1. start from a verified exact Original state and a scored `OriginalBefore` control;
2. apply the candidate and activate/restart the device;
3. verify the stored candidate state;
4. run the bounded 5-second non-scored transition warm-up;
5. capture one 10-second scored candidate observation;
6. restore and verify exact Original;
7. recreate/warm the renderer as required;
8. capture one 10-second scored `OriginalAfter` control;
9. evaluate the local pair.

`OriginalAfter` becomes the next pair's chain anchor only when the pair/control state is acceptable. Unstable retries acquire a fresh valid control rather than treating a failed pair as a trustworthy anchor.

### 7.1 Local reference and effect

Raw observations remain authoritative and unchanged.

For positive-valued higher-is-better metrics:

```text
LocalReference = sqrt(OriginalBefore × OriginalAfter)
LocalEffect    = Candidate / LocalReference - 1
```

For lower-is-better frame p99:

```text
LocalEffect = LocalReference / Candidate - 1
```

Positive effect always means improvement-directed movement.

The report/UI may show the raw three values plus the derived effect. It must not synthesize pseudo-normalized FPS and present that as a measured observation.

### 7.2 Pair drift budget

Primary pair movement is the relative movement between the two adjacent Original 1%-low controls.

```text
PairDriftBudget = clamp(max(6%, 2 × InitialOriginal1PercentLowNoise), 6%, 10%)
```

If Original movement exceeds the budget, the pair is `Unstable` and cannot rank the candidate.

One fresh retry is allowed. If the second attempt also exceeds the budget, the candidate becomes `Inconclusive`. Two consecutive candidates that exhaust their retry cause an early safe stop with exact Original retained. A later valid candidate resets the consecutive-unstable counter.

Structural evidence failures remain fail-closed immediately and do not consume the statistical retry.

## 8. Full-search candidate strategy

The full search spends short screens broadly and long confirmation narrowly.

### Stage A — physical-core representatives

For every eligible physical core, choose one eligible logical processor using:

1. current CPU-set availability/allocation evidence;
2. lower observed pressure;
3. deterministic processor-number fallback.

Do not assume even/odd SMT numbering and do not globally ban CPU0.

Each representative receives one valid paired short screen.

### Stage B — sibling refinement

Order valid Stage-A hypotheses by paired 1%-low effect. Select:

- the best two physical-core hypotheses;
- plus a third only when it is within the 1 percentage-point practical-equivalence margin of second place;
- hard cap: three physical cores.

Screen any still-untested eligible SMT sibling on those selected cores.

### Stage C — finalists

From all valid logical-CPU screening pairs, advance:

- the best two logical CPUs;
- plus one additional CPU only when it is within the same 1 percentage-point margin of second place;
- hard cap: three finalists.

The 10-second screen is a filter, not final proof. Primary ranking uses paired 1%-low effect. AVG/frame-p99 are guardrail/context signals; 0.1% low remains diagnostic in the short window.

## 9. Finalist confirmation

Finalist evidence is separate from the 10-second screening sample.

1. capture a fresh 30-second Original control;
2. run three rounds;
3. deterministically shuffle finalist order per round;
4. each finalist must obtain three independent valid 30-second local pairs;
5. apply the same one-retry local-stability rule to a finalist pair.

Persist per finalist:

- the three accepted pair numbers;
- median paired 1%-low effect;
- median paired AVG effect;
- median paired frame-p99 effect;
- median diagnostic 0.1%-low effect where available;
- local pair-control movement;
- `DecisionFloor`;
- verdict and reason.

### 9.1 Finalist decision floor

```text
DecisionFloor = max(1%, median finalist pair-control movement)
```

A finalist is improvement-capable only when:

- at least two of its three valid paired 1%-low effects are positive;
- median paired 1%-low effect is above the decision floor;
- no valid primary pair shows a material regression beyond that floor;
- median paired AVG does not materially regress;
- median paired frame-p99 does not materially regress;
- sufficiently supported GPU-driver DPC/ISR tail evidence does not materially regress.

A finalist that cannot obtain three valid pairs within the bounded retry policy is `Inconclusive` and cannot be kept.

## 10. Ranking and practical ties

Finalists are ordered by median paired 1%-low effect.

Differences of at most one **percentage point of paired effect** are practical ties. LatencyPilot may use passive observed-pressure/topology/processor identity ordering to select one operational target among tied improvement-capable finalists, but the report/UI must say **Practical tie** and must not claim the chosen target proved faster than tied peers.

There is no hidden weighted score.

0.1% low remains visible tail context but does not independently overturn the primary paired-v2 authority.

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

For the pinned console path, process liveness alone is not treated as capture readiness. LatencyPilot waits for PresentMon's explicit `Started recording.` console marker before returning the optional collector session, then still runs the benchmark-owned bounded unscored observer-settle work before opening the scored QPC window. If the marker is not observed within the bounded startup deadline, PresentMon remains explicit unavailable cross-check evidence rather than delaying indefinitely or fabricating readiness.

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

Presentation must distinguish:

```text
raw observation
≠ paired derived effect
≠ persisted decision/finalist authority
≠ verified terminal machine state
```

The UI must consume persisted decision/finalist authority rather than re-ranking shuffled execution order.

For direct pair evidence it should expose, where available:

- Original before;
- Candidate;
- Original after;
- paired effect;
- control movement;
- drift budget;
- attempt/verdict.

A non-Keep candidate is diagnostic/comparison-only even if it shows a positive measured effect.

`GateAClosureEligible` is a compatibility field for source/evidence eligibility. UI copy uses **Evidence eligible** because the field does not mean physical Gate A has already closed.

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

- Do not call a positive single pair a proven winner.
- Do not compare historical v1 normalized aggregates as though they were paired-v2 effects.
- Do not treat registry state as runtime placement proof.
- Do not hide unstable/inconclusive evidence.
- Do not turn optional missing telemetry into zero or success.
- Do not lengthen warm-up or add new isolation machinery merely because a run is noisy; first inspect the paired evidence and identify a falsifiable cause.
- Prefer Restore Original to manufacturing certainty when the evidence contract is not satisfied.

See ADR 0007 and `docs/PHASE3_PHYSICAL_VALIDATION.md` for the authoritative paired-v2 Gate A procedure.
