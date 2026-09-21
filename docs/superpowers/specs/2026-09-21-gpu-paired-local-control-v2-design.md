# GPU Paired Local-Control Benchmark v2 Design

Status: **Proposed for owner review** (2026-09-21)

## Intent

LatencyPilot must answer one practical question on the current Windows 11 machine: **which logical CPU is a repeatably better GPU interrupt-affinity target under the controlled LatencyPilot workload?**

The v1 block-of-four time-local normalization method produced useful safety evidence but owner-hardware runs showed that large restart/time-order drift can overwhelm the candidate signal and create normalized values that are difficult to reason about. v2 replaces that measurement/ranking method with direct local paired controls while preserving the existing mutation, journal, rollback, device-restart, provenance and ETW-verification infrastructure.

Success means:

- a valid run can report a repeatable winner, a practical tie, or an inconclusive result without manufacturing precision;
- every candidate decision is auditable from raw `Original before -> Candidate -> Original after` values;
- strong local drift invalidates the pair instead of being transformed into candidate benefit;
- noisy runs terminate early instead of spending the full search budget after the measurement substrate is already untrustworthy;
- development iteration can restrict the search to an exact user-selected CPU subset through the existing Gate A developer UI;
- subset runs use the real hardware screening path but can never be mistaken for full-search closure evidence or auto-Keep authority.

## Scope and authority

This design supersedes only the GPU measurement, screening and ranking portions of ADR 0006 once accepted and implemented. The following existing contracts remain authoritative and unchanged unless implementation proves a direct conflict:

- exact original-state snapshot and journal ownership;
- apply/restart/stored-state verification;
- exact rollback and terminal-state verification;
- source/provenance assessment;
- final kernel-ETW runtime placement verification;
- public mutation remains unarmed;
- single Windows processor-group limitation for the current KAFFINITY mutation path;
- CPU0 is not globally banned;
- raw evidence is never rewritten to match a newer interpretation.

The report method identifier becomes `gpu-affinity-benchmark-v2`. v1 reports remain v1 evidence and are not reinterpreted.

## Explicit non-goals

v2 does **not** add:

- global `ReservedCpuSets` / `SetRTCores` isolation;
- undocumented `NtSetSystemInformation` mutation;
- power-plan, clock, HAGS, BIOS, MSI-mode, NIC/RSS or audio mutation;
- multi-processor/MSI-X GPU interrupt search;
- a new statistics framework;
- a new UI framework, persistence layer or IPC stack;
- a new permanent-test family merely to increase coverage.

Windows CPU Sets may be evaluated later only as a bounded benchmark-self-shielding experiment if paired v2 still shows material residual self-interference. They are not part of the first v2 implementation.

## Reuse before new construction

v2 reuses the existing:

- `GpuAutoAffinitySession` orchestration boundary;
- `GpuAffinityCandidatePlanner` topology, availability and pressure evidence;
- `ProcessorTopologyReader` and `ProcessorCpuSetReader`;
- GPU affinity mutation backend and durable mutation journal;
- GPU device restart / renderer recreation path;
- built-in D3D12 benchmark process and QPC evidence;
- PresentMon best-effort cross-check;
- kernel ETW screening/guardrail evidence;
- final runtime ISR placement verifier;
- Gate A helper, progress file, progress window and evidence export;
- Gate A source-state/provenance assessment;
- existing result presentation infrastructure.

No new product dependency is required for v2.

## Measurement model

### 1. Initial Original qualification

Before the first candidate mutation, capture the existing non-scored Original warm-up and establish a real scored Original regime using **10-second scored observations**, matching the v2 screening duration, and the existing three-of-up-to-five bounded cluster policy:

- prefer a three-run cluster within +/-3% of its median;
- from observation four onward permit the existing bounded recovery band up to +/-6%;
- stop before any candidate mutation when no bounded three-run Original cluster exists after five scored Original observations;
- preserve every scored observation in the audit trail.

`InitialOriginal1PercentLowNoise` is the maximum relative deviation of the accepted three-observation cluster from that cluster's 1%-Low median.

The accepted cluster qualifies the screening substrate and defines initial noise; it is **not** reused as a local pair control. After qualification, capture one fresh Original warm-up plus one fresh 10-second scored Original control `O0`. This avoids treating an excluded/stale qualification observation as the first local control.

### 2. Paired local-control sequence

