# GPU Auto-Affinity Ranked Search Design

Status: **Current canonical design**  
Date: 2026-09-17

This specification supersedes `2026-09-16-gpu-auto-affinity-benchmark-design.md` where the older design treated the Windows/original affinity as a winner that every forced-CPU candidate had to measurably beat.

## Product question

LatencyPilot Auto GPU Affinity answers:

> Among the eligible logical processors that can validly host the target GPU's interrupts, which processor produces the best repeatable frame-tail behavior under the fixed built-in graphics workload?

The Windows/original affinity remains:

- exact rollback/recovery state;
- a reference side in reports and balanced confirmation;
- evidence for contamination/continuity;

but it is **not** a fixed minimum-improvement gate for the forced-CPU ranking problem.

If no candidate remains decision-grade and repeatable, restore exact original state. Never manufacture a winner from invalid evidence.

## Reused components; no wheel reinvention

- **PresentMon 2.5.1 standalone console** supplies raw presentation/frame CSV. LatencyPilot pins the official executable and SHA-256. A separate PresentMon Service/API installation is not a Gate A prerequisite.
- **Sylvan.Data.Csv 1.4.4** parses PresentMon CSV rather than maintaining a custom CSV parser.
- **Vortice.Direct3D12 / Vortice.DXGI 3.8.3** remain the .NET bindings for the built-in deterministic D3D12 workload and adapter identity.
- **Microsoft TraceEvent + existing LatencyPilot ETW code** provide ISR/DPC evidence and placement proof.
- Existing mutation journal, exact-state comparer, GPU restart coordinator, safe cancellation and recovery paths remain authoritative.

Do not add liblava, Vulkan SDK, OCAT, a second benchmark stack or a custom PresentMon replacement for this feature.

## Candidate population

- screen one representative logical processor for every eligible physical core when physical-core count is within the v1 bound (maximum 16);
- CPU0 is eligible;
- passive DPC/ISR pressure is ordering/context only;
- preserve one frozen worker map, seed, calibrated workload, resolution and command/simulation load across the whole session;
- after the winning physical core is established, measure both SMT siblings when present.

## Measurement lifecycle

The physical report from 2026-09-17 demonstrated a systematic first-pass transient after GPU affinity apply/restart. Therefore every state transition that can restart/reinitialize the display path has a non-scored stabilization interval before decision evidence.

### Original reference

```text
original state
→ 5 s workload warm-up (not scored)
→ reference control #1
→ reference control #2
```

### Candidate screen

For each physical-core candidate:

```text
journal-owned ApplyCandidate
→ GPU activation/restart
→ verify stored candidate
→ 5 s post-transition warm-up (not scored)
→ scored whole-run #1
→ scored whole-run #2
→ verify placement/evidence
→ exact rollback to original
```

A warm-up is retained in the report but is never used in ranking statistics.

## Validity and rankability

A scored candidate block is rankable only when all of the following hold:

- exact source/method/workload/process identities are valid;
- target PnP + single DXGI hardware adapter identity is stable;
- stored candidate is verified before/after capture;
- kernel ETW integrity is clean;
- GPU ISR attribution source is present and consistent;
- at least one attributed ISR executes on the requested logical processor;
- zero attributed ISR events execute off target;
- raw PresentMon frame evidence is available for the benchmark process/window;
- D3D12 timestamp evidence is valid;
- two run-level frame-p99 values are finite/positive;
- `(max p99 - min p99) / min p99 <= 20%`.

`Inconclusive` blocks are not rankable. A generic Original-vs-candidate `Regressed`, `Tradeoff`, `NoMeasurableDifference` or `Improved` label is report context and does not by itself disqualify a valid forced-CPU candidate.

## Ranking

No weighted composite score.

For each rankable candidate:

```text
ranking value = median(run-level frame-p99)
lower is better
```

Deterministic tie-breaks:

1. lower observed passive pressure;
2. lower physical-core index;
3. lower logical processor number.

These tie-breaks provide deterministic output only; they do not override a lower measured frame-tail value.

## Fresh finalist re-screen

Take the best up-to-three rankable physical-core candidates and re-measure every one from a **fresh apply/restart**:

```text
apply/restart
→ 5 s non-scored warm-up
→ two fresh scored whole runs
→ repeatability/validity gate
→ rank again by median frame-p99
→ rollback
```

