# GPU Auto-Affinity Benchmark and One-Click Optimizer Design

Status: **Superseded / historical**  
Original date: 2026-09-16  
Superseded: 2026-09-17

This document is retained only as historical design context. Its original implementation direction required each forced-CPU candidate to compete against the Windows/original affinity as a minimum-improvement winner gate. Physical Gate A evidence and the clarified product goal showed that this is the wrong decision question for Auto GPU Affinity.

The current canonical design is:

- `docs/superpowers/specs/2026-09-17-gpu-auto-affinity-ranking-design.md`
- `docs/BENCHMARK_METHODOLOGY.md`
- `docs/PHASE3_PHYSICAL_VALIDATION.md`

Current semantics in summary:

```text
fixed D3D12 workload
→ original warm-up + reference controls
→ every eligible physical core: apply/restart → 5 s non-scored warm-up → 2 scored runs → rollback
→ exclude invalid/Inconclusive/unstable candidates
→ rank valid candidates by median run-level frame-p99
→ fresh re-screen of best up-to-three candidates
→ SMT sibling refinement
→ ABBA + BAAB balanced confirmation with warm-up after every state transition
→ Keep best valid/repeatable forced-CPU finalist OR exact RestoreOriginal when evidence cannot support a winner
```

The Windows/original state remains the exact recovery/reference state, but is no longer a fixed threshold that every forced-CPU candidate must beat. PresentMon evidence for this path is collected with LatencyPilot's pinned standalone PresentMon 2.5.1 console collector; a separately installed PresentMon Service/API is not a Gate A prerequisite.

For original research rationale, prior assumptions, and historical alternatives, use this file's Git history rather than treating an old revision as current authority.