Screening uses a chained sequence:

```text
O0 -> C1 -> O1 -> C2 -> O2 -> C3 -> O3 ...
```

For each candidate:

1. start from a verified exact Original state whose scored observation is the pair's `OriginalBefore`;
2. apply the candidate GPU interrupt affinity;
3. restart/activate the device and verify stored state;
4. perform the bounded non-scored transition warm-up;
5. capture one 10-second scored candidate observation;
6. restore and verify exact Original;
7. recreate/warm the benchmark renderer as required by the existing restart contract;
8. capture one 10-second scored Original observation as `OriginalAfter`;
9. evaluate the pair locally.

`OriginalAfter` becomes the next valid pair's `OriginalBefore`, so v2 does not capture two redundant Original controls between adjacent candidates.

There are no four-candidate temporal blocks, no linear interpolation and no normalization of a candidate back to a distant session baseline.

### 3. Raw values and local effect

Raw observations remain authoritative evidence and are persisted unchanged.

For positive-valued ratio metrics, the local reference is the equal-weight geometric mean of the two adjacent Original controls:

```text
LocalReference = sqrt(OriginalBefore * OriginalAfter)
```

For higher-is-better metrics such as 1% Low and AVG:

```text
LocalEffect = Candidate / LocalReference - 1
```

For lower-is-better metrics such as frame p99:

```text
LocalEffect = LocalReference / Candidate - 1
```

A positive local effect therefore always means improvement-directed movement. The UI and report show the raw three values and the derived percentage; they do not synthesize a normalized FPS value.

### 4. Pair stability

A pair's primary local-control movement is the relative difference between its two Original 1%-Low values.

The allowed pair movement is:

```text
PairDriftBudget = clamp(max(6%, 2 * InitialOriginal1PercentLowNoise), 6%, 10%)
```

This reuses the existing bounded 6% recovery concept while preventing a noisy starting regime from silently widening acceptance beyond 10%.

If `OriginalBefore -> OriginalAfter` exceeds the pair drift budget, the pair is `Unstable` and cannot rank the candidate. The candidate receives at most one fresh retry using the current verified Original observation as the new `OriginalBefore`.

If the retry also fails the drift budget:

- the candidate becomes `Inconclusive`;
- it is not ranked as a winner or loser;
- the session increments a consecutive-unstable-candidate counter.

Two consecutive candidates that remain unstable after their bounded retry cause an early safe abort: verify exact Original, close ownership cleanly and return an inconclusive session. A later valid candidate resets the consecutive counter.

Structural evidence failures remain fail-closed immediately and do not consume the statistical retry.

### 5. Screening duration and metrics

Screening is deliberately a filter, not final proof:

- scored candidate/control window: exactly 10 seconds;
- primary screening metric: local 1%-Low effect;
- screening guardrails/context: AVG and frame p99;
- 0.1% Low remains diagnostic in the short window and does not decide screening rank;
- ETW/PresentMon keep their current screening integrity roles.

The 10-second duration is one shared v2 method constant persisted in the report; the UI does not own a separate duration.

## Full-search candidate strategy

The full production/evidence search avoids spending a long scored run on every SMT sibling before a physical core has shown promise.

### Stage A — physical-core representatives

For every eligible physical core, select one representative logical processor using existing candidate evidence in this order:

1. eligible/unallocated active CPU set state;
2. lower observed pressure;
3. deterministic processor number fallback.

Do not assume even/odd numbering and do not ban CPU0.

Deterministically shuffle the representative candidates using a recorded session seed, then paired-screen every representative once.

### Stage B — sibling refinement

Rank only valid Stage A pairs by local 1%-Low effect with the existing 1% practical-equivalence margin. Select:

- the best two physical-core hypotheses;
- plus a third physical core only when it is within 1% of second place;
- hard cap: three physical cores.

For each selected physical core, paired-screen any still-untested eligible SMT sibling. Non-SMT cores require no refinement.

### Stage C — finalist selection

From all valid logical-CPU screening pairs, advance:

- the best two logical CPUs;
- plus one additional CPU only when it is within the existing 1% practical-equivalence margin of second place;
- hard cap: three finalists.

If fewer than two valid logical CPUs remain, finalist confirmation still runs for the available candidate but the report explicitly states the reduced comparison coverage.

## Finalist confirmation

Screening measurements are not mixed with finalist samples because the scored durations differ.

