# ADR 0005 — Video-faithful Gate A ranking from in-app frame periods

Status: **Accepted** (owner-directed, 2026-09-18)
Supersedes: nothing. Amends the Gate A validity contract described in
`docs/PHASE3_PHYSICAL_VALIDATION.md` and `docs/BENCHMARK_METHODOLOGY.md`.

## Context

The manual per-core GPU affinity workflow this product automates is:

```text
per CPU core: set affinity mask → restart → run 3D benchmark
→ record AVG FPS, 1% low, 0.1% low → pick the best core → keep it
```

The decision signal is the benchmark application's own frame rate
statistics. No external frame collector participates in the decision.

Gate A as previously specified required a pinned standalone PresentMon
capture, kernel ETW integrity, and resolved ISR placement **for every
trial to count at all**. Owner-local evidence (Gate A run
`gpu-auto-affinity-20260917T213830632Z`) proved the failure mode: the
D3D12 benchmark produced 800 healthy frames per trial while the external
collector produced zero usable rows, so the whole search aborted at the
first warm-up without testing a single CPU — even though the exact
video-style signal was present in every artifact.

## Decision

1. **Primary ranking signal** is the benchmark's own wall-clock frame
   periods (`FramePeriodMilliseconds` per artifact frame), interpreted as
   AVG FPS / frame-p99 / 1% low / 0.1% low. This is the automated
   equivalent of the manual workflow's decision table.
2. **Standalone PresentMon and kernel ETW degrade to best-effort
   guardrails.** When present they contribute cross-checks and
   driver-duration guardrails; when absent the trial stays valid and the
   absence is recorded as explicit context, never as a silent pass.
3. **Stored-state verification stays hard.** Every trial still requires
   the exact registry affinity state verified before and after capture,
   plus artifact identity, frozen-workload identity, and continuity.
4. **ISR placement proof applies when attempted.** With healthy ETW,
   attribution is attempted and a failed placement invalidates the
   trial. Without ETW there is no proof either way, so the trial is
   rankable but explicitly flagged `placement unverified`.
5. **Keep never leaves the machine measurably slower.** Confirmation
   `Regressed` restores the exact original state instead of keeping.
   `Improved`, `NoMeasurableDifference`, and `Tradeoff` (visible
   guardrail cost) can keep after a repeatable ABBA + BAAB confirmation.
6. **USB affinity stays manual and gated.** The manual workflow's second
   half (lowest-DPC CPU → USB mask → reboot) is not automated: USB/xHCI
   mutation remains deferred until Gate A proves the shared substrate
   physically, per the existing contract. The App already surfaces
   read-only USB route evidence and per-CPU interrupt distribution for
   the manual step.

## Consequences

- Gate A can complete and rank on benchmark evidence alone; external
  collector outages degrade confidence visibly instead of aborting the
  search.
- `latencypilot-gpu-benchmark-v1` artifacts now carry per-frame wall
  periods;   trial reports carry AVG / 1% low / 0.1% FPS.
- The progress window shows the ranked video-style table (AVG · 1% ·
  0.1% · p99) that mirrors the manual decision step.
- Journal ownership, exact rollback, recovery, and the Gate B/C/D
  ordering are unchanged.
