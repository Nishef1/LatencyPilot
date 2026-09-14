# Phase 3 Physical Validation and Arming Design

Status: **Source tranche implemented; owner-local Gate A physical validation remains open**  
Date: 2026-09-14

## Goal

Bridge LatencyPilot's unarmed GPU interrupt-affinity mutation substrate to a safely armable one-click optimizer without weakening the read-only product boundary.

The immediate outcome is **not** public mutation. The repository provides a repeatable owner-only way to prove journaling, restart/recovery, exact-target resource activation and runtime observation on physical Windows 11 hardware. Only after that internal proof may mutation-specific IPC be added and physically validated before product arming.

## Architecture decision

Keep the current architecture:

- non-elevated WinUI 3 App;
- narrow Windows Service privileged boundary;
- protocol-v6 read-only Named Pipe surface during Gate A;
- SQLite durable mutation journal;
- SetupAPI/Configuration Manager for exact-target device/resource evidence;
- ETW for runtime DPC/ISR observation;
- existing narrow GPU-affinity transaction/recovery implementation.

Do not add a generic registry writer, shell host, process launcher, plugin/DLL execution surface or arbitrary SYSTEM primitive.

The evidence hierarchy remains strict:

```text
stored interrupt-affinity policy
!= allocated interrupt resources for the exact devnode
!= runtime DPC/ISR behavior
```

A registry write, stored-state equality or device refresh alone is never runtime activation proof.

## Owner-only physical-validation harness

`tools/LatencyPilot.PhysicalValidation` is a permanent developer/owner tool used only for Phase 3 physical validation.

It is **not** a product surface:

- not referenced or launched by `LatencyPilot.App`;
- not reachable through public Named Pipes;
- not launched by `run.ps1`;
- not included by installer/release packaging;
- not a generic mutation shell;
- absent from normal hosted CI after temporary implementation smoke evidence is removed.

The harness may use narrowly scoped friend-assembly access to Service internals rather than making mutation APIs public.

### Allowlisted commands

```text
inspect
list-gpus
plan-gpu-affinity --evidence <baseline.json>
prepare-gpu-affinity --device <exact-instance-id> --processor <group-0-cpu> --confirm-physical-mutation
apply --experiment <guid> --confirm-physical-mutation
verify-gpu-placement --experiment <guid>
rollback --experiment <guid> --confirm-physical-mutation
recover --experiment <guid> --confirm-physical-mutation
```

Command roles:

- `inspect`: read unresolved journal state and fresh recovery assessment; no device write.
- `list-gpus`: enumerate exact present display-adapter identity, driver and allocated interrupt resources; no mutation.
- `plan-gpu-affinity`: read a valid Real-world repeated baseline, reconcile it with current topology/CPU-set metadata and produce the bounded ranked candidate set; no journal/device mutation.
- `prepare-gpu-affinity`: validate an explicit exact GPU and ranked group-0 CPU, capture exact original state and create the durable unresolved experiment; no candidate policy write yet.
- `apply`: revalidate target/driver/topology/stored original immediately before the owned write, apply candidate state, verify storage and perform exact-target device refresh/restart handling.
- `verify-gpu-placement`: read the exact journaled target, require allocated interrupt-affinity evidence for that devnode to match the candidate, then capture clean raw ETW runtime evidence. Service-module ISR correlation is supplementary and may be unavailable; ownership is never guessed.
- `rollback`: restore the exact captured original state, verify it and establish trusted activation/final state before terminal `Reverted`.
- `recover`: execute only the rollback-biased action justified by a fresh recovery assessment.

Mutation-capable commands require Windows, an elevated interactive owner terminal and explicit `--confirm-physical-mutation`. `verify-gpu-placement` is read-only with respect to mutation state but still requires elevation because it performs raw kernel ETW capture.

The tool never accepts a raw registry key/value, arbitrary device class or arbitrary executable command.

## Recovery model

`MutationRecoveryAssessment` is the shared fresh-state classifier used by startup inspection and explicit recovery. Normal Service startup **does not** perform hardware recovery writes.

`MutationRecoveryExecutor` always re-reads actual machine state immediately before selecting an action.

Supported actions:

- `None`: no write;
- `AbortPreparedWithoutApply`: terminalize a still-original `Prepared` entry without touching device policy;
- `FinalizeVerifiedRollback`: when actual storage is already original, verify/refresh the exact target before reaching trusted `Reverted`;
- `RestoreOriginalState`: use the exact snapshot/restart/verification rollback path;
- `ManualInterventionRequired`: refuse automatic action and preserve the unresolved record.

Recovery never resumes forward in journal v1.

Unknown state, external divergence, unreadable authoritative state or changed driver assumptions must never trigger a blind overwrite.

## Proven pre-write-abort semantics

If the transaction has durably entered `Applying` but, immediately before the owned registry write, proves that target state/topology/driver assumptions changed, it records a terminal aborted-before-owned-write result:

```text
Applying -> AbortedBeforeApply
```

That transition is limited to the proven pre-write-abort path. If the terminal journal transition itself fails, the unresolved `Applying` entry remains fail-closed for later fresh-state recovery assessment.

A crash in an ambiguous `Applying` window is never assumed to have written or not written.

## Candidate-planning boundary

The privileged mutation writer does not decide which CPU is "best".

