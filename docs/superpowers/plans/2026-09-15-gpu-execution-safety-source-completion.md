# GPU Execution and Safety Source Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the repository-verifiable GPU execution/safety gap between candidate planning/confirmation interpretation and a physically gated one-click optimizer path, while keeping public mutation unarmed until Gate A physical proof passes.

**Architecture:** Reuse the existing narrow GPU affinity policy store, durable mutation journal, recovery executor, ETW capture, PresentMon capture and `LatencyPilot.Benchmarking.Optimization` decision/confirmation contracts. First make storage writes interruption-safe and classifiable, then add one non-public orchestration layer that drives journal state through measure/decision using synchronized evidence. Public observation protocol remains v6/read-only and `MutationAvailable=false`; no mutation IPC or user-reachable mutation is introduced in this tranche.

**Tech Stack:** C# 14, .NET 10 LTS, WinUI 3/Windows App SDK 2.4, Windows Service, SetupAPI/Configuration Manager, ETW/TraceEvent, PresentMon, SQLite mutation journal, MSTest/Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-09-14-product-1.0-source-completion-design.md`

## Global Constraints

- Work directly on `main`; preserve unrelated work.
- Public protocol stays observation-only v6 in this tranche.
- `ServiceBoundary.MutationAvailable` stays `false`.
- Permanent automated tests stay at exactly 10/10; extend existing test methods rather than adding methods.
- Hosted CI stays test-only; no hosted build/hardware claim is introduced.
- No registry/shell/process generic privileged primitive.
- Unknown/diverged external state, changed target/driver identity, insufficient evidence or failed restart/rollback stays fail-closed.
- Hardware Gate A remains owner-local and is never marked passed from repository-only evidence.

---

### Task 1: Make the two-value GPU affinity storage operation interruption-safe

**Files:**
- Modify: `src/LatencyPilot.Platform.Windows/Devices/GpuInterruptAffinityPolicyStore.cs`
- Modify: `src/LatencyPilot.Service/GpuInterruptAffinityMutationTransaction.cs`
- Modify: `src/LatencyPilot.Service/MutationRecoveryAssessment.cs`
- Modify within an existing method only: `tests/LatencyPilot.CriticalTests/CriticalPathTests.cs`

**Interfaces:**
- Consumes: `GpuInterruptAffinitySnapshot`, `GpuInterruptAffinityCandidate`, `GpuInterruptAffinityStateComparer`, `MutationJournal`.
- Produces: a policy-store apply/restore contract that either verifies the full desired pair or attempts exact compensating restoration before surfacing failure; recovery assessment can distinguish a journal-owned partial apply from unrelated external divergence without weakening refusal of unknown changes.

- [ ] **Step 1: Extend the existing mutation/recovery critical test matrix**

Add scenarios inside the existing GPU mutation/recovery test method that model failure after the first affinity value is changed and require the transaction to retain an unresolved/recovery-owned state rather than misclassify it as a successful pre-write abort or external divergence. Keep the permanent method count unchanged.

- [ ] **Step 2: Verify RED with the test-only command**

Run the repository's existing critical test command. Expected: at least one new assertion fails because current `GpuInterruptAffinityPolicyStore.Apply` writes `DevicePolicy` and `AssignmentSetOverride` separately with no compensating full-pair restoration contract.

- [ ] **Step 3: Implement bounded compensating storage semantics**

Inside `GpuInterruptAffinityPolicyStore`, capture the exact pre-call snapshot immediately before mutation. If any exception occurs after the first attempted write, attempt to restore the exact captured pair/key-existence state, flush, and verify it. If compensation itself fails, throw an aggregate/explicit exception that preserves both the write and compensation failures. Do not overwrite a subsequently changed third-party state blindly: compensation is allowed only when current state matches original, candidate, or the explicitly recognized partial state produced by this call.

- [ ] **Step 4: Make journal ownership explicit for interrupted applies**

Update `GpuInterruptAffinityMutationTransaction`/recovery assessment so a write-attempt failure after `Applying` always remains journal-owned recovery work unless exact original restoration was verified. Preserve `AbortedBeforeApply` only for paths that provably performed no write.

- [ ] **Step 5: Verify GREEN**

Run the existing test-only suite; require 10 passed, 0 failed, 0 skipped.

- [ ] **Step 6: Commit**

Commit message: `fix(mutation): harden GPU affinity interrupted writes`

---

### Task 2: Resolve allocated IRQ descriptor parsing ambiguity in source

