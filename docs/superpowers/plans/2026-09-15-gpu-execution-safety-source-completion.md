# GPU Execution and Safety Source Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Status:** Repository/source tranche complete; physical Gate A remains open.

**Goal:** Close the repository-verifiable GPU execution/safety gap between candidate planning/confirmation interpretation and a physically gated one-click optimizer path, while keeping public mutation unarmed until Gate A physical proof passes.

**Architecture:** Reuse the narrow GPU affinity store, durable journal/recovery path, ETW, PresentMon and Benchmarking decision contracts. Public observation protocol remains v6/read-only and `MutationAvailable=false`.

**Spec:** `docs/superpowers/specs/2026-09-14-product-1.0-source-completion-design.md`

## Current test constraint

The permanent suite remains 10. Every test source file must remain <=1200 lines. The owner authorizes growth up to 20 only when genuinely necessary to preserve that file-size boundary or a materially safer durable split; temporary/obsolete tests must be removed rather than accumulated.

---

### Task 1: Make GPU affinity storage interruption-safe

- [x] RED scenario covered the partial two-value write/compensation ownership risk inside existing critical tests.
- [x] GPU affinity write/restore became a bounded logical operation with exact compensation when possible.
- [x] journal/recovery semantics retain ownership when exact original restoration cannot be proven.
- [x] verified by the normal test-only workflow.

Key source result: a failure after mutation begins can no longer be mislabeled as a harmless pre-write abort merely because the high-level call failed.

---

### Task 2: Parse allocated IRQ descriptors defensively

- [x] explicit descriptor-layout validation added inside the existing critical test matrix.
- [x] implicit blind reinterpretation replaced by defensive parsing.
- [x] truncated/wrong-type/zero-target descriptors fail closed.
- [x] raw allocated-resource evidence remains distinct from stored policy and runtime ISR evidence.
- [x] the physical runbook retains the owner-local RTX allocated-resource ambiguity instead of manufacturing a placement claim.

---

### Task 3: Add synchronized non-public GPU evidence collection

- [x] `GpuOptimizationEvidenceCollector` created.
- [x] exact session/workload/environment/source/capture identity retained.
- [x] ETW and raw PresentMon capture run concurrently under one deadline.
- [x] >=95% common requested interval required.
- [x] raw DPC duration primary samples and complete PresentMon raw-frame guardrails retained.
- [x] short/dirty/mismatched/incomplete evidence fails closed.
- [x] candidate evidence additionally requires effective GPU ISR placement: attributable GPU ISR on target CPU and zero attributable GPU ISR off target; unresolved attribution is not treated as success.

The runtime-placement contract was introduced with a deliberate RED at commit `44d678e4cb8b8269a30b3794cf26ea618a1e92a8` / run #666 and completed at `bf71682fd63c74fff02ee161db438d7663e4ec06` / run #667.

---

### Task 4: Add the internal GPU optimization orchestrator

- [x] bounded candidate screening implemented.
- [x] every screening candidate is prepare/apply/measure/exact-rollback owned before the next candidate.
- [x] screening can nominate only one finalist.
- [x] fixed ABBA+BAAB eight-run confirmation implemented.
- [x] consecutive Candidate blocks reuse the active candidate; returns to Original exact-rollback first.
- [x] final Candidate reaches `AwaitingDecision` only after measurement.
- [x] Keep terminalization re-reads actual candidate stored state and driver identity.
- [x] non-Keep outcome restores exact original state.
- [x] failure after apply, including `BeginMeasurement` failure, remains inside rollback ownership.
- [x] no public mutation command/UI exposure added.

TDD evidence:

- `a9d6afc0852f6c820c6b73d6e5510cb4e87feafe` / run #660 — RED: execution backend/orchestrator absent.
- `5496ad5ed3728595d83bafc0d4dff1a804b72f38` / run #661 — GREEN: bounded screening.
- `c95b8cb35e947460741637659423c9f1fd62814e` / run #662 — RED: finalist reached deliberate confirmation stop.
- `9a0a937017744baabc0c38b8a73679f59993d558` / run #663 — GREEN: balanced confirmation/Keep path.
- `5cd950780e0dd8a4c8d29972bfea544daaa6e179` / run #664 — RED: real rollback leak after `BeginMeasurement` failure (`expected 1`, `actual 0`).
- `b35c225b8b5f5f8d3d5cfa5fc57d5eed8e37138f` / run #665 — GREEN: rollback ownership fixed.
- `44d678e4cb8b8269a30b3794cf26ea618a1e92a8` / run #666 — RED: runtime-placement evidence contract absent.
- `bf71682fd63c74fff02ee161db438d7663e4ec06` / run #667 — GREEN: effective runtime ISR-placement evidence integrated.

---

### Task 5: Reconcile status and Gate A handoff

- [x] `AGENTS.md` updated with the owner’s current test/file-size policy.
- [x] `PROJECT_STATUS.md` reconciled to actual GPU execution source and exact run #667 evidence.
- [x] `ROADMAP.md` marks repository-proven GPU execution items complete while leaving physical gates open.
- [x] `SYSTEM_DESIGN.md` documents the current internal orchestrator, synchronized evidence and runtime-placement boundary.
- [x] `docs/BENCHMARK_METHODOLOGY.md` advanced to the synchronized raw-frame/runtime-placement contract.
- [x] `docs/PHASE3_PHYSICAL_VALIDATION.md` requires effective same-interval runtime placement evidence without coercing ambiguous allocated-resource data.
- [x] exact current owner-local Gate A sequence remains explicit.

Repository/source tranche proof: exact GPU execution HEAD `bf71682fd63c74fff02ee161db438d7663e4ec06` completed the normal hosted Tests workflow successfully in run **#667 / `34939960176`**.

Documentation reconciliation continues on later commits, but those docs-only commits do not change the source proof above.

## Physical work deliberately not marked complete

- [ ] current exact-main App/Service owner-local build/install/launch;
- [ ] unresolved journal survives/reclassifies correctly across real Service restart;
- [ ] exact-target restart/reboot-required behavior on supported physical GPU;
- [ ] physical candidate apply → stored verification → clean same-interval target ISR placement → exact rollback;
- [ ] controlled forced-failure/recovery exercise;
- [ ] final exact original + zero unresolved;
- [ ] Gate B typed mutation IPC;
- [ ] Gate C physical IPC mutation boundary proof;
- [ ] Gate D user-facing arming.

## Follow-on source sequence

The active follow-on plan is `docs/superpowers/plans/2026-09-15-remaining-1.0-source-completion.md`:

1. authoritative USB hub/port/xHCI + input timing source;
2. USB attribution/readiness;
3. StandardCimv2 NIC/RSS source and local-network benchmark/readiness;
4. workload profiles, Pareto policy and global Restore Baseline planning;
5. release/upgrade/uninstall diagnostics hardening;
6. final source reconciliation and exact-final-HEAD test-only CI;
7. owner-local physical/package/signing closure before true 1.0.
