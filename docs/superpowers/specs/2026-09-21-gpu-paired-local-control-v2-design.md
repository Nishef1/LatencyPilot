# GPU Paired Local-Control Benchmark v2 Design

Status: **Implemented; archived design record** (2026-09-21)

Canonical decision authority now lives in [`../../adr/0007-paired-local-control-gpu-affinity-v2.md`](../../adr/0007-paired-local-control-gpu-affinity-v2.md). This file records the implementation intent that led to ADR 0007 and must not override current source, `PROJECT_STATUS.md`, or the ADR.

## Intent

Replace the former block/time-local normalization method with direct local paired controls while preserving the existing mutation, journal, rollback, device-restart, provenance and ETW-verification infrastructure.

The design question is:

> Which logical CPU, if any, shows a repeatable local paired improvement for GPU interrupt affinity on the current Windows 11 machine under the controlled LatencyPilot workload?

The method must be able to return a repeatable winner, practical tie or inconclusive result without manufacturing precision.

## Implemented measurement shape

```text
bounded Original qualification
→ fresh Original control O0
→ O0 → C1 → O1 → C2 → O2 ...
→ bounded physical-core representative screen
→ bounded SMT sibling refinement
→ up to 3 finalists
→ 3 independent 30 s local pairs per finalist
→ final target-only kernel-ETW ISR-placement proof before Keep
```

Screening scored windows are 10 seconds. Original qualification uses the existing bounded three-of-up-to-five cluster policy: ±3% preferred, bounded recovery up to ±6% after additional observations, and no candidate mutation when no bounded three-run cluster exists after five observations.

## Local pair semantics

For higher-is-better metrics:

```text
reference = sqrt(originalBefore * originalAfter)
effect    = candidate / reference - 1
```

For lower-is-better frame p99:

```text
effect = reference / candidate - 1
```

Raw observations stay unchanged. A positive effect means improvement-directed movement. The UI/report expose the raw three observations and derived effect rather than manufacturing pseudo-normalized FPS.

## Stability policy

```text
PairDriftBudget = clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)
```

A pair above the budget is unstable and cannot rank the candidate. One fresh retry is allowed. A second unstable attempt makes that candidate inconclusive. Two consecutive candidates that exhaust the bounded retry stop safely and retain exact Original.

Structural evidence failures remain fail-closed immediately.

## Search strategy

- **Stage A:** screen one eligible logical-processor representative per physical core using current CPU-set eligibility, lower observed pressure and deterministic processor-number fallback.
- **Stage B:** refine still-untested siblings only on the best two physical-core hypotheses, plus a third only when within the 1 percentage-point practical-equivalence margin; hard cap three cores.
- **Stage C:** advance the best two logical CPUs, plus one additional CPU only when within the same margin; hard cap three finalists.

CPU0 is not globally banned and even/odd SMT numbering is never assumed.

## Finalist authority

Each accepted finalist needs three valid independent 30-second local pairs. Persist median paired 1%-low/AVG/frame-p99 effects, diagnostic 0.1%-low effect where available, pair numbers and:

```text
DecisionFloor = max(1%, median finalist pair-control movement)
```

A finalist is improvement-capable only when at least two of three primary effects are positive, median 1%-low effect clears the decision floor, and primary/AVG/p99/supported interrupt-tail guardrails do not materially regress.

Finalists within one percentage point are a practical tie. Passive topology/pressure ordering may choose an operational target inside the tie, but must not be presented as proof that it measured faster.

## Final Keep

A performance winner is insufficient. Keep requires exact stored candidate state plus clean attributable target-only GPU ISR placement on the selected logical processor and verified terminal state. Failure restores exact Original.

## Diagnostic scopes

- `Custom` / selected CPUs: real paired screening on the requested subset, diagnostic-only, skips the machine-wide finalist tournament, never Keeps and always restores Original.
- `OriginalDiagnostics`: no affinity mutation or device restart; measures pre-search Original variability only.

## Non-goals preserved

Paired v2 does not add global CPU isolation, undocumented kernel mutation, power/HAGS/BIOS/MSI/NIC/audio tuning, multi-processor MSI-X search, a new statistics framework, a new UI framework or a new permanent-test family merely for coverage.

## Evidence/UI authority

Historical v1 reports remain v1 evidence. New method identity is `gpu-affinity-benchmark-v2`; report schema is `latencypilot-gpu-auto-affinity-report-v2`.

Presentation consumes persisted decision/finalist authority and direct pair evidence. It must not re-rank shuffled execution order. `GateAClosureEligible` remains a compatibility field for source/evidence eligibility; UI copy uses **Evidence eligible** because physical Gate A is a separate owner-hardware gate.

## Closure boundary

Hosted CI verifies software contracts only. Physical Gate A still requires one exact clean green revision, a full paired-v2 search, whole-search repeatability evidence, Stop-safely/failure recovery evidence and real Windows render/accessibility inspection.

For the current authoritative contract, use ADR 0007 and `docs/PHASE3_PHYSICAL_VALIDATION.md`.