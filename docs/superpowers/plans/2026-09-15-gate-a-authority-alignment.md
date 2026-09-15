# Gate A Authority Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the owner-local Gate A tooling enforce the same optimizer-readiness and runtime-placement authority contracts already required by the production source and physical-validation runbook.

**Architecture:** Keep the existing read-only protocol and mutation substrate unchanged. Tighten only the owner validation path: candidate planning must pass `baseline-quality-v2` plus `workload-stability-v1`, and runtime placement must be gated by verified stored candidate state before/after the ETW interval plus direct GPU-driver ISR placement. ConfigMgr allocated-resource data remains recorded provenance rather than a success gate.

**Tech Stack:** C# 14, .NET 10, existing Benchmarking/Core/Platform.Windows types, MSTest 4 / Microsoft.Testing.Platform, GitHub Actions Tests workflow.

**Spec:** `docs/PHASE3_PHYSICAL_VALIDATION.md`

## Global Constraints

- Work directly on `main`; do not create a branch.
- Public protocol remains observation-only v6 and `MutationAvailable=false`.
- No new package, persistence engine, IPC surface, mutation domain or evidence schema version.
- ConfigMgr allocated interrupt resources are provenance, not proof of effective placement.
- Gate A success requires exact stored candidate state around the measurement interval, clean ETW, direct attributable GPU-driver ISR on the requested CPU, and no attributable off-target GPU ISR.
- Candidate planning requires the shared `GpuOptimizationBaselineReadiness` contract.
- Do not run owner-local App/Service build or physical mutation from automation; exact-final-HEAD GitHub Actions Tests is the repository gate.

---

### Task 1: Enforce workload readiness in owner candidate planning

**Files:**
- Modify: `tools/LatencyPilot.PhysicalValidation/BaselineEvidenceCandidatePlan.cs`
- Modify: `tools/LatencyPilot.PhysicalValidation/Program.cs`

**Interfaces:**
- Consumes: evidence-v8 `windows` and `runtimeWindows`, `WorkloadStabilityAnalyzer.Analyze`, `GpuOptimizationBaselineReadiness.IsEligible`.
- Produces: a plan result that exposes the computed workload-stability result and refuses candidate creation when shared optimizer readiness is false.

- [ ] Parse `runtimeWindows` in strict window order and reconstruct `WorkloadWindowEvidence` from each baseline window plus optional `systemCpuBusyPercent`.
- [ ] Analyze the five windows with `WorkloadStabilityAnalyzer` and apply `GpuOptimizationBaselineReadiness.IsEligible(quality, stability)` before processor-pressure planning.
- [ ] Fail closed for missing/misaligned/partial/invalid runtime activity evidence according to the shared analyzer semantics.
- [ ] Print the workload-stability method/status in `plan-gpu-affinity` output so Gate A evidence records the reason candidates were eligible.

### Task 2: Align runtime placement proof with the runbook

**Files:**
- Modify: `tools/LatencyPilot.PhysicalValidation/Program.cs`

**Interfaces:**
- Consumes: journaled original/candidate state, `GpuInterruptAffinityPolicyStore.Capture`, `GpuInterruptAffinityStateComparer.MatchesCandidate`, ConfigMgr interrupt resources, kernel ETW capture, `GpuInterruptRuntimePlacementVerifier.Analyze`.
- Produces: exit 0 only when stored state is verified immediately before and after capture, driver identity remains unchanged, capture integrity is clean, and direct runtime placement confirms the requested CPU.

- [ ] Verify exact candidate policy and driver identity immediately before ETW; fail before measurement when it is not the journaled candidate.
- [ ] Always record ConfigMgr resource evidence when available, but do not stop or pass solely because allocated affinity is unavailable, ambiguous, matching or conflicting.
- [ ] Capture ETW, then re-read exact candidate policy/driver identity and record post-capture verification.
- [ ] Require clean capture plus `ConfirmsRequestedPlacement`; any missing/unresolved/off-target proof remains non-success.
- [ ] Update success/failure wording so it never calls ConfigMgr authoritative.

### Task 3: Reconcile source status and verify exact HEAD

**Files:**
- Modify only if needed: `PROJECT_STATUS.md`
- Modify only if needed: `ROADMAP.md`
- Modify: `docs/superpowers/plans/2026-09-15-remaining-1.0-source-completion.md`

- [ ] Reconcile the handoff so it states that the owner Gate A planner uses the shared workload-readiness gate and that allocated-resource evidence is supplemental to runtime placement proof.
- [ ] Inspect the final diff for unrelated churn, protocol/schema changes, new dependencies and accidental arming.
- [ ] Push the final source/doc commit(s) on `main` and require a successful `Tests` workflow on the exact final HEAD.
- [ ] Do not mark Gate A or product 1.0 physically complete; owner-local apply/revert/recovery evidence remains open.
