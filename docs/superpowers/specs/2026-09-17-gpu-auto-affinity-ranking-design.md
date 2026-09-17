# GPU Auto-Affinity Ranked Search Design — Historical

Status: **Superseded by ADR 0006 (`docs/adr/0006-simple-auto-interrupt-affinity-v1.md`)**  
Original date: 2026-09-17  
Superseded: 2026-09-18

This specification records the intermediate ranked-search design that corrected the earlier “every candidate must measurably beat Windows default” mistake. It is no longer the canonical v1 behavior.

## Durable lessons retained

- Windows/original GPU affinity is exact rollback/reference state, not a fixed minimum-improvement winner gate.
- Candidate generation must use real Windows processor topology; CPU0 is eligible and even/odd numbering is not a portable rule.
- Every GPU affinity transition gets a non-scored stabilization/warm-up before scored evidence.
- One frozen D3D12 workload/worker map/seed must be preserved across candidate measurements.
- PresentMon is reused rather than replaced, but its standalone console is an independent cross-check rather than a separately installed Service prerequisite.
- Existing ETW, mutation journal, exact-state comparison, GPU restart and recovery infrastructure are reused rather than replaced.
- No liblava/Vulkan/OCAT/second benchmark stack is required for v1.

## Superseded search details

The 2026-09-17 design used two scored runs for every physical core, active SMT sibling refinement, p99-centric ranking and balanced ABBA/BAAB finalist confirmation. ADR 0006 deliberately simplifies that flow.

Current v1 behavior is:

```text
5 s original non-scored warm-up/reference
→ every eligible physical core:
     apply/restart/verify
     5 s non-scored warm-up
     1 scored screen
     exact rollback
→ rank by median 1% low → 0.1% low → AVG FPS, p99 as diagnostic/tie context
→ best up to three:
     fresh apply/restart/warm-up
     2 additional scored runs
     exact rollback
→ reject materially unstable repeated 1% lows
→ apply ranked winner once
→ final ETW target-only GPU ISR placement verification
→ Keep only if final placement is proved; otherwise exact RestoreOriginal
```

Screening can continue when PresentMon or ETW is unavailable if the controlled benchmark artifact and stored-state continuity are valid. Healthy ETW proving wrong/off-target placement invalidates the candidate. Final Keep is stricter: missing runtime ISR-placement proof is not accepted.

USB/xHCI automatic selection/mutation is also part of the v1 product direction after the shared GPU mutation/recovery substrate passes physical Gate A; it is no longer permanently manual/deferred.

For current requirements use ADR 0006, `ROADMAP.md`, `PROJECT_STATUS.md`, `SYSTEM_DESIGN.md`, `docs/BENCHMARK_METHODOLOGY.md`, and `docs/PHASE3_PHYSICAL_VALIDATION.md`.