At the start of finalist confirmation, capture a fresh 30-second Original control. Each finalist then receives **three independent 30-second paired observations** under the same chained `OriginalBefore -> Candidate -> OriginalAfter` contract. Finalist order is deterministically shuffled per round and the seed/order are persisted.

For each finalist calculate:

- median paired 1%-Low effect;
- median paired AVG effect;
- median paired frame-p99 effect;
- diagnostic 0.1%-Low effect;
- per-pair control movement;
- existing GPU-driver DPC/ISR tail guardrails when sufficient ETW evidence exists.

A finalist is improvement-capable only when:

- at least two of its three valid paired 1%-Low effects are positive;
- its median paired 1%-Low effect exceeds `max(1%, median finalist pair-control movement)`;
- no valid pair shows a material 1%-Low regression beyond the same decision floor;
- AVG, frame-p99 and existing interrupt-tail guardrails do not show a material regression under their existing noise-aware semantics.

A finalist pair that is locally unstable follows the same single-retry rule. If a finalist cannot produce three valid pairs within that bounded retry policy, it is `Inconclusive` and cannot be kept.

## Winner, practical ties and final Keep

Finalists are ordered by median paired 1%-Low effect. Differences <=1% remain practical ties.

When two improvement-capable finalists are practically tied in performance, LatencyPilot may use the existing passive topology/pressure ordering to choose an operational target, but the report and UI must say **Practical tie** and must not claim that the selected CPU proved faster.

Before Keep:

1. apply the selected candidate once more;
2. verify stored state;
3. run the existing final benchmark-only warm-up;
4. capture final kernel ETW;
5. require healthy ETW with attributable GPU ISR samples confined to the selected logical processor;
6. verify terminal stored state.

Failure to prove final runtime placement restores exact Original.

Full-search outcomes are therefore one of:

- `Keep` — repeatable improvement plus final runtime placement proof;
- `PracticalTieKeep` — multiple equivalent improvement-capable finalists, deterministic passive selection, final runtime placement proof;
- `RestoreOriginalNoMeasuredWinner`;
- `RestoreOriginalInconclusive`;
- structural failure/recovery outcome using the existing safety contract.

The public product continues to receive only the conservative Keep/Restore truth; richer development labels remain evidence metadata.

## Development custom-CPU scope

### Purpose

The owner needs a **fast physical methodology-validation path** for testing exact CPUs without waiting for the full topology search and finalist tournament.

Custom scope is not a second measurement formula. It executes the exact same v2 10-second paired screening path with a restricted candidate source, then restores Original. It intentionally skips full-search physical-core expansion, finalist confirmation and final Keep because its purpose is rapid diagnostic iteration.

### UI placement

Reuse the existing collapsed `Developer validation` card on the Measure page. The current `DeveloperValidationHost` already contains the source-state badge and `Run Gate A` button.

Add a compact **CPU scope** button immediately beside `Run Gate A`:

```text
[ Evidence-ready ] [ Run Gate A ] [ All CPUs v ]
```

When a custom subset is active:

```text
[ Evidence-ready ] [ Run selected CPUs ] [ 4 CPUs v ]
```

The scope button opens a WinUI `ContentDialog` rather than expanding the card inline. This keeps the normal Measure page compact while giving enough room to inspect topology.

### CPU-selection dialog

The dialog contains:

- mode selector: `All eligible CPUs` / `Selected CPUs`;
- one selectable row/tile per logical CPU grouped by physical core;
- each entry shows logical CPU number and physical-core index;
- SMT siblings are visibly grouped under the same physical core;
- ineligible processors are disabled and show a concise reason from the existing CPU-set/topology evidence;
- `Select all eligible` and `Clear` actions;
- selected-count summary;
- validation that custom mode contains at least one eligible CPU;
- primary action `Use selection` and secondary action `Cancel`.

The selection is ephemeral development state. It resets to `All eligible CPUs` on app restart and is rebuilt whenever topology is recaptured. It is not written to product settings or SQLite.

The main card status explicitly says when a subset is active, for example:

```text
Custom diagnostic scope: CPU 4, CPU 8, CPU 12, CPU 6. Screening only; Original will be restored and this run cannot close Gate A.
```

### Helper contract

The app passes a custom subset to the existing elevated Gate A helper with one bounded argument:

```text
--candidate-cpus 4,8,12,6
```

