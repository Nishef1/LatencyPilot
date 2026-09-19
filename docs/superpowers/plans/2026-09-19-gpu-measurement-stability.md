# GPU Measurement Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the current v1 GPU-affinity experiment more trustworthy before expanding its search space.

**Architecture:** Preserve ADR 0006 as the product authority. The automatic workflow excludes MSI mutation from v1. The benchmark subject is GPU-dominant rather than deliberately CPU-pressure calibrated. Physical evidence from 2026-09-19 then proved substantial temporal drift remained, so screening now uses bounded time-local Original controls every four candidates and aborts the remaining screen on drift. Read-only runtime interrupt-topology evidence remains the prerequisite for any future multi-processor/MSI-X policy work.

**Tech Stack:** C# 14, .NET 10, D3D12/Vortice, MSTest/Microsoft.Testing.Platform, Windows 11 x64.

**Spec:** `docs/adr/0006-simple-auto-interrupt-affinity-v1.md`

## Global Constraints

- Work directly on `main` for owner-directed automation.
- Keep public mutation unarmed.
- Hosted GitHub Actions remains test-only and cannot close physical Gate A.
- Do not add permanent test entrypoints; fold durable high-blast-radius assertions into the existing consolidated audit boundary.
- Preserve exact rollback/recovery behavior.
- Do not add MSI-mode mutation, power-plan mutation, NIC/RSS or audio mutation to the v1 automatic path.
- Do not expand single-CPU affinity search to multi-processor/MSI-X policy search until runtime interrupt topology is observable and physically validated.
- Candidate measurements from a drift-invalidated block are diagnostic evidence only; they cannot be presented as a valid ranked winner.

## Rulings

- **Timestamp-frequency ruling (2026-09-19):** do not change `GpuTimestampCollector` merely to re-query `GetTimestampFrequency` per resolve. The current Microsoft DirectX engineering spec states that timestamp frequencies do not change even when other GPU clocks change, while the current English Learn page dated 2026-08-19 no longer carries the earlier localized dynamic-clock-scaling warning. The previously proposed per-resolve fix therefore lacks current authoritative support and would add work without a demonstrated defect. The RED assertion for that proposed behavior was withdrawn before production code changed.
- **Local-control ruling (updated 2026-09-19):** the stabilized GPU-dominant workload was physically re-run and still showed severe temporal drift: Original 1%-low noise was 56.14%, the post-sweep control drifted 52.44% in 1% low, 11.23% in AVG and 87.14% in frame-p99, and Original was safely restored. This is direct evidence that a full sweep can become non-comparable before its final control. Screening therefore uses a fresh Original warm-up + scored control after each four-candidate block when candidates remain, while retaining the final post-sweep and post-finalist controls.
- **Warm-up ruling (2026-09-19):** do not replace the fixed 5 s transition warm-up with an arbitrary longer sleep yet. The physical run suggests transition behavior may still be non-steady, but the correct stability signal/threshold is not yet proven. The next exact-revision hardware run must observe the new local controls and warm-up behavior before a bounded stability gate is designed.
- **PresentMon ruling (2026-09-19):** `NoSwapChains` is explicit best-effort collector evidence, not permission to synthesize frames and not a hard failure when benchmark-owned controlled frame periods are valid. Final Keep remains dependent on ETW placement proof, not PresentMon availability.

## Review Focus

- Keep D3D12 timestamp interpretation consistent with current Microsoft DirectX semantics rather than stale localized guidance.
- MSI source may remain available for future/manual/recovery use but must not run in automatic v1 sequencing.
- Benchmark changes must not make candidate workload vary by candidate or restart.
- Runtime reductions must not weaken final ETW placement proof or rollback safety.
- Intermediate Original controls must run only after candidate rollback/original-state verification, never while a candidate mutation is still owned.
- Any methodology change that needs physical evidence remains implemented-but-not-closed until owner hardware validation.

---

### Task 1: Reconcile v1 automatic sequencing — source complete

**Files:**
- Modified: `tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs`
- Modified: `src/LatencyPilot.Service/AutomaticOptimizationWorkflow.cs`

**Interfaces:**
- Consumes: ADR 0006 automatic-stage scope.
- Produces: automatic v1 sequence without MSI while retaining the conservative non-v1/manual MSI substrate for recovery and future use.

- [x] Change the existing consolidated audit contract so MSI is absent from `AutomaticOptimizationWorkflow.OrderedStages` and the reboot-pending mutation example is xHCI.
- [x] Run hosted Tests and observe the expected RED against current production source (`35471270990`).
- [x] Re-check the proposed D3D12 timestamp-frequency defect against current primary Microsoft sources; withdraw it when the current engineering spec contradicted the older guidance.
- [x] Remove MSI from the automatic stage array while retaining non-v1 MSI mutation substrate.
- [x] Reconcile canonical wording with ADR 0006.
- [x] Preserve `ServiceBoundary.MutationAvailable = false` and exact MSI/xHCI recovery substrate.