**Files:**
- Modify: `src/LatencyPilot.Platform.Windows/Interop/ConfigurationManager.cs`
- Modify the reader that consumes `CM_Get_Res_Des_Data` for IRQ resources under `src/LatencyPilot.Platform.Windows/Devices/`
- Modify within an existing method only: `tests/LatencyPilot.CriticalTests/CriticalPathTests.cs`
- Modify: `docs/PHASE3_PHYSICAL_VALIDATION.md`

**Interfaces:**
- Consumes: raw `CM_Get_Res_Des_Data` bytes and documented ConfigMgr IRQ descriptor layouts.
- Produces: explicit parsed descriptor version/size/flags/group/affinity/IRQ fields with validation that refuses impossible/truncated layouts instead of interpreting arbitrary bytes as effective placement.

- [ ] **Step 1: Add descriptor-layout assertions to an existing critical test method**

Cover the exact byte offsets and expected minimum size for the native IRQ descriptor representation used by the reader, plus rejection of truncated/unknown payloads. Do not add a new permanent test method.

- [ ] **Step 2: Verify RED**

Expected: current direct `StructLayout` interpretation does not satisfy the explicit byte-layout contract or cannot reject the ambiguous payload shape deterministically.

- [ ] **Step 3: Replace implicit struct-cast assumptions with explicit parsing**

Parse the returned buffer with `BinaryPrimitives`/documented offsets after validating descriptor size/type. Preserve raw flags and expose unknown/unsupported descriptor semantics as unavailable rather than coercing them into group/affinity values. Keep allocated-resource evidence separate from stored affinity policy and ETW runtime evidence.

- [ ] **Step 4: Update the physical runbook**

Document what source ambiguity is now eliminated and what still requires owner-local validation on the RTX 3070/current topology. Do not claim that any particular returned affinity proves runtime placement until the physical check passes.

- [ ] **Step 5: Verify GREEN**

Run test-only suite; require 10/10.

- [ ] **Step 6: Commit**

Commit message: `fix(windows): parse allocated IRQ descriptors defensively`

---

### Task 3: Add a non-public GPU experiment evidence collector

**Files:**
- Create: `src/LatencyPilot.Service/GpuOptimizationEvidenceCollector.cs`
- Modify: `src/LatencyPilot.Service/LatencyPilot.Service.csproj` only if an existing project reference is required.
- Reuse existing ETW/PresentMon readers from `LatencyPilot.Platform.Windows` and models from `LatencyPilot.Core`/`Benchmarking`.
- Modify within an existing method only: `tests/LatencyPilot.CriticalTests/CriticalPathTests.cs` for pure mapping/provenance behavior only.

**Interfaces:**
- Consumes: exact GPU target identity, workload identity, requested duration, expected Original/Candidate state, source revision/session/environment identity.
- Produces: `GpuOptimizationConfirmationRun`-compatible evidence with unique capture ID, actual duration, sample distributions, target-state verification, capture integrity and required named guardrails.

- [ ] **Step 1: Define a focused collector result/provenance contract**

The collector must record one common requested interval, exact start/end timestamps, ETW integrity, expected-state verification, PresentMon process/API/swapchain identity and raw enough sample distributions to satisfy the existing >=1,000-samples-per-run confirmation contract where the source actually exposes samples. Missing required evidence returns an explicit unavailable/inconclusive result; it never fabricates samples from aggregates.

- [ ] **Step 2: Extend an existing test method with pure collector-mapping scenarios**

Verify that mismatched workload/adapter identity, incomplete PresentMon data, short duration, duplicate capture IDs or absent target-state verification cannot become a valid confirmation run. Keep hardware capture itself owner-local.

- [ ] **Step 3: Implement collector composition**

Compose existing ETW capture and PresentMon APIs; do not create a second capture stack. Use a shared cancellation/deadline and record actual interval overlap. Only emit a confirmation run when the required evidence shares the same workload/session/source/environment identity.

- [ ] **Step 4: Verify test-only suite**

Require 10/10.

- [ ] **Step 5: Commit**

Commit message: `feat(optimizer): collect synchronized GPU experiment evidence`

---

### Task 4: Add the internal GPU optimization orchestrator

**Files:**
- Create: `src/LatencyPilot.Service/GpuOptimizationOrchestrator.cs`
- Modify: `src/LatencyPilot.Service/GpuInterruptAffinityMutationTransaction.cs` to expose only the narrow internal journal-state transitions needed by orchestration.
- Modify: `src/LatencyPilot.Benchmarking/Optimization/*` only if a missing pure contract is discovered.
- Modify within existing critical test methods only.

