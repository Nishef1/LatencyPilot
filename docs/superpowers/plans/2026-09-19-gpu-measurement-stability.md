# GPU Measurement Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the current v1 GPU-affinity experiment more trustworthy before expanding its search space.

**Architecture:** Preserve ADR 0006 as the product authority. First reconcile the automatic workflow with that authority by removing MSI mutation from the v1 sequence. Then reduce benchmark scheduler contamination without changing the candidate search or Keep safety contract. Only after fresh owner-hardware evidence should time-local controls be added. Read-only runtime interrupt-topology evidence remains the prerequisite for any future multi-processor/MSI-X policy work.

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
- **Local-control ruling (2026-09-19):** do not add Original controls every 3–4 candidates yet. The benchmark subject changed materially by removing adaptive CPU-pressure calibration. A fresh physical development run must establish the remaining temporal drift before adding more scored controls and runtime. Existing post-sweep and post-finalist Original controls stay in force.

## Review Focus

- Keep D3D12 timestamp interpretation consistent with current Microsoft DirectX semantics rather than stale localized guidance.
- MSI source may remain available for future/manual use but must not run in automatic v1 sequencing.
- Benchmark changes must not make candidate workload vary by candidate or restart.
- Runtime reductions must not weaken final ETW placement proof or rollback safety.
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
- [x] Reconcile canonical wording: ADR 0006 and `PROJECT_STATUS.md` already state MSI is outside the v1 automatic path, so no duplicate status rewrite was required.
- [x] Preserve `ServiceBoundary.MutationAvailable = false` and exact MSI/xHCI recovery substrate.

### Task 2: Make the benchmark subject GPU-dominant without candidate-dependent work — source complete, physical evidence open

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
- [x] Mark variance/runtime improvement as physically unproven until a fresh owner-machine Gate A development run exists.

### Task 3: Re-measure before adding time-local Original controls — physical evidence gate

**Current decision:** deferred until a fresh hardware run of the stabilized benchmark shows whether meaningful within-sweep temporal drift remains.

- [ ] Run the exact new workload on owner hardware in development mode and retain full artifact/report evidence.
- [ ] Compare Original noise, post-sweep drift, CPU recording time distribution, GPU work distribution and total runtime with the previous noisy run.
- [ ] Only if temporal drift remains material, prototype a bounded block size against the actual runtime budget.
- [ ] Preserve the >15% noise early-stop, finalist cap and existing post-sweep/post-finalist controls.
- [ ] If local controls are justified, change ADR/methodology before treating the new sequence as authority.

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