### Task 2: Make the benchmark subject GPU-dominant without candidate-dependent work — source complete, physical effect observed

**Files:**
- Modified: `src/LatencyPilot.GpuBenchmark/BenchmarkWorkload.cs`
- Modified: existing consolidated audit contract only; no new permanent test entrypoint.

**Interfaces:**
- Consumes: frozen worker map/workload identity and current frame telemetry.
- Produces: GPU command-batch calibration with synthetic CPU simulation fixed at the existing minimum instead of intentionally calibrating CPU/scheduler pressure into the scored workload.

- [x] Characterize the current calibration rule: synthetic CPU simulation began at 20,000 iterations and adapted toward a broad 2–12 ms CPU-recording band, up to 4,000,000 iterations.
- [x] Define the smallest candidate-independent change: keep simulation at existing `MinimumSimulationIterations` (1,000), retain adaptive GPU command-batch calibration, and leave worker map/thread priority/seed/process semantics unchanged.
- [x] RED through the existing consolidated contract (`35471559271`) showed the old adaptive CPU calibration was still present.
- [x] GREEN on source commit `38106c2c0199d8b55395636df167fc3537841171` with the existing critical-tests workflow.
- [x] Keep seed, process lifetime, worker map, resolution, restart/recreation and candidate search semantics frozen across candidates.
- [x] Owner-hardware development run showed CPU recording was reduced to roughly sub-millisecond medians, while substantial temporal drift still remained; this separated the former synthetic CPU-pressure confounder from the remaining environment drift problem.

### Task 3: Detect temporal drift before wasting the full screen — source complete, physical re-validation open

**Files:**
- Added: `tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs`
- Modified: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Modified: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinityProgressPlan.cs`
- Modified: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs`
- Modified: `tests/LatencyPilot.CriticalTests/GpuBenchmarkContractTests.cs`
- Modified: `docs/BENCHMARK_METHODOLOGY.md`
- Modified: `PROJECT_STATUS.md`

**Interfaces:**
- Consumes: exact Original repeatability/noise model, candidate rollback/original verification, benchmark-owned frame periods, optional PresentMon evidence.
- Produces: bounded four-candidate screening blocks, time-local Original controls, early drift invalidation, accurate progress budgeting and explicit no-winner UI semantics.

- [x] Physical development run proved remaining temporal drift was material after the GPU-dominant workload change.
- [x] RED run `35473866508` proved the old source still screened all six synthetic candidates and the UI lacked an explicit invalidated-result state.
- [x] Add a fresh Original warm-up + scored control after every four completed screening candidates when more remain.
- [x] Reuse the existing per-metric noise-aware Original drift bands for 1% low, AVG and frame-p99.
- [x] On local-control drift: stop future candidates, never start finalist confirmation, never Keep, verify exact Original, and preserve the invalidating control in the report.
- [x] Make drift-invalidated candidate rows diagnostic-only and show `Measurements invalidated by drift — no valid winner` rather than a false top-ranked result.
- [x] Update progress planning for intermediate block controls and the real five-finalist cap.
- [x] Bind `PresentMon=NoSwapChains` semantics: benchmark-owned frame periods remain valid primary evidence, no fake guardrail samples are created, and the collector status remains explicit.
- [x] GREEN source run `35474222841` after the core drift/UI implementation.
- [ ] Run the exact final documentation/source HEAD through hosted Tests.
- [ ] Run the new block-control workflow on owner hardware and confirm early termination on drift or full-sweep comparability when stable.
- [ ] Inspect transition warm-ups and total runtime; only add stability-based warm-up if physical evidence still demonstrates a transition problem.

### Task 4: Add read-only runtime interrupt topology evidence

**Files:**
- Modify/create only under `LatencyPilot.Platform.Windows/Devices` and `LatencyPilot.Core/Devices` as required by the existing boundaries.
- Modify existing runtime-placement/audit tests if a high-blast-radius contract is needed.
- Update `SYSTEM_DESIGN.md`, methodology/status and an ADR if the evidence model changes architecture.

**Interfaces:**
- Consumes: allocated ConfigMgr interrupt resources and stored interrupt configuration.
- Produces: explicit read-only evidence that separates stored policy, allocated interrupt resources and runtime placement/topology without guessing MSI/MSI-X semantics from registry state alone.

- [ ] Model only facts Windows APIs actually prove.
- [ ] Keep mutation unchanged.
- [ ] Do not add pair/set/spread candidate generation yet.
- [ ] Use this evidence as the prerequisite for any future MSI-X/multi-processor search design.

### Task 5: Final verification and delivery state

- [ ] Review the complete diff from pre-work HEAD `38edfc0c571bf7ff3f176e2eb81e57102d7171cc` against ADR 0006 and AGENTS.md.
- [ ] Run the existing hosted Tests on the exact final `main` HEAD and require success.
- [ ] Confirm public mutation remains disabled.
- [ ] Report exact final HEAD, CI evidence, what remains physically unproven, and the next two stages.
