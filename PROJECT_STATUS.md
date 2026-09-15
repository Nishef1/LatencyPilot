# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-15

## Overall

- Product version: **0.0.2 pre-alpha**.
- Product completion: **Phases 0–1 closed; Phase 2 physical closure open; Phase 3 GPU execution source implemented but physical arming open; Phases 4–7 source completion in progress**.
- User-visible mutation capability: **Unavailable / unarmed**.
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**.
- Privileged boundary: **Windows Service; public protocol remains read-only**.
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`).
- Evidence schema: **`latencypilot-evidence-v8`**.
- Baseline method: **`baseline-quality-v2`**.
- Permanent automated tests: **10 current; default target 10; owner-authorized maximum 20 only when genuinely required by the <=1200-lines-per-test-file rule or a materially safer durable split**.
- Hosted CI: **test-only**. It does not prove App/Service launch, physical hardware behavior, installer/package behavior, accessibility or signing.
- Current exact source evidence: `bf71682fd63c74fff02ee161db438d7663e4ec06` passed Tests run **#667 / `34939960176`**.

## Current execution ladder

1. **Completed now:** the internal GPU source path now composes bounded candidate screening, interruption-safe journal ownership, synchronized ETW + raw PresentMon evidence, effective GPU ISR-placement verification, balanced ABBA+BAAB finalist confirmation, explicit decision interpretation and verified Keep/Restore journal transitions. Product mutation is still not exposed through protocol v6.
2. **Evidence:** exact HEAD `bf71682fd63c74fff02ee161db438d7663e4ec06` completed the normal hosted Tests workflow successfully in run **#667 / `34939960176`**. Earlier TDD runs deliberately failed for the missing orchestrator, missing confirmation path, a real `BeginMeasurement` rollback leak and missing runtime-placement contract before the corresponding fixes were applied.
3. **Still open:** Phase 2 owner-local read-only closure; Phase 3 Gate A physical restart/recovery/apply/runtime-placement/rollback proof; Gate B/C/D mutation authorization/IPC/arming; USB/xHCI timing and authoritative port evidence; NIC/RSS source; cross-subsystem profiles/restore; release hardening; owner-local App/UI/package/signing/representative-hardware validation.
4. **Next stage:** complete repository-verifiable USB/xHCI and input evidence, then authoritative NIC/RSS read-only evidence. Keep every mutation path internal/unarmed while Gate A is open.
5. **After that:** complete transparent profiles/Pareto/global Restore Baseline source and release/upgrade/uninstall diagnostics; reconcile all authority docs; run exact-final-HEAD test-only CI; then execute the owner-local physical closure sequence.

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

Repository source includes processor-group-aware topology, present PnP/driver/interrupt inventory, stored-vs-allocated-vs-runtime interrupt evidence separation, protected read-only Service/Named Pipe v6, ETW DPC/ISR capture and module/processor attribution, repeated baseline quality gates, evidence-v8 provenance/SHA verification and the adaptive evidence UI.

Physical evidence already includes a valid Real-world five-window decision baseline on clean revision `a4b4ff36c875982d5a263665860853462d0b055b`. ADR 0004 permits targeted later-phase source work from that evidence but does **not** close Phase 2.

Remaining Phase 2 owner-local obligations:

1. valid Controlled-idle five-window baseline;
2. representative GPU/NIC/xHCI inspector sanity;
3. attribution plausibility against an independent observer where practical;
4. App-close/Service-restart/stale-ETW cleanup and active-session rejection checks;
5. Light/Dark/High Contrast, narrow/text-scaling, keyboard and UI Automation/screen-reader sanity;
6. JSON-visible-data/SHA/source-revision reconciliation;
7. proof that read-only validation performs zero unrelated system mutation.

## Phase 3 — GPU execution source state

ADR 0004 permits internal mutation-safety/source implementation to overlap remaining Phase 2 physical work. Mutation stays unavailable until the arming gates are physically satisfied.

### Durable mutation and recovery substrate

Implemented in source:

- concrete SQLite `LatencyPilot.Persistence` journal with compare-and-swap revisions;
- one unresolved experiment blocks unsafe follow-on mutation;
- explicit `Prepared → Applying → Applied → Measuring → AwaitingDecision` plus `Reverting/Reverted/Kept/RecoveryRequired/AbortedBeforeApply` states;
- exact original GPU affinity snapshot and candidate payloads;
- startup and explicit recovery re-read actual machine state;
- fail-closed original/candidate/diverged/unknown classification;
- rollback-biased recovery executor;
- exact-target GPU restart path with reboot/restart-required detection;
- pre-write external-change detection that does not claim ownership of someone else’s write;
- interruption-safe two-value affinity writes using transaction/compensation semantics, with unresolved recovery retained when exact original restoration cannot be proven;
- owner-only physical-validation harness; public protocol v6 remains mutation-free.

### GPU candidate and synchronized experiment execution

Implemented in source:

- topology-aware bounded physical-core candidates from measured DPC+ISR pressure; CPU0 is not hard-excluded;
- screening may only nominate `ConfirmFinalist`; it cannot directly Keep a screening result;
- every screening candidate is applied through the journaled transaction, measured, then exact-rolled-back before the next candidate;
- `BeginMeasurement` and capture are inside the same owned rollback scope, so setup/capture failure cannot leave an active candidate silently behind;
- one finalist proceeds to the fixed eight-run ABBA+BAAB confirmation schedule;
- consecutive candidate blocks reuse the active candidate; transitions back to Original perform exact rollback;
- final candidate reaches `AwaitingDecision` only after the final candidate measurement;
- `KeepCandidate` re-reads the stored target and driver identity before terminalizing `Kept`; otherwise recovery/rollback remains required;
- any non-Keep confirmation restores exact original state.

### Synchronized ETW + PresentMon evidence

`GpuOptimizationEvidenceCollector` now:

- verifies expected original/candidate stored state immediately before and after capture;
- collects ETW and PresentMon concurrently under one deadline;
- requires >=95% common requested interval;
- keeps exact session/workload/environment/source revision and unique capture identity;
- uses raw DPC duration samples as the primary metric;
- uses raw PresentMon frame samples for available CPU frame-time / CPU-GPU busy-wait / GPU/display latency / dropped-frame guardrails;
- rejects workload/process/version/window mismatch, incomplete required samples, short intervals and dirty identities rather than manufacturing evidence.

### Runtime placement proof boundary

Stored registry equality is not runtime proof. Candidate evidence now additionally uses `GpuInterruptRuntimePlacementVerifier` against the same ETW capture:

- GPU-driver ISR events must be attributable to the exact display-adapter driver/module identity;
- at least one attributed GPU ISR must be observed on the candidate logical processor;
- attributed GPU ISR on off-target processors makes the candidate run unusable;
- unresolved ISR attribution remains explicit and does **not** count as evidence that the candidate placement worked.

This source contract is verified deterministically; actual effective placement on the owner’s GPU still requires Gate A physical evidence.

### Decision semantics

The shared comparison path retains explicit `Improved`, `Regressed`, `Tradeoff`, `NoMeasurableDifference` and `Inconclusive` results. Confirmation requires eight compatible runs, verified expected state, clean capture integrity, equal >=30 s requested durations with >=95% completion and >=1,000 samples per required metric/run. A guardrail regression prevents an automatic win. Raw deltas, sample counts, noise/drift reasons and run provenance are retained.

## Current GPU verification evidence

Recent execution-source TDD/CI chain:

- run #660 failed because the orchestration backend/type did not exist;
- run #661 passed after bounded screening orchestration was added;
- run #662 failed on the deliberate “finalist exists but confirmation is not executed” stop;
- run #663 passed after ABBA+BAAB confirmation and verified terminal Keep were implemented;
- run #664 failed with `RollbackCount` expected 1 / actual 0, exposing a real leak when `BeginMeasurement` failed after apply;
- run #665 passed after the rollback ownership scope was fixed;
- run #666 failed because effective GPU ISR-placement evidence was not yet part of the collector contract;
- exact HEAD `bf71682fd63c74fff02ee161db438d7663e4ec06` passed run **#667 / `34939960176`** after runtime placement evidence was integrated.

Hosted evidence proves deterministic/source contracts only. It does not prove App launch, LocalSystem execution, protected mutation IPC, device restart behavior or latency improvement on physical hardware.

## Phase 3 arming gates

### Gate A — internal physical substrate proof — OPEN

The prior owner-local read-only preflight on clean `7601d1935aa30fc1a0e416d449ad712382aa779e` found the RTX 3070 and a clean journal but produced an allocated-resource tuple whose semantics were not acceptable as runtime placement proof. The source parser is now defensive and runtime ISR evidence is a separate authoritative layer, but no physical apply/restart/rollback cycle has yet closed this gate.

Gate A still requires on current clean `main`:

1. App + protected Service build/install/launch on the supported owner-local Windows path;
2. clean startup journal with zero unresolved entries;
3. controlled unresolved entry survives Service restart/reclassification from actual machine state;
4. exact-target `DICS_PROPCHANGE` and reboot-required behavior are physically verified;
5. one bounded GPU candidate apply → stored verification → restart → effective ISR evidence → exact rollback;
6. one deliberate supported failure proves rollback/recovery rather than only the happy path;
7. final machine state equals the exact original and journal reports zero unresolved entries.

### Gate B — typed mutation IPC — BLOCKED BY GATE A

Only after Gate A passes may mutation-specific typed/allowlisted protocol commands and authorization be implemented. No arbitrary registry/shell/process primitive. `MutationAvailable` remains false.

### Gate C — physical IPC boundary proof — BLOCKED BY GATE B

Validate the real App/client → Service mutation path, authorization, target identity, journal ownership, restart/recovery and exact rollback on supported hardware.

### Gate D — product arming — BLOCKED BY GATE C

Only after Gate C and credible target/guardrail evidence may the supported one-click mutation workflow become user reachable.

## Phases 4–7 source-completion sequence

The approved remaining source plan is `docs/superpowers/plans/2026-09-15-remaining-1.0-source-completion.md`.

Execution order:

1. authoritative Raw Input → USB hub/port → xHCI route evidence and host-observable input report timing;
2. xHCI ETW attribution/readiness without arming mutation;
3. authoritative StandardCimv2 NIC/RSS inventory and local-network benchmark/readiness contracts;
4. transparent workload profiles, Pareto/trade-off policy and journal-aware global Restore Baseline planning;
5. recovery-aware release/upgrade/uninstall and redacted diagnostics;
6. final roadmap/system-design/docs reconciliation and exact-final-HEAD test-only CI.

Source implementation of a later subsystem is not permission to perform or expose its physical mutation before the applicable safety gate exists.

## Exact next owner-local closure sequence

Repository source work can continue independently, but true 1.0 requires this evidence order:

```text
finish remaining Phase 2 read-only physical audit
→ build/install/launch current exact main
→ Gate A internal physical GPU substrate proof
→ Gate B mutation-specific IPC source
→ Gate C physical App/client → Service proof
→ Gate D user-facing GPU arming
→ physical USB/xHCI and NIC/RSS experiment/rollback evidence
→ App UI/accessibility validation
→ package/installer/signing/upgrade/uninstall validation
→ representative supported hardware audit
→ final 1.0 tag audit
```

Never mark a physical/build/package/signing requirement complete from hosted CI or source existence alone.
