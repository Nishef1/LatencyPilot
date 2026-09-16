# GPU Go Mode Source Completion Implementation Plan — Historical

Status: **Superseded on 2026-09-16**

This file is retained as historical execution context only. Do **not** execute it as the current GPU optimization plan.

The canonical GPU path is now:

- design: `docs/superpowers/specs/2026-09-16-gpu-auto-affinity-benchmark-design.md`;
- execution plan: `docs/superpowers/plans/2026-09-16-gpu-auto-affinity-benchmark.md`.

The 2026-09-16 authority replaces the conflicting passive/steady-scene candidate-search assumptions from this older plan with the following explicit split:

```text
steady RealWorld
= existing controlled/manual five-window evidence product

gpu-affinity-benchmark-v1
= automatic deterministic synthetic GPU-affinity candidate-search method

Run GPU Gate A
= development-only owner substrate-validation surface

Auto-optimize GPU
= future product surface, blocked until Gate D
```

In particular, the older four-candidate default, rank-1/passive selection assumptions and use of `workload-stability-v1` as the automatic GPU benchmark readiness gate are superseded. Automatic GPU affinity must actively test the bounded physical-core set, keep CPU0 eligible, refine SMT siblings, retain original Windows affinity as a control, and use the separate GPU-benchmark-specific readiness/evidence contract defined by the 2026-09-16 spec.

The Gate A → B → C → D safety boundary is unchanged. Public protocol remains observation-only v6 and product mutation remains unarmed until the later gates physically authorize it.

Git history preserves the original contents of this plan for auditability.