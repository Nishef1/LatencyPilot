# Phase 3 Physical Validation and Arming Design

Status: Approved direction for implementation
Date: 2026-09-14

## Goal

Bridge the gap between LatencyPilot's existing unarmed GPU interrupt-affinity mutation source and a safely armable one-click optimizer without weakening the current read-only product boundary.

The immediate outcome is **not** public mutation. It is a repeatable owner-only way to prove the internal mutation/recovery substrate on physical Windows 11 hardware, followed by a later mutation-specific IPC gate and only then user-facing arming.

## Why this change is required

Current source already contains the difficult low-level pieces: exact GPU affinity snapshot/apply/restore, SQLite journaling, restart/reboot-required detection, startup recovery classification, candidate planning and runtime ISR-placement evidence.

Two gaps prevent the documented Phase 3 sequence from being executable:

1. the repository asks for physical apply/restart/rollback validation before public mutation IPC exists, but there is no owner-only entrypoint that can invoke the internal transaction safely;
2. startup recovery classifies unresolved experiments but does not yet execute a selected safe recovery action.

There is also a documentation-order contradiction. ADR 0004 correctly requires mutation-specific typed authorization before mutation ships, while some live sequencing text reads as though IPC can be designed only after every physical mutation check. The correct interpretation is a staged gate: validate the internal substrate first, then build the IPC boundary, then validate end-to-end through that boundary, then arm the product UI.

## Decisions

### 1. Keep the current architecture

Do not replace WinUI 3, the Windows Service, Named Pipes, SQLite, SetupAPI/ConfigMgr, ETW or the existing GPU affinity transaction.

Microsoft's documented model remains the basis:

- `Interrupt Management\Affinity Policy` with `DevicePolicy` and `AssignmentSetOverride` is the supported affinity configuration surface used by the current implementation;
- `DIF_PROPERTYCHANGE`/`DICS_PROPCHANGE` is a documented device property/state-change path;
- restart-required flags and devnode state are evidence that an in-place activation cannot simply be assumed.

A successful registry write is therefore never treated as proof of effective runtime placement.

### 2. Introduce a permanent owner-only physical-validation harness

Create `tools/LatencyPilot.PhysicalValidation` as a narrow console project used only by the repository owner/developer during Phase 3 physical validation.

It is **not** a product surface:

- it is not referenced by `LatencyPilot.App`;
- it is not installed or packaged by the release/installer path;
- it is not reachable over Named Pipes;
- it is not launched by `run.ps1`;
- it exposes no arbitrary PowerShell, process execution, registry path/value, DLL/plugin or generic SYSTEM primitive;
- it remains absent from the normal hosted test contract except for temporary compile/smoke checks that are removed afterward.

The harness may access the Service's internal Phase 3 types through a narrowly scoped friend-assembly relationship. This is preferable to making mutation internals public or adding temporary product IPC solely for hardware testing.

### 3. Harness commands are allowlisted and phase-specific

Initial commands:

```text
inspect
list-gpus
prepare-gpu-affinity --device <exact-instance-id> --processor <group-0-logical-cpu>
apply --experiment <guid> --confirm-physical-mutation
rollback --experiment <guid> --confirm-physical-mutation
recover --experiment <guid> --confirm-physical-mutation
```

Rules:

- mutation commands require an elevated interactive owner terminal;
- mutation commands require the explicit `--confirm-physical-mutation` acknowledgement;
- target validation still occurs inside the existing transaction immediately before any write;
- `prepare` journals an exact original snapshot but does not write candidate state;
- only one unresolved experiment may exist;
- the harness prints the experiment ID, journal state, target, stored-state relation, restart/reboot-required result and recovery disposition;
- unknown/diverged/driver-changed state is never overwritten automatically;
- the tool never accepts a raw registry key/value or arbitrary device class.

`list-gpus` and `inspect` are read-only and may run without the mutation acknowledgement.

### 4. Make recovery executable, but rollback-biased

Add a Service-internal recovery executor that always re-reads actual machine state at execution time and re-derives the recovery plan before doing anything.

Supported actions:

- `None`: no write;
- `AbortPreparedWithoutApply`: terminalize a still-original `Prepared` entry without touching the device;
- `FinalizeVerifiedRollback`: when storage is already original, verify/refresh the exact target as needed before reaching a verified rollback terminal state;
- `RestoreOriginalState`: use the existing exact snapshot/restart/verification rollback path;
- `ManualInterventionRequired`: refuse automatic action.

Recovery never resumes forward in journal v1.

### 5. Correct pre-write-abort semantics

If the transaction has durably entered `Applying` but then proves immediately before the owned registry write that state/topology/driver assumptions changed, it must record a terminal **aborted-before-owned-write** result rather than leaving a known no-write case described as a recovery-required mutation.

