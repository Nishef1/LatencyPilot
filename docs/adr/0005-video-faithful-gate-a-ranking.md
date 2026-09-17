# ADR 0005 — Video-faithful Gate A ranking from in-app frame periods

Status: **Superseded in ranking/confirmation/product-scope details by ADR 0006** (2026-09-18)

ADR 0005 remains the historical record for why the benchmark's own frame-period signal replaced PresentMon/ETW as a hard screening dependency. The current v1 decision contract is `0006-simple-auto-interrupt-affinity-v1.md`.

## Historical context

The manual per-core GPU affinity workflow this product automates is:

```text
per CPU core: set affinity mask → restart → run 3D benchmark
→ record AVG FPS, 1% low, 0.1% low → pick the best core → keep it
```

The owner-local Gate A run `gpu-auto-affinity-20260917T213830632Z` showed that the controlled D3D12 benchmark could produce healthy frame evidence while standalone PresentMon produced zero usable rows. Requiring every external collector to succeed before any CPU could be ranked made the automation less robust than the manual workflow it was intended to replace.

## Durable decisions retained

- The controlled benchmark's own wall-clock frame periods are sufficient for **screening/ranking** when artifact identity, stored-state verification and workload continuity are intact.
- Standalone PresentMon and kernel ETW are independent cross-checks/guardrails during screening rather than mandatory ranking inputs.
- Exact stored-state verification and journal-owned rollback remain hard safety requirements.
- When ETW is healthy during screening, a proven wrong/off-target ISR placement invalidates that candidate; missing ETW is recorded explicitly rather than silently treated as proof.
- PresentMon remains useful as an external frame-cadence cross-check, but its absence cannot abort the entire CPU search.

## Superseded decisions

ADR 0006 replaces these earlier details:

- p99-centric or ambiguous multi-metric ranking order;
- two scored runs for every physical core;
- active SMT sibling refinement in v1;
- ABBA + BAAB finalist confirmation;
- allowing a kept final winner without fresh ETW runtime placement proof;
- treating USB/xHCI automation as permanently manual/outside the v1 product workflow.

The current v1 flow is intentionally smaller: one scored run per eligible physical core, two additional re-tests for the best up-to-three, ranking by median 1% low → 0.1% low → AVG FPS with p99 as diagnostic/tie context, followed by a hard final ETW ISR-placement verification before Keep.
