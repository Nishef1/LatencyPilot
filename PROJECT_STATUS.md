# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-14

## Overall

- Product version: **0.0.2 pre-alpha**
- Product completion: **Phases 0–1 closed; Phase 2 physical closure open; Phase 3 Gate A source/harness preparation complete and owner-local physical validation next**
- User-visible mutation capability: **Unavailable / unarmed**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service; public protocol remains read-only**
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`)
- Evidence schema: **`latencypilot-evidence-v8`**
- Baseline method: **`baseline-quality-v2`**
- Permanent automated tests: **9 / hard maximum 10**
- Normal hosted CI: **test-only**; temporary Phase 3 compile/smoke/test surfaces were removed after evidence
- Current source handoff: **rollback-biased recovery and the owner-only non-shipping Gate A harness are implemented and hosted-compile/smoke-verified; no mutation IPC or physical mutation is yet proven**

## Measurement authority

The current measurement contract remains:

```text
Quick diagnostic snapshot
  1 × 5 s
  integrity / attribution / concentration / hypothesis generation
  never a health or optimizer verdict

Repeated decision baseline — baseline-quality-v2
  workload already warmed/repeatable when applicable
  5 s LatencyPilot/service settle
  5 × 20 s authoritative windows
  750 ms inter-window settle
  >=95% actual/request duration
  >=1,000 DPC and >=1,000 ISR events/window
  clean capture integrity
  noise + drift gates

p99.9
  shown only with >=10,000 samples for that distribution