Only fresh re-screen evidence chooses the physical-core finalist. If none survives, restore original.

## SMT refinement

Measure the logical siblings of the winning physical core using the same transition warm-up, two scored runs, validity gate and median-p99 ranking. The best rankable sibling becomes the finalist.

## Balanced confirmation

Keep the existing fixed confirmation schedule:

```text
A B B A B A A B
```

`A` = exact original state.  
`B` = ranked finalist.

Each role receives a fresh 5-second non-scored warm-up after its state is established, followed by the scored confirmation run.

The finalist may be kept when:

- all candidate confirmation evidence is decision-grade;
- ISR attribution remains comparable;
- candidate-side repeated p99 spread remains <=20%;
- final stored candidate is verified;
- the owned experiment can be terminalized safely.

It does **not** need to exceed a generic 3% improvement threshold versus Windows default. Original comparison remains visible context. If confirmation is `Inconclusive` or candidate repeatability fails, rollback to exact original.

## PresentMon integration

Use the official standalone console surface supported upstream:

```text
--process_id
--output_file
--v2_metrics
--date_time
--timed
--terminate_after_timed
```

LatencyPilot starts PresentMon before the scored workload window, starts ETW, starts the controlled benchmark, then crops parsed CSV rows to the benchmark artifact's exact start/end timestamps. This avoids treating collector startup or shutdown as benchmark frames.

Collector resolution order:

1. explicitly supplied trusted path, if any;
2. LatencyPilot packaged `ThirdParty/PresentMon` path;
3. LatencyPilot-controlled versioned cache;
4. provision exact official v2.5.1 release asset into the cache.

Every candidate executable must match the pinned SHA-256 before execution. A failed download/hash/version/capture is a failed evidence path, not permission to use an arbitrary installed binary.

Gate A GPU identity continuity uses PnP + DXGI and does not depend on a PresentMon Service graphics-device ID. Multi/hybrid-GPU routing remains fail-closed until direct workload-to-adapter proof exists.

## Metrics

Primary ranking metric:

- run-level raw PresentMon CPU frame-time p99, median across repeated runs.

Evidence/guardrails/context retained:

- 1% low derived from p99;
- D3D12 GPU work timestamps;
- GPU-driver DPC duration;
- attributed GPU ISR duration;
- PresentMon CPU busy/wait, GPU time/latency/display latency when present;
- system CPU/background activity as context;
- exact state/driver/power/awake-time continuity.

Do not collapse these into an opaque score.

## User environment

The automatic benchmark should not require the user to manually close every ordinary application. Background activity is recorded/contextualized. Genuine contamination, identity drift or unstable repeated evidence fails/retries through the bounded validity rules.

For the cleanest physical Gate A evidence, the owner may still minimize unnecessary background load; this is validation hygiene, not a product prerequisite.

## Safety invariants

Unchanged:

- one owned mutation at a time;
- journal before mutation;
- exact stored-state verification;
- explicit GPU activation/restart result;
- runtime target/off-target ISR placement proof;
- exact rollback between candidates;
- safe cancellation owns rollback before terminal state;
- unresolved/diverged state never auto-overwritten;
- public mutation remains blocked until Gate B/C/D.

## Physical closure

Source/CI completion is not physical success. Gate A still requires an owner-local run on the exact green revision proving:

- standalone PresentMon collection works without separate service installation;
- every eligible physical core gets apply → warm-up → scored runs → rollback;
- first/second scored runs no longer show the systematic post-restart transient seen in the prior report;
- a fresh finalist re-screen produces at least one rankable candidate;
- final `finalProcessor` is non-null on a successful Keep;
- final candidate state and target-only ISR placement are verified;
- `recoveryStatus=clean-zero-unresolved`;
- a repeated whole search selects an equivalent finalist or gives an explicit evidence-based failure rather than arbitrary winner changes.

## External basis rechecked 2026-09-17

- GameTechDev PresentMon: current standalone console documentation and CLI source (`--process_id`, CSV output, V2 metrics, timed capture).
- GameTechDev PresentMon v2.5.1 release asset and digest.
- Sylvan.Data.Csv current documentation/release notes for header/quoted/async CSV parsing.
- AutoGpuAffinity prior art for active per-core benchmarking, repeated runs and post-change workload settling.
- Microsoft D3D12 timestamp/multithreading and Windows interrupt-affinity semantics already cited by the broader project documentation.