Candidate ranking is evidence-driven and bounded. Gate A uses a valid topology-matched Real-world five-window baseline plus fresh current topology/CPU-set metadata. The planner preserves the documented policy:

- measured mean per-window DPC+ISR pressure;
- one logical sibling per physical core;
- current CPU-set availability when readable;
- hybrid efficiency-class representation;
- one processor group for v1 KAFFINITY;
- bounded default candidate count;
- CPU0 is not hard-excluded.

A ranked candidate is a screening hypothesis, not proof of improvement. The Service still independently revalidates any explicit candidate against current machine state before writing.

## Four arming gates

### Gate A — internal physical substrate proof

Public protocol remains v6/read-only and `MutationAvailable=false`.

On the owner-controlled Windows 11 x64 target prove:

1. exact clean current-main App + Service build/install/launch;
2. journal startup readiness with zero unresolved entries at clean start;
3. candidate selection from a valid topology-matched Real-world baseline rather than a guessed CPU;
4. one controlled unresolved entry survives Service restart and is reclassified from fresh actual state;
5. exact-target restart/reboot-required behavior is recorded without treating incomplete activation as success;
6. one candidate reaches verified stored state, exact-target **allocated interrupt affinity** matches the candidate, and a clean ETW runtime observation is captured;
7. service-module ISR correlation is reported when observable and remains explicitly unavailable/not-observed otherwise rather than guessed;
8. exact original state is restored and trusted;
9. one controlled forced-failure/recovery path is proven;
10. final `inspect` reports zero unresolved experiments.

Gate A is hardware evidence, not hosted-CI evidence.

### Gate B — mutation IPC implementation

Only after Gate A passes, design and implement protocol-v7 mutation-specific typed/allowlisted commands and authorization.

No generic registry/shell/process primitive. `MutationAvailable` remains false during Gate B source work.

### Gate C — physical IPC boundary proof

Physically validate the real non-elevated App/client → protected Service mutation path, including authorization, target identity, journal ownership, disconnect/cancellation behavior where applicable, restart/recovery and exact rollback.

### Gate D — product arming

Only after Gate C and the required optimizer target/guardrail path are credible may `MutationAvailable` become true for supported hardware and the user-facing one-click GPU workflow become reachable.

## Error and safety behavior

- unsupported OS/topology: fail before journal/apply;
- non-elevated mutation command: fail before transaction invocation;
- missing explicit confirmation: fail before transaction invocation;
- target is not the exact present display adapter: fail before write;
- baseline/topology mismatch during planning: refuse candidate plan;
- driver/topology/stored-state drift: abort or require recovery, never overwrite blindly;
- restart/reboot required or unhealthy devnode: keep experiment unresolved until trustworthy final state can be established;
- allocated-affinity mismatch after apply: Gate A placement proof fails; do not call the candidate active based only on registry state;
- lossy/invalid ETW capture: runtime proof is incomplete;
- unknown/diverged recovery state: manual intervention required;
- recovery failure: preserve unresolved state;
- tool crash: durable journal remains source of truth and Service startup inspection exposes unresolved work.

## Physical validation record

The Gate A record must retain:

- exact clean source revision;
- successful hosted Tests run for that revision;
- owner-local App/Service build/install/launch result;
- Windows edition/build;
- GPU name, driver and exact instance ID;
- baseline evidence source revision and ranked candidate set;
- selected candidate CPU/mask;
- exact original snapshot summary;
- every experiment ID and journal transition;
- exact-target restart/reboot-required evidence;
- allocated IRQ group/affinity before/after where available;
- ETW capture integrity and runtime ISR/module evidence;
- rollback/recovery result;
- Service restart/reboot observations;
- final exact-original state and unresolved count.

A physical validation claim without exact clean source provenance is invalid.

## Test and CI boundary

Permanent automated tests remain **9/10** unless a genuinely higher-blast-radius invariant justifies the final slot or replacing/merging an existing test.

Temporary implementation tests/smokes are allowed but must be removed before the final repository state. Normal hosted CI is test-only and cannot establish WinUI launch, LocalSystem Service runtime, hardware restart, allocated-resource activation, ETW placement behavior or mutation safety on real hardware.

## Explicit non-goals for this tranche

- no protocol v7 before Gate A;
- no user-facing Optimize GPU button before Gate D;
- no MSI/MSI-X mutation bundled with the first affinity experiment;
- no USB/xHCI or NIC mutation in this tranche;
- no generic recovery shell;
- no auto-resume-forward after crash;
- no release/installer inclusion of the validation harness;
- no universal claim that one CPU affinity improves latency;
- no invented service-module ISR threshold from one machine.

## Source-tranche completion condition

The source tranche is complete when:

- pre-write-abort semantics are corrected;
- rollback-biased recovery assessment/execution exists;
- the owner-only harness implements the bounded Gate A command surface;
- candidate planning is baseline/topology driven;
- exact-target allocated-affinity plus clean ETW verification is available;
- protocol remains v6/read-only and `MutationAvailable=false`;
- temporary test/smoke surfaces are gone;
- permanent suite remains within the 10-test cap;
- normal CI is test-only;
- authoritative docs agree on Gate A → B → C → D;
- `PROJECT_STATUS.md` points next to owner-local Gate A.

That source tranche is now implemented. **Phase 3 itself remains open** until physical Gate A, mutation IPC, physical Gate C and product Gate D are completed.