# ADR 0011 — Restart-canonicalized observer-isolated GPU affinity v6

Status: **Accepted and implemented in source** (2026-09-25)

Supersedes ADR 0010 for new GPU measurement/search/ranking evidence. ADR 0010 remains the historical v5 contract. ADR 0006 remains the broader product/safety authority.

## Context

The owner physical v5 run from source revision `071abc671352ee698c38e23851828f2667ef0e4b` completed safely and demonstrated that the local paired/finalist logic was doing useful work: CPU 15 looked strong in short screening but reversed during repeated finalist confirmation, CPU 4 remained the best observed finalist, a measured interrupt-tail guardrail prevented Keep, and exact Original was restored with zero unresolved journal ownership.

The same run exposed a separate comparison-history defect. The initial scored Original observations were captured before any real display-adapter restart. Their persisted decision baseline was approximately 297.8 FPS 1% low / 367.5 FPS AVG. After the first candidate apply and exact rollback, which both exercise the real GPU restart path and recreate the D3D12 renderer, the adjacent Original control moved to approximately 379.4 FPS 1% low / 430.9 FPS AVG and later Original controls stayed in that higher regime.

Local `OriginalBefore → Candidate → OriginalAfter` pairs protected candidate ranking from treating that first transition as a candidate gain, but the initial Original variability estimate and later paired controls did not share equivalent restart/renderer history.

## Decision

New Full and Selected-CPU paired evidence uses method id `gpu-affinity-benchmark-v6`.

Before any benchmark warm-up or scored Original observation:

1. verify the exact captured Windows/driver Original affinity policy and display-driver version;
2. perform one real in-place display-adapter restart while that policy remains unchanged;
3. require the adapter to return healthy without a system reboot;
4. re-verify the exact Original policy and driver identity;
5. recreate the benchmark D3D12 renderer/device;
6. only then run the existing 5-second non-scored Original warm-up and 3–5 scored Original observations.

This pre-score restart is comparison-state canonicalization, not a candidate mutation. It deliberately uses the same supported restart mechanism already exercised by candidate apply/rollback so the initial Original estimate and later paired Original controls are compared in the same post-restart device/renderer regime.

If the in-place restart cannot be trusted, exact Original cannot be re-verified, or renderer recreation fails, scored candidate search does not begin.

## Unchanged decisions

v6 does **not** change:

- the exact Original policy as the recovery/reference state;
- local geometric-mean pair effects;
- one bounded retry for high local control movement;
- Stage-A physical-core coverage, uncertainty-aware refinement, top-four recheck, or bounded top-two finalist confirmation;
- median paired 1%-low ranking authority;
- High/Medium/Low ranking confidence;
- separate AVG/frame-p99/interrupt-tail Keep guardrails;
- mandatory final target-only ISR placement proof before Keep;
- exact rollback/recovery ownership;
- the report schema `latencypilot-gpu-auto-affinity-report-v3` or evidence envelope `latencypilot-gpu-benchmark-v1`.

## Diagnostic exception

`OriginalDiagnostics` remains intentionally restart-free and mutation-free. It answers only “how variable is the current untouched Original environment?” and must not claim restart comparability or candidate benefit.

## Evidence interpretation

Historical v1–v5 evidence remains historical and is never reinterpreted as v6. The 2026-09-25 v5 run is evidence for this design change, not physical closure of v6.

Graphics-hook observations such as `nvspcap64.dll` remain interference context only; v6 does not automatically close, disable, or blame third-party overlays.

## Consequences

The full search pays one additional display-adapter restart before scoring, but no extra scored benchmark windows. This should remove a large one-time pre-restart/post-restart regime mismatch without weakening local pairing, finalist confirmation, guardrails, rollback, or physical-evidence requirements.
