# GPU Measurement Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the current v1 GPU-affinity experiment more trustworthy before expanding its search space.

**Architecture:** Preserve ADR 0006 as the product authority. First reconcile the automatic workflow with that authority by removing MSI mutation from the v1 sequence. Then reduce benchmark scheduler contamination without changing the candidate search or Keep safety contract, add bounded local controls only where evidence justifies them, and add read-only runtime interrupt-topology evidence before any future multi-processor/MSI-X policy work.

**Tech Stack:** C# 14, .NET 10, D3D12/Vortice, MSTest/Microsoft.Testing.Platform, Windows 11 x64.

**Spec:** `docs/adr/0006-simple-auto-interrupt-affinity-v1.md`

## Global Constraints

- Work directly on `main` for owner-directed automation.
- Keep public mutation unarmed.
- Hosted GitHub Actions remains test-only and cannot close physical Gate A.
- Do not add permanent test entrypoints; fold durable high-blast-radius assertions into existing consolidated audit contracts.
- Preserve exact rollback/recovery behavior.
- Do not add MSI-mode mutation, power-plan mutation, NIC/RSS or audio mutation to the v1 automatic path.
- Do not expand single-CPU affinity search to multi-processor/MSI-X policy search until runtime interrupt topology is observable and physically validated.

## Rulings

- **Timestamp-frequency ruling (2026-09-19):** do not change `GpuTimestampCollector` merely to re-query `GetTimestampFrequency` per resolve. The current Microsoft DirectX engineering spec states that timestamp frequencies do not change even when other GPU clocks change, while the current English Learn page dated 2026-08-19 no longer carries the earlier localized dynamic-clock-scaling warning. The previously proposed per-resolve fix therefore lacks current authoritative support and would add work without a demonstrated defect. The RED assertion for that proposed behavior was withdrawn before production code changed.

## Review Focus

- Keep D3D12 timestamp interpretation consistent with current Microsoft DirectX semantics rather than stale localized guidance.
- MSI source may remain available for future/manual use but must not run in automatic v1 sequencing.
- Benchmark changes must not make candidate workload vary by candidate or restart.
- Runtime reductions must not weaken final ETW placement proof or rollback safety.
- Any methodology change that needs physical evidence remains implemented-but-not-closed until owner hardware validation.

---

### Task 1: Reconcile v1 automatic sequencing

**Files:**
- Modify: `tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs`
- Modify: `src/LatencyPilot.Service/AutomaticOptimizationWorkflow.cs`
- Modify: `PROJECT_STATUS.md`

**Interfaces:**
- Consumes: ADR 0006 automatic-stage scope.
- Produces: automatic v1 sequence without MSI while retaining the conservative non-v1/manual MSI substrate for recovery and future use.

- [x] Change the existing consolidated audit contract so MSI is absent from `AutomaticOptimizationWorkflow.OrderedStages` and the reboot-pending mutation example is xHCI.
- [x] Run hosted Tests and observe the expected RED against current production source.
- [x] Re-check the proposed D3D12 timestamp-frequency defect against current primary Microsoft sources; withdraw it when the current engineering spec contradicted the older guidance.
- [x] Remove MSI from the automatic stage array while retaining non-v1 MSI mutation substrate.
- [ ] Update canonical status wording.
- [ ] Run hosted Tests to GREEN on the reconciled source/status HEAD.

### Task 2: Make the benchmark subject GPU-dominant without candidate-dependent work

**Files:**
- Modify: `src/LatencyPilot.GpuBenchmark/BenchmarkWorkload.cs`
- Possibly modify: `src/LatencyPilot.GpuBenchmark/CpuRenderWorker.cs`
- Modify: existing benchmark audit contract only if a durable invariant needs protection.
- Modify: `docs/BENCHMARK_METHODOLOGY.md`
- Modify: `PROJECT_STATUS.md`

**Interfaces:**
- Consumes: frozen worker map/workload identity and current frame telemetry.
- Produces: a workload whose CPU recording/simulation does not intentionally target multi-millisecond all-core pressure that can dominate frame tails.

- [ ] Characterize the current calibration rule and worker barrier with existing source/evidence.
- [ ] Define the smallest candidate-independent calibration change that biases the subject toward GPU work instead of scheduler pressure.
- [ ] RED→GREEN through an existing benchmark contract if the invariant is durable; otherwise use a temporary investigative assertion and remove it before final commit.
- [ ] Keep seed, process lifetime, worker map and restart/recreation semantics frozen across candidates.
- [ ] Mark physical variance improvement as unproven until owner Gate A evidence exists.

### Task 3: Add bounded time-local Original controls

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Modify: existing `GpuAutoAffinitySessionTests.cs` audit case(s), without adding a new permanent test entrypoint.
- Modify: ADR/methodology only if the measurement sequence changes.

**Interfaces:**
- Consumes: current full sweep, post-sweep control and finalist control model.
- Produces: bounded local drift evidence that does not recreate the previous runtime explosion.

- [ ] Prototype a bounded block size against the current runtime budget; do not hard-code a research suggestion without checking the actual session algorithm.
- [ ] Add local controls only if they materially improve temporal comparability without causing excessive extra restart/scored runs.
- [ ] Preserve the >15% noise early-stop and finalist cap.
- [ ] Verify deterministic sequencing and rollback.

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

### Task 5: Final verification and status reconciliation

- [ ] Review the complete diff against ADR 0006 and AGENTS.md.
- [ ] Run the existing hosted Tests on the exact final `main` HEAD and require success.
- [ ] Confirm public mutation remains disabled.
- [ ] Update `PROJECT_STATUS.md` execution ladder so physical Gate A is the next authority for benchmark-variance claims.
- [ ] Report exact commit(s), CI evidence, what remains physically unproven, and the next two stages.
