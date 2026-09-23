# ADR 0007 — Paired local-control GPU affinity v2

Status: **Superseded for new evidence by ADR 0008** (2026-09-24)

This document remains the historical `gpu-affinity-benchmark-v2` contract. ADR 0008 owns new GPU measurement/ranking evidence. ADR 0006 remains authoritative for the narrow v1 product scope, mutation/recovery ownership, xHCI direction and final product sequencing.

> Historical note: the hard Original-cluster, pair-drift rejection and finalist decision-floor rules below describe v2 only. They must not be applied to v3 evidence.

## Decision

LatencyPilot measures GPU interrupt-affinity candidates with direct local pairs instead of distant block controls or pseudo-normalized FPS:

```text
Original before → Candidate → Original after
```

Raw observations remain unchanged. For a positive higher-is-better metric:

```text
reference = sqrt(originalBefore * originalAfter)
effect    = candidate / reference - 1
```

For lower-is-better frame p99:

```text
effect = reference / candidate - 1
```

Positive effect always means improvement-directed movement. The report/UI show the raw three observations plus the derived effect; they do not manufacture a normalized FPS value.

New paired-v2 evidence uses method id `gpu-affinity-benchmark-v2`. The report schema is `latencypilot-gpu-auto-affinity-report-v2`. Historical v1 evidence remains historical and is never reinterpreted as v2.

## Initial Original qualification

Before any candidate mutation:

1. run the existing 5 s non-scored Original warm-up;
2. collect 10 s scored Original observations;
3. require a three-observation 1%-low cluster, preferring ±3% of the cluster median;
4. if needed, collect observations four and five and permit the bounded recovery band up to ±6%;
5. if no bounded three-run cluster exists after five scored observations, verify exact Original and stop before the first candidate mutation.

Every observation remains in the audit trail. The accepted cluster qualifies the substrate; it is not reused as a local pair control.

## Pair stability and retry

After qualification, acquire a fresh 10 s Original control `O0`, then chain screening:

```text
O0 → C1 → O1 → C2 → O2 → ...
```

The allowed local Original movement is:

```text
clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)
```

A pair above that budget is unstable and cannot rank the candidate. One fresh retry is allowed. A second unstable attempt makes that candidate inconclusive. Two consecutive candidates that exhaust the bounded retry stop the search safely and retain exact Original. Structural evidence failures remain fail-closed immediately.

## Full-search strategy

### Stage A — physical-core representatives

Screen one eligible logical processor per physical core. Representative selection uses current eligibility/CPU-set evidence, then lower observed pressure, then deterministic processor number fallback. CPU0 is not globally banned and no even/odd SMT assumption is permitted.

### Stage B — SMT sibling refinement

From valid Stage-A pairs, keep the best two physical-core hypotheses plus a third only when it is within the 1% practical-equivalence margin of second place; hard cap three physical cores. Screen any still-untested eligible siblings on those cores.

### Stage C — finalists

From all valid logical-CPU screening pairs, advance the best two logical CPUs plus one additional CPU only when it is within the 1% practical-equivalence margin of second place; hard cap three finalists.

Screening scored windows are exactly 10 s. Short-screen ranking uses paired 1%-low effect; AVG and frame p99 are guardrail/context metrics and 0.1% low remains diagnostic.

## Finalist confirmation

Finalist evidence is separate from the 10 s screen. Start with a fresh 30 s Original control. Each finalist must obtain three independent valid 30 s local pairs, with deterministically shuffled order per round and the same single-retry stability rule.

Persist, per finalist:

- median 1%-low paired effect;
- median AVG paired effect;
- median frame-p99 paired effect;
- diagnostic median 0.1%-low effect where available;
- the pair numbers and local control movement;
- `DecisionFloor = max(1%, median finalist pair movement)`;
- verdict and reason.

A finalist is improvement-capable only when at least two of three 1%-low pair effects are positive, the median 1%-low effect exceeds the decision floor, no pair has a material primary regression beyond that floor, and AVG/frame-p99/interrupt-tail guardrails do not materially regress.

## Practical ties

Finalists are ordered by median paired 1%-low effect. A difference of at most one percentage point is a practical tie. Passive pressure/topology ordering may choose the operational target inside a tie, but the report/UI must explicitly say practical tie and must not claim the selected CPU proved faster than tied peers.

## Final Keep

A performance winner is not sufficient. Before Keep, LatencyPilot applies the chosen candidate once more, verifies stored state, performs the final warm-up and kernel-ETW capture, and requires clean attributable GPU ISR evidence confined to the chosen logical CPU with zero resolved off-target ISR. Terminal stored state must also verify.

If final runtime placement cannot be proved, exact Original is restored.

## Diagnostic scopes

`Custom` CPU scope runs the real Original qualification and local paired screening only for the selected CPUs. It is diagnostic-only, skips the machine-wide sibling/finalist tournament, never Keeps, always restores exact Original, and cannot close Gate A.

`OriginalDiagnostics` keeps the machine unmodified and captures the bounded Original-only sample set to assess pre-search variability.

## Evidence and UI authority

The v2 report persists raw trials, pair capture ids/effects/movement/retries/verdicts, finalist aggregates/decision floors, requested/validated processors, realized candidate/finalist order, durations, source provenance and terminal state. Derived execution metadata must come from the persisted raw trial/pair evidence rather than a second mutable record.

Presentation code must consume persisted `DecisionRank`/finalist authority; it must never re-rank shuffled execution data. The result surface exposes direct pair evidence (`Original before`, `Candidate`, `Original after`, local effect, control movement/budget) and labels non-Keep comparisons as diagnostic only.

`GateAClosureEligible` remains the stored compatibility field for source/evidence eligibility. UI copy says **Evidence eligible**, because that field does not mean physical Gate A has already closed.

## Safety and product scope preserved

This ADR does not arm public mutation, add global CPU isolation, add undocumented kernel mutation, expand to power/HAGS/BIOS/MSI/NIC/audio tuning, or infer MSI-X/vector topology from registry policy alone. Exact snapshot, journal ownership, restart/recreate, rollback, recovery and final-state verification remain mandatory.

Hosted CI verifies software contracts only. Physical Gate A still requires an exact clean green revision run on supported owner hardware, repeated whole-search evidence, Stop-safely/failure recovery evidence, and real Windows render/accessibility inspection.