```

`Valid` means repeatable enough for the current comparison method. It does not mean the machine is globally healthy or optimally configured.

## Phase 2 — physical closure remains open

Source-complete read-only capabilities include:

- processor-group-aware topology and CPU sets;
- present PnP inventory with stable IDs and driver metadata;
- stored interrupt configuration kept distinct from allocated IRQ/resources and runtime DPC/ISR behavior;
- representative GPU/display, NIC and actual `USBXHCI` evidence surfaces;
- protected local Windows Service observation host;
- bounded/fail-closed Named Pipe protocol v6 with active-console-session authorization;
- ETW DPC/ISR collection, per-processor aggregation and image-lifetime-aware module attribution;
- explicit unresolved attribution rather than guessed ownership;
- Real-world / Controlled idle / Before-after scenario provenance;
- evidence-v8 with RequestIds, source revision, runtime CPU/power context and SHA-256;
- keyboard/high-contrast/adaptive evidence UX.

Physical evidence already includes a valid Real-world five-window decision baseline on clean revision `a4b4ff36c875982d5a263665860853462d0b055b`. Under ADR 0004 this is sufficient to allow targeted Phase 3 source work, but it does **not** close Phase 2.

Remaining Phase 2 physical obligations include:

1. valid Controlled-idle five-window baseline;
2. representative GPU/NIC/xHCI inspector sanity;
3. attribution plausibility against an independent observer where practical;
4. App-close/Service-restart/stale-ETW cleanup and active-session rejection checks;
5. Light/Dark/High Contrast, narrow/text-scaling, keyboard and UI Automation/screen-reader sanity;
6. JSON-visible-data/SHA/source-revision reconciliation;
7. proof that read-only validation performs zero unrelated system mutation.

## Phase 3 — current source state

ADR 0004 permits safety/candidate **source implementation** to overlap the remaining Phase 2 physical record. Mutation stays unavailable until the arming gates are satisfied.

### Durable safety substrate

Implemented in source:

- concrete SQLite persistence project;
- schema-v1 mutation journal with atomic transactions and compare-and-swap revisions;
- unresolved journal blocks another experiment;
- explicit `Prepared → Applying → Applied → Measuring → AwaitingDecision` and rollback/recovery states;
- proven pre-write aborts can terminalize as `AbortedBeforeApply` without claiming ownership of an external change;
- recovery from `RecoveryRequired` is biased toward rollback, not forward resume;
- journal payloads are bounded valid JSON objects;
- versioned GPU-affinity journal payload codec stores exact original snapshot and candidate;
- exact original registry value existence/kind/raw bytes are retained;
- startup Service initializes the journal and re-reads actual stored GPU affinity state for unresolved entries;
- shared `MutationRecoveryAssessment` re-reads actual machine state for explicit recovery decisions;
- unresolved stored state is classified as original / candidate / both / diverged / unknown;
- recovery planning is fail-closed: unknown, externally diverged state, or a changed target driver environment requires manual intervention rather than a blind write;
- `MutationRecoveryExecutor` performs explicit rollback-biased recovery and never turns Service startup into an automatic hardware write path;
- owner-only `tools/LatencyPilot.PhysicalValidation` exposes only the bounded internal validation commands needed for Gate A and requires elevation plus explicit acknowledgement before state-changing commands;
- public observation protocol still has only `GetStatus` and `CaptureKernelLatency`; no mutation command is reachable.

### GPU affinity applicability and candidate generation

Implemented in source:

- mutation target restricted to a present SetupAPI display adapter;
- only documented `Interrupt Management\Affinity Policy` values are touched;
- existing `DevicePolicy`, when present, must use the documented `REG_DWORD` shape;
- existing `AssignmentSetOverride`, when present, must use documented DWORD/QWORD or <=64-bit binary shape;
- one processor group only for v1 KAFFINITY writes;
- CPU0 is not hard-excluded;
- one logical sibling per physical core is selected using measured pressure plus CPU-set availability;
- hybrid efficiency classes are represented in the bounded screening set;
- candidate count defaults to four;
- baseline per-CPU DPC+ISR event shares are converted into per-window-normalized pressure evidence and fed into candidate planning.

### Apply/restart/revert source path

Implemented but **unarmed and not yet physically validated**:

- `Prepare` validates current topology/applicability, captures exact original state and durably journals it before apply;
- no-op candidate requests are rejected;
- before any write, the exact stored original, driver version and current processor topology are revalidated;
- after journal transition to `Applying`, the same state is re-read immediately before the registry write to narrow the external-change race;
- a change detected before LatencyPilot writes is recorded as a pre-write abort and is **not** automatically rolled back as though LatencyPilot owned that external change;
- candidate write is verified from stored state;
- device refresh uses SetupAPI `DIF_PROPERTYCHANGE` + `DICS_PROPCHANGE` for the exact display adapter;
- post-change install flags are inspected for `DI_NEEDRESTART` / `DI_NEEDREBOOT`;
- devnode state is checked through `CM_Get_DevNode_Status`, including restart-needed problem code 14;
- inability to establish a healthy in-place restart leaves the experiment unresolved in `RecoveryRequired`;
- rollback refuses a blind write if the display-driver version changed or current stored state matches neither the captured original nor the experiment candidate;
- otherwise rollback restores exact original values/key absence, verifies stored state, restarts the device and reaches `Reverted` only after the active original state is trusted;
- failed/incomplete rollback remains unresolved instead of being reported as success.

The code deliberately does **not** use a broad device-restart primitive that could restart unrelated devices sharing function/filter drivers.

### Runtime-effect verification

Implemented as source evidence, not yet integrated into an armed experiment:

- stored registry equality is not treated as runtime proof;
- the raw ETW capture can resolve GPU-driver ISR events by the display adapter's driver service/module name;
- observed ISR execution is counted on the candidate CPU versus off-target CPUs;
- unresolved attribution stays explicitly unresolved;
- no arbitrary pass threshold has been invented before physical data establishes an adequate rule.

### PresentMon

PresentMon API discovery, graphics-device correlation and workload metric capture exist in source. Available workload metrics include frame/FPS plus optional CPU/GPU busy/wait, GPU/display latency and dropped-frame evidence where the installed PresentMon API exposes them. These are not yet wired into the candidate screening/finalist orchestration.

## Current verification evidence

- Earlier Service compile evidence: Tests run `34839998553` / run #535 succeeded after the `CA1859` analyzer fix at `5292dd504cb426bafd714510106eef79104ebb66`.
- Pre-write-abort TDD: run `34843429491` / #541 failed on the missing `Applying → AbortedBeforeApply` transition; run `34843775832` / #543 succeeded after the semantic fix.
- Recovery-executor TDD: run `34844029007` / #545 failed because `MutationRecoveryExecutor` did not yet exist; run `34844530778` / #549 succeeded after the rollback-biased executor was implemented and Service compiled through the temporary test reference.
- Owner-harness TDD/smoke: run `34844759935` / #551 failed because the validation project did not yet exist. A later temporary smoke exposed a real `Program.cs` compile error in run `34845073158` / #554; the nullable problem-code formatting was fixed at `ad1d18507d8438fab2d482e53f93aee5d093336d`.
- Tests run `34846180666` / #561 on `f1e5feabae7c6d75c98b56a34da0abaa146be476` then completed successfully with both the temporary Phase 3 harness smoke and the critical suite green.
- Temporary Phase 3 test/smoke/Service-reference surfaces were removed in cleanup commit `aea84e6eb865c5624327b7a45b6cfe78795679dd`.
- Normal test-only Tests run `34846559881` / #562 on that cleanup commit completed successfully. The permanent suite is back to **9/10**.

Hosted evidence proves narrow source/tool contracts and deterministic tests. It does **not** prove WinUI App build, protected Service installation/startup, LocalSystem runtime behavior, named-pipe mutation authorization, hardware restart behavior, effective ISR placement or any physical mutation result.

No physical GPU mutation, GPU restart, forced-failure rollback or reboot recovery has been performed by this source work.

## Phase 3 arming gates — Gate A is next; B/C/D remain closed

### Gate A — internal physical substrate proof — NOT YET PASSED

Use the owner-only harness while public protocol v6 stays read-only. Gate A requires, on the supported owner-local Windows path:

1. current exact clean `main` builds and launches App + Service successfully;
2. startup journal/recovery inspection is healthy with zero unresolved entries at the clean start;
3. a controlled unresolved entry survives Service restart and is correctly reclassified from actual machine state;
4. exact-target `DICS_PROPCHANGE` restart behavior is physically verified and reboot-required behavior is handled without pretending activation succeeded;
5. one bounded candidate apply → stored verification → restart → runtime ISR evidence → exact rollback cycle is proven;
6. a deliberate supported failure proves rollback/recovery rather than only the happy path;
7. final actual state is the exact original and `inspect` reports zero unresolved entries.

### Gate B — typed mutation IPC implementation — BLOCKED BY GATE A

After Gate A passes, add only mutation-specific typed/allowlisted IPC and mutation-specific authorization. No arbitrary registry, shell, process or generic privileged primitive. Keep product mutation unarmed and `MutationAvailable=false`.

### Gate C — physical IPC boundary proof — BLOCKED BY GATE B

Physically validate the real client/App → Service mutation path on supported hardware, including authorization, target identity, journal ownership, restart/recovery and exact rollback.

### Gate D — product arming — BLOCKED BY GATE C

Only after Gate C and the required optimizer target/guardrail path are credible may the supported one-click mutation workflow become user reachable.

Phase 2 physical closure remains a separate obligation; Phase 3 progress does not silently close it.

## Exact next owner-local sequence

After the latest exact `main` revision has a completed green Tests run:

```powershell
git pull
.\run.ps1
```

First objective is **compile/install/launch validation only**, not mutation. Confirm:

- App header shows the exact clean source revision;
- Service starts and remains read-only over protocol v6;
- observation and evidence export still work;
- `run.ps1` reports the protected Service as `Running`;
- `run.ps1`/Service logs prove mutation-journal startup inspection with zero unresolved experiments on a clean machine state.

If current App/Service source does not compile, install or launch on the supported owner-local Windows path, fix that before any physical Gate A mutation step.

Then execute `docs/PHASE3_PHYSICAL_VALIDATION.md` exactly. The Gate A sequence is:

```text
read-only harness inspect + exact GPU identity
→ prepare one bounded journaled experiment
→ prove unresolved-state survival/reclassification across Service restart
→ apply one candidate and record exact-target restart/reboot-required evidence
→ reconcile stored state with runtime GPU ISR placement evidence
→ restore exact original state and prove terminal rollback
→ run one controlled forced-failure/recovery exercise
→ finish with zero unresolved experiments
```

## After Gate A passes

Proceed in this order:

```text
Gate B — mutation-specific typed/allowlisted IPC + authorization
→ Gate C — physical end-to-end client/App → Service mutation proof
→ bounded candidate screening
→ PresentMon + ETW target/guardrail integration
→ balanced finalist confirmation (for example ABBA/BAAB)
→ Gate D — arm the supported one-click workflow
→ Keep best or restore exact original
```

Later phases remain USB/xHCI, NIC/RSS, bounded cross-subsystem optimization/profiles/Pareto/Restore Baseline, then release hardening.