Because the current mutation path supports exactly one processor group, these numbers mean logical processor numbers in group 0. The helper, not the UI, revalidates every requested processor against the fresh topology/candidate source before mutation.

Malformed, duplicate, out-of-range or currently ineligible processors fail before the first candidate mutation with an exact diagnostic.

The helper deterministically shuffles the validated custom processors using the session seed before screening so the UI selection order cannot become a time-order bias.

The report persists:

- `SearchScope = Full | Custom`;
- requested processors;
- validated processors actually screened;
- realized shuffled order;
- whether full topology coverage was achieved.

### Custom-mode safety semantics

A custom subset run is always diagnostic-only even on a clean exact source revision:

- `GateAClosureEligible = false`;
- execute only initial Original qualification + fresh `O0` + paired screening of selected CPUs + bounded pair retry/early-abort semantics;
- skip sibling expansion, three-pair finalist confirmation and final candidate Keep;
- no candidate may remain kept at the end of the session;
- exact Original is restored and verified before completion;
- the report may identify `BestWithinSelectedScope` from valid screening pairs, but must never label it the best CPU on the machine;
- the result UI says `Best within selected CPUs` and `Original restored`;
- custom mode cannot arm public mutation.

This gives a short real-hardware probe for methodology/debugging without creating a shortcut around the full-search evidence gate.

## Result and progress UX

The v2 result surface prioritizes direct evidence over rank decoration.

For each candidate pair show:

```text
CPU 8 · Core 4
Original before     198.2 FPS
Candidate           214.7 FPS
Original after      201.0 FPS
Local effect        +7.6%
Control movement    1.4%
Status              Advanced to finalist
```

Custom scope substitutes `Best within selected CPUs` / `Diagnostic only` language and never says `Advanced to finalist` because custom runs stop after screening.

Do not display normalized pseudo-FPS.

Full-mode finalist presentation shows the three independent paired effects and their median. The primary status is one of `Winner`, `Practical tie`, `No measured winner`, `Inconclusive`, or `Custom diagnostic result`.

Color semantics:

- green only for verified Keep / verified restored-safe terminal state where success semantics are accurate;
- accent/blue for leading diagnostic candidates;
- attention for uncertainty/tie;
- failure only for actual integrity/recovery failure.

The progress window shows the active phase and scope (`Full search` or `Custom: 4 CPUs`) and no longer presents a raw screening leader as though it were already a proven machine-wide winner.

## Report/data contract

The v2 report must make the decision reconstructable without UI inference. Add explicit records for:

- method version and search scope;
- shuffle seed and realized candidate order;
- initial Original qualification cluster/noise;
- fresh screening `O0`;
- per-pair `OriginalBefore`, `Candidate`, `OriginalAfter` artifact references;
- pair drift budget and observed movement;
- raw local effects by metric;
- retry relationship when applicable;
- pair verdict (`Valid`, `Unstable`, `Inconclusive`, structural invalidity where appropriate);
- physical-core representative/refinement provenance in full mode;
- finalist paired observations and median effects in full mode;
- practical-tie metadata;
- selected operational target, if any;
- final runtime ISR-placement evidence when Keep is attempted;
- exact terminal machine state and recovery status;
- full/custom coverage and closure eligibility.

The UI consumes persisted authoritative fields. It must not independently re-rank raw values.

## Error handling and early termination

The existing structural fail-closed rules remain.

Additional v2 measurement handling:

- initial Original cannot establish a bounded cluster -> stop before mutation;
- one unstable pair -> one fresh retry;
- retry remains unstable -> candidate inconclusive;
- two consecutive candidates remain unstable after retry -> early safe abort;
- custom requested CPU becomes invalid during fresh helper validation -> stop before mutation;
- no valid screening candidates -> RestoreOriginal/Inconclusive;
- finalist lacks three valid pairs -> finalist inconclusive, continue to another valid finalist if available;
- no improvement-capable finalist -> RestoreOriginalNoMeasuredWinner;
- final ETW cannot prove target-only placement -> restore Original.

Cancellation/Stop safely continues to use journal-owned rollback/recovery and must ignore the custom/full distinction for safety.

## Testing strategy

Keep the permanent test budget within the owner-authorized maximum and prefer modifying existing audit cases rather than adding new permanent methods.

Durable contracts to cover using the existing test surfaces:

