# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-15

## Overall

- Product version: **0.0.2 pre-alpha**
- Product completion: **Phases 0–1 closed; Phase 2 physical closure open; Phase 3 decision interpretation implemented, execution and physical arming incomplete**
- User-visible mutation capability: **Unavailable / unarmed**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service; public protocol remains read-only**
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`)
- Evidence schema: **`latencypilot-evidence-v8`**
- Baseline method: **`baseline-quality-v2`**
- Permanent automated tests: **10 / hard maximum 10**
- Normal hosted CI: **test-only**; temporary Phase 3 compile/smoke/test surfaces were removed after evidence
- Current source handoff: **bounded screening and balanced confirmation interpretation exist; real capture orchestration, atomic registry-write interruption handling, mutation IPC and physical mutation proof remain open**

## Current execution ladder

1. **Completed now:** screening only nominates a finalist (`ConfirmFinalist`); it cannot recommend keeping a screening result. `gpu-affinity-confirmation-v1` interprets eight ABBA + BAAB observations, rejects missing/short/dirty/unverified or identity-mismatched runs, reports metric deltas/sample counts/noise/drift and recommends Keep only for a confirmed primary improvement without guardrail regression. PresentMon aggregate conversion retains complete comparable windows; failed windows and changed process/swapchain sets cannot disappear silently. Uninstall safety and post-device-restart stored-state rechecks are present from `f0d8d7b`.
2. **Evidence:** exact prior head `c574897a8ace3f02581e9ffcc65b6879a26f445b` passed hosted Tests run `34899224926`. Current owner-local Debug critical suite passes **10/10** including confirmation failure/identity/noise/tail/drop scenarios. `dotnet build LatencyPilot.slnx --configuration Debug --no-restore` passes, including App and Service, with **0 warnings/errors**. These prove compilation and deterministic interpretation, not App launch, Service installation, or an improvement on hardware.
3. **Still open:** the decision engine has no App/Service execution caller. PresentMon currently provides dynamic window aggregates, not the adequately sampled per-frame distributions required by confirmation; adapter/workload correlation, all required GPU guardrails and common ETW/PresentMon intervals remain unintegrated. The existing two-value registry apply/restore is non-atomic: interrupted partial writes can become `Diverged` and require manual recovery. Keep public mutation unarmed. Gate A/B/C/D and the remaining Phase 2 physical record remain open.
4. **Next stage:** fix and verify atomic affinity storage/interruption semantics without weakening external-divergence refusal; validate current App/Service/harness startup; integrate actual ETW and PresentMon workload evidence with the candidate experiment/confirmation sequence; complete Gate A physical apply, exact rollback and controlled failure recovery before Gate B IPC.
5. **After that:** Gate B typed authorization/IPC → Gate C real boundary validation → Gate D supported GPU one-click execution. Complete USB/input and NIC/RSS experiments, whole-system profiles/restore, then release/signing/upgrade and representative physical validation. Source-only progress does not replace any of these product requirements.

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

PresentMon API discovery, graphics-device correlation and workload metric capture exist in source. Available workload metrics include frame/FPS plus optional CPU/GPU busy/wait, GPU/display latency and dropped-frame evidence where the installed PresentMon API exposes them. The aggregate-series builder now requires available windows from one process/API version/window duration and the same nonempty swapchain set. Each emitted metric covers every supplied window and swapchain; an optional incomplete metric is omitted as a whole. Failed sequences produce no comparable series. These aggregates are not raw frame samples and are not yet wired into the candidate screening/finalist orchestration.

### GPU decision interpretation — implemented, execution not closed

`GpuOptimizationDecisionEngine.Screen` ranks clean candidates but returns only `ConfirmFinalist` or `RestoreOriginal`. `GpuOptimizationConfirmation` consumes a valid decision baseline and eight completed run records from the same session/workload/environment/source. It requires verified expected state and capture integrity, equal requested durations of at least 30 seconds with >=95% actual completion, and at least 1,000 samples per metric per run. It preserves all run records and per-metric results. Duration tails use p99, higher-is-better throughput tails use p01, and dropped-frame ratios use the mean so rare drops are not hidden by p99. Noise/drift screens and effective thresholds are explicit in `docs/BENCHMARK_METHODOLOGY.md`.

This deterministic recommendation is not permission to change hardware, close a journal, or expose a product Keep command. The collection layer must establish the recorded identities/verification from actual machine evidence and select all required workload guardrails. That collection/execution integration and physical calibration remain open.

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