The journal state machine may therefore allow the narrowly used transition:

```text
Applying -> AbortedBeforeApply
```

Only the Service's pre-write-abort path uses it. If that terminal transition itself fails, the unresolved `Applying` entry remains fail-closed and startup recovery still re-reads actual state.

This does not change the conservative rule for a crash in the ambiguous window: an unresolved `Applying` entry is never assumed to have written or not written; recovery classifies actual state first.

### 6. Separate four arming gates

#### Gate A — Internal substrate physical gate

Public protocol remains v6/read-only. On the owner's physical Windows 11 target prove:

1. current-main App + Service build/install/launch;
2. journal startup readiness;
3. controlled unresolved entry survives Service restart and is classified correctly;
4. exact-target restart/reboot-required behavior;
5. one candidate apply -> stored verification -> restart -> runtime ISR placement observation -> exact rollback;
6. forced failure/recovery;
7. final exact-original state and no unresolved journal.

#### Gate B — Mutation IPC implementation gate

Only after Gate A passes, design and implement protocol-v7 typed mutation commands and mutation-specific authorization. The command model stays domain-specific, not registry-generic.

#### Gate C — End-to-end IPC physical gate

Repeat the safety path through the actual non-elevated App/client -> protected Service boundary. Prove authorization, cancellation, journaling, restart/recovery and rollback through the public mutation protocol.

#### Gate D — Product arming gate

Only after Gate C passes may `MutationAvailable` become true for supported hardware and the WinUI one-click GPU experiment become user reachable.

### 7. Candidate planning remains outside the privileged mutation mechanism

`Benchmarking`/App owns evidence interpretation and candidate ranking. The privileged Service independently validates any requested candidate against current topology and exact target state before applying it.

Do not move ranking heuristics into the privileged writer and do not trust a stale App candidate merely because it was valid earlier.

### 8. No permanent-test inflation

Permanent automated tests stay at 9/10 unless a genuinely higher-blast-radius invariant requires replacing/merging an existing test or using the final slot.

Use temporary tests/compile smokes for this tranche, remove them before the final source state, and keep physical hardware validation separate from the automated-test count.

## Components

### `LatencyPilot.Service`

Add a reusable actual-state assessment path and recovery executor around the existing journal/planner/transaction. Keep all machine writes in the existing narrow GPU-affinity mutation domain.

### `LatencyPilot.Persistence`

Only the state-machine semantic correction is expected: `Applying -> AbortedBeforeApply` for a proven pre-write abort. No schema-v2 migration is required for this tranche.

### `tools/LatencyPilot.PhysicalValidation`

Owner-only executable for inspection and explicit physical mutation validation. It references the Service but is not a shipping dependency.

### Documentation

Add a Phase 3 physical-validation runbook and reconcile `ROADMAP.md`, `SYSTEM_DESIGN.md`, `OPTIMIZER_TARGET_GRAPH.md` and `PROJECT_STATUS.md` around the four-gate model.

## Error and safety behavior

- non-Windows or unsupported topology: fail before journal/apply;
- non-elevated mutation command: fail before transaction invocation;
- missing explicit confirmation: fail before transaction invocation;
- target not a present display adapter: fail before write;
- driver/topology/stored-state drift: abort or require recovery, never overwrite blindly;
- restart/reboot required: keep experiment unresolved until activation/final state can be proven;
- unknown/diverged state: manual intervention required;
- recovery failure: retain unresolved state;
- tool crash: durable journal remains source of truth and Service startup inspection catches unresolved work.

## Physical validation record

The runbook must record exact source revision, hosted Tests run, owner-local build result, Windows build, GPU/driver, target instance ID, candidate CPU, original snapshot summary, journal experiment ID/state transitions, restart result, runtime ISR placement evidence, rollback state, Service restart/reboot observations and final unresolved-journal count.

A physical validation claim is invalid without exact clean source provenance.

## Explicit non-goals for this tranche

- no protocol v7 yet;
- no user-facing Optimize GPU button yet;
- no MSI/MSI-X mutation;
- no USB/xHCI or NIC mutation;
- no generic recovery shell;
- no auto-resume-forward after crash;
- no release/installer inclusion of the validation harness;
- no claim that a specific CPU affinity improves latency before measured comparison.

## Completion condition for this tranche

This source tranche is complete when:

- the owner-only harness and recovery executor exist and compile;
- pre-write-abort semantics are corrected;
- normal public protocol remains v6/read-only and `MutationAvailable=false`;
- the permanent suite remains within the 10-test cap;
- temporary tests/smokes are removed;
- docs consistently describe Gate A -> Gate B -> Gate C -> Gate D;
- `PROJECT_STATUS.md` points next to owner-local Gate A physical validation.

The Phase 3 product itself remains **implemented but not closed** until the later physical/IPC/one-click experiment gates pass.