1. v2 source contains no block-of-four interpolation/normalized pseudo-FPS authority;
2. candidate sequencing is chained `O-C-O` with exact rollback between candidates;
3. unstable pair receives at most one measurement retry and cannot rank;
4. repeated local instability triggers early RestoreOriginal;
5. custom scope revalidates the requested subset, performs screening only and never Keep/closure-qualifies;
6. full scope still derives from fresh eligible topology rather than UI input;
7. UI/result presentation consumes authoritative v2 pair/finalist decisions and uses `Best within selected CPUs` for custom runs;
8. final Keep still requires target-only runtime ISR proof and verified terminal state.

Temporary investigative tests may be created while implementing and removed before final delivery.

## Likely files changed

The implementation should remain concentrated in existing boundaries. Expected files include:

- `docs/adr/0007-paired-local-control-gpu-affinity-v2.md`;
- `docs/BENCHMARK_METHODOLOGY.md`;
- `docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md` (supersession/handoff only);
- `PROJECT_STATUS.md` / `ROADMAP.md` where current execution/closure semantics change;
- `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs`;
- `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`;
- `src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs`;
- `src/LatencyPilot.App/GateAValidationExperience.cs`;
- `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml(.cs)` only where v2 phase/scope presentation requires it;
- `tools/LatencyPilot.GateAValidation/Program.cs`;
- `tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs`;
- `tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs` where scope/phase persistence changes;
- existing critical-test source files that own GPU measurement/temporal/session contracts.

Do not create a new UI subsystem, selection persistence service, statistics package or alternate mutation backend for this work.

## Implementation stages

| Stage | Deliverable | Exit evidence |
|---|---|---|
| 1 | Accept v2 ADR/report semantics and mark v1 block normalization superseded | docs internally consistent |
| 2 | Versioned v2 pair/report model | source/tests compile against explicit v2 evidence |
| 3 | `O-C-O` screening, local effects, bounded retry and early abort | existing session/temporal contracts pass |
| 4 | full-search physical-core representative + sibling refinement + finalist selection | deterministic software contracts pass |
| 5 | three-pair finalist confirmation + practical-tie/winner policy | decision contracts pass |
| 6 | helper custom CPU scope and fresh privileged revalidation | invalid/custom/full scope contracts pass |
| 7 | Gate A CPU-scope GUI beside Run Gate A | owner-local Windows build + render inspection |
| 8 | v2 progress/results presentation | owner-local Windows build/render + result-contract tests |
| 9 | exact-head hosted Tests + diff/step-back review | green test-only CI on exact commit |
| 10 | quick physical custom run, initially suggested CPUs 4/8/12/6 | uploaded real v2 screening evidence; Original restored |
| 11 | full v2 physical search | valid winner/tie/inconclusive evidence |
| 12 | repeat full search for reproducibility | winner or practical-tie set reproduces, or method truthfully reports insufficient discrimination |
| 13 | Stop safely + one supported failure/recovery exercise | verified terminal state, unresolved journal = 0 |

## Step-back review / hidden-assumption check

The most likely hidden assumptions are:

- device restart effects may persist longer than the bounded warm-up;
- chained adjacent Original controls may still be too far apart when system state changes abruptly;
- a 10-second screen may mis-rank heavy-tail behavior that appears only in longer windows;
- selecting one representative per physical core may miss an SMT-sibling-specific effect;
- benchmark-generated scheduler load may still contaminate the IRQ target despite the paired design.

The design addresses these without prebuilding more infrastructure:

- abrupt restart/time effects invalidate local pairs instead of being normalized away;
- top physical cores explicitly receive sibling refinement;
- only screening uses short windows; final authority uses repeated 30-second pairs;
- custom scope gives a bounded real-hardware methodology probe without running the full tournament;
- if paired v2 still exhibits material residual self-interference, Windows CPU-set shielding becomes a separately evidenced follow-up rather than a bundled treatment;
- global core reservation remains outside the initial v2 path.

## Completion rule

v2 source is implemented when deterministic software contracts pass, the owner-local Windows build succeeds for the WinUI/Windows-specific projects, and hosted test-only CI is green on the exact source HEAD. v2 measurement is **not closed** until owner-hardware runs demonstrate that the paired method can either produce reproducible decision-grade evidence or truthfully identify that this workload cannot distinguish CPU targets on that machine.