**Interfaces:**
- Consumes: authoritative Real-world baseline, bounded `GpuAffinityCandidate` list, `GpuInterruptAffinityMutationTransaction`, `GpuOptimizationEvidenceCollector`, `GpuOptimizationDecisionEngine`, `GpuOptimizationConfirmation`.
- Produces: internal screening/confirmation result that always ends in either verified exact-original restoration or a journal state that remains unresolved/recovery-required; no public IPC/UI exposure.

- [ ] **Step 1: Extend existing optimizer test matrix with orchestration-state scenarios**

Cover: no eligible finalist → original retained; screening finalist → exactly one finalist proceeds; dirty/inconclusive measurement → restore; confirmed improvement → recommendation may be Keep but journal is not terminalized as kept until active candidate state verification succeeds; any failure → rollback/recovery path.

- [ ] **Step 2: Verify RED**

Expected: orchestration type/calls do not exist.

- [ ] **Step 3: Implement screening loop**

For each bounded candidate: prepare → apply/activate → verify actual candidate state → collect evidence → restore exact original before trying the next candidate. Feed only complete compatible evidence into screening. Preserve each candidate result and failure reason.

- [ ] **Step 4: Implement balanced finalist confirmation**

Execute the existing fixed ABBA+BAAB schedule. Before every Original/Candidate run, verify the expected active stored state after any required apply/rollback/restart. Collect one run per schedule element with unique capture identity. Pass exactly eight runs to `GpuOptimizationConfirmation`.

- [ ] **Step 5: Implement final decision safety**

`RestoreOriginal` always verifies exact original active state. `KeepCandidate` may leave the candidate active only if the final expected-state verification succeeds and the journal transition is explicit; otherwise restore/recovery. Do not expose this through protocol v6.

- [ ] **Step 6: Verify GREEN**

Run test-only suite; require 10/10.

- [ ] **Step 7: Commit**

Commit message: `feat(optimizer): orchestrate bounded GPU experiments`

---

### Task 5: Reconcile status and Gate A handoff

**Files:**
- Modify: `PROJECT_STATUS.md`
- Modify: `ROADMAP.md`
- Modify: `SYSTEM_DESIGN.md`
- Modify: `docs/BENCHMARK_METHODOLOGY.md` only if collection semantics changed.
- Modify: `docs/PHASE3_PHYSICAL_VALIDATION.md`
- Modify: this plan.

**Interfaces:** live authority docs and owner-local handoff.

- [ ] **Step 1: Mark only repository-proven items complete**

Record interruption-safe storage source, defensive allocated-IRQ parsing, synchronized collector source and internal orchestration source only after their verification evidence exists. Keep Gate A physical apply/restart/runtime placement/rollback evidence open.

- [ ] **Step 2: Record exact commits and CI runs**

Use the exact final source SHAs and test-only run IDs. Keep test count 10/10.

- [ ] **Step 3: Produce the exact next owner-local Gate A sequence**

Start with current clean `main`, normal `run.ps1`, journal zero-unresolved check, read-only allocated-resource inspection, then only the bounded apply/verify/rollback/recovery sequence. Preserve stop conditions on unknown/diverged state.

- [ ] **Step 4: Final exact-HEAD CI verification**

Require completed success for the normal test-only workflow on the exact final commit before calling this source tranche complete.

- [ ] **Step 5: Commit**

Commit message: `docs(optimizer): close GPU execution source tranche`

---

## Follow-on 0→100 sequence after this tranche

1. Execute owner-local Gate A evidence; if passed, Gate B typed mutation IPC and mutation-specific authorization.
2. Gate C physical App/client → Service mutation-boundary proof.
3. Gate D arm the supported GPU one-click workflow and expose the premium user-facing execution/result UX.
4. Complete USB/xHCI authoritative route evidence and reversible experiment path using the same journal/orchestrator boundary.
5. Complete NIC/RSS authoritative read-only evidence and reversible RSS experiment path.
6. Add transparent workload profiles/cross-subsystem Pareto decision policy and whole-system Restore Baseline.
7. Close remaining Phase 2 physical measurement/accessibility/integrity obligations.
8. Release hardening: upgrade/uninstall recovery, signing/checksums, installer/package validation and diagnostics/export.
9. Final 1.0 audit against every ROADMAP exit gate; no source-only or hosted-CI evidence may close hardware/signing/UI gates.
