# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-13

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current product version: **0.0.2 pre-alpha**
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **read-only Windows Service; mutation commands do not exist**
- Observation protocol: **v5**
- Evidence schema: **latencypilot-evidence-v6**
- Permanent automated tests: **8 / hard maximum 10**
- Current stage: **Phase 2 source implementation is substantially complete; Stage B physical validation, Stage C physical baseline closure and Stage D physical UX closure remain open**
- Current owner instruction for this implementation loop: **do not run App/Service builds; hosted CI remains test-only**

## Current automated evidence

The hosted **Tests** workflow is intentionally **test-only**. It runs the eight permanent critical tests on `main` and pull requests.

Hosted Actions do **not** build or publish the WinUI App, build or publish the Service, create installers/portable packages, run GUI launch smoke, upload release artifacts or publish releases.

Therefore:

- deterministic contract correctness may use the hosted Tests workflow as evidence;
- App/Service compile evidence is not supplied by hosted CI;
- package/launch-smoke evidence belongs to the owner-local release path when that validation is intentionally resumed;
- hardware behavior and latency claims require physical Windows 11 evidence;
- this implementation loop must continue source work/checklist hardening without substituting a hidden build step for the test-only CI contract.

Rapid source commits may cancel superseded workflow runs through Actions concurrency. Final deterministic claims must use a completed Tests run for the exact revision being claimed.

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the solution, deterministic comparison/domain foundation, focused permanent suite and first non-mutating desktop vertical slice. ADR 0002 later superseded the historical WPF UI with WinUI 3. Historical Phase 1 artifacts do not redefine the current test-only hosted-CI policy.

## Phase 2 architecture/hardening implemented in source

### WinUI 3 boundary

- normal-user/non-elevated App;
- Windows App SDK 2.4 Stable;
- `WindowsPackageType=None`;
- self-contained Windows App SDK deployment;
- `win-x64` target;
- no second UI toolkit or speculative MVVM/navigation framework.

### Privileged observation Service

- Windows Service hosts privileged kernel ETW observation;
- commands remain allowlisted to status + kernel observation only;
- `ServiceBoundary.MutationAvailable == false`;
- no generic shell/process/registry primitive;
- bounded typed/versioned Named Pipe framing;
- unknown JSON members fail closed;
- network identities denied;
- ACL narrowed to local interactive/service identities;
- connected client session must match the active console session;
- failure to establish client-session identity fails closed;
- disconnect/protocol activity cancels active capture work;
- deadlines remain bounded;
- current observation authorization is explicitly not future mutation authorization;
- the App requires the status response to prove the host is the installed Windows Service with expected kernel-capture privilege context before capture is enabled.

### Current project boundaries

Current Phase 2 source projects are:

```text
Core
Benchmarking
Protocol
Platform.Windows
Service
App
CriticalTests
```

The future SQLite persistence boundary is intentionally **not** materialized as an empty Phase 2 project. It is created in Phase 3 together with its real schema/journal/recovery contract.

### Structured diagnostics

Contract: `docs/DIAGNOSTICS.md`.

- App CLEF/compact-JSON logs under `%LOCALAPPDATA%\LatencyPilot\Logs\App`;
- Service logs under `%PROGRAMDATA%\LatencyPilot\Logs\Service`;
- bounded rolling/retention;
- async non-blocking file sinks;
- App → IPC → Service correlation through `RequestId`;
- protocol-v5 capture evidence preserves the same `RequestId`;
- expected capture-unavailable/session-rejection paths retain structured failure provenance;
- raw per-event DPC/ISR logging remains prohibited from the measurement hot path.

## 2.1 Inventory and evidence provenance — SOURCE IMPLEMENTED, PHYSICAL VALIDATION OPEN

Implemented:

- processor-group-aware topology;
- stable present PnP instance IDs;
- driver provider/version/INF metadata;
- stored interrupt configuration with availability/error provenance;
- ConfigMgr allocated IRQ/resource evidence;
- optional per-device property/resource failures degrade to partial evidence instead of invalidating the whole inventory;
- Windows-specific representative-device selection lives in `Platform.Windows`, not the App;
- representative read-only evidence roles cover display/GPU-class devices, network-class devices and devices actually bound to the `USBXHCI` service;
- selector prioritizes evidence-rich devices with allocated IRQ/configuration/driver data without claiming a device is the system's primary device;
- WinUI source contains a read-only device-evidence inspector that presents instance/service/driver metadata, stored MSI/affinity configuration and allocated IRQ resources as separate layers;
- `MSISupported=1` is explicitly labeled as stored configuration only;
- raw ConfigMgr IRQ flags are shown as raw evidence and are not misrepresented as authoritative line/MSI state;
- representative selector invariants are folded into the existing Windows integration contract test, so the permanent test count remains eight.

Still open:

- physical representative GPU/xHCI/NIC validation;
- allocated-resource plausibility checks on real target hardware;
- actual line/message interrupt distinction only if an authoritative assigned-resource source can expose `CM_RESOURCE_INTERRUPT_MESSAGE`; stored MSI configuration and ConfigMgr `IRQ_DES` flags are not substitutes;
- representative multi-processor-group evidence where hardware exists.

Authoritative semantic boundary:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

## 2.2 ETW observation — SOURCE IMPLEMENTED, PHYSICAL VALIDATION OPEN

Implemented:

- controlled kernel ETW session lifecycle;
- DPC/ISR collection;
- per-processor aggregation;
- authoritative kernel-image module attribution with stop-time rundown;
- image lifetime/address-reuse handling;
- unresolved/ambiguous mapping remains unresolved;
- bounded module and unresolved-routine contributor lists;
- one canonical percentile estimator;
- p50/p95/p99/max distributions;
- p99.9 only when at least 1,000 samples exist in that distribution;
- capture-integrity provenance for ETW loss/invalid latency/image events/event limit;
- bounded protocol-v5 aggregate response;
- capture `RequestId` retained for evidence/log correlation;
- fail-closed client response validation including distribution ordering, contributor bounds and threshold-count consistency;
- active-console client-session authorization;
- expected capture failures retain structured root-cause provenance.

Interpretation rules:

- `DPC >100 µs` and `ISR >25 µs` are Microsoft driver guidance;
- `>1 ms` and `>3 ms` are LatencyPilot local diagnostic tail buckets, not official Windows pass/fail or user-impact severity boundaries;
- an integrity-warning capture cannot receive a healthy classification merely because threshold counts are low;
- raw counts, p99/max, attribution and capture integrity remain authoritative; color/badges are supplemental.

Still open:

- physical current-Service capture/cleanup validation;
- active-session rejection validation with a second local session where practical;
- attribution plausibility against an independent observer where practical;
- observer-overhead profiling only if physical evidence shows a meaningful measurement risk.

## 2.3 Baseline quality — SOURCE IMPLEMENTED, PHYSICAL CLOSURE OPEN

`baseline-quality-v1` now requires:

- **exactly five** contiguous windows, not merely at least five;
- minimum 20 events per metric/window;
- clean capture integrity;
- <=30% relative P10–P90 spread;
- <=20% early/late drift;
- no >50% extreme-window deviation;
- `WindowNumber` (`1..5`) as authoritative sequence;
- `StartedAtUtc` as provenance rather than a monotonic sequencing clock.

The consolidated baseline test covers stable, extra-window, drifted, ETW-loss, gapped-window and backwards-wall-clock scenarios without increasing the permanent test count. A stable sixth window now correctly makes `baseline-quality-v1` `Inconclusive` because extra data must not silently change a versioned method.

### Quiet repeated-measurement flow

The current WinUI source:

- freezes the selected measurement scenario while capture is busy;
- waits for a settle delay before the first capture;
- avoids full observation/module/processor/health/list redraws between authoritative windows;
- updates only lightweight progress/status text between windows;
- renders detailed observation state after the fifth capture;
- computes the baseline verdict from all five windows;
- permits partial evidence export after a stopped sequence but never upgrades a partial sequence to `Valid` merely because it can be exported.

### Low-overhead runtime context

Best-effort runtime context brackets each App capture request/response interval as provenance:

- system CPU busy percentage from `GetSystemTimes` cumulative deltas;
- AC/DC source, battery state and Battery Saver from `GetSystemPowerStatus`;
- active power-plan GUID/friendly name from `PowerGetActiveScheme` / `PowerReadFriendlyName`;
- Windows 11 user-configured AC/DC power mode from `PowerGetUserConfiguredACPowerMode` / `PowerGetUserConfiguredDCPowerMode`;
- configured power mode is treated as the user's configured Best power efficiency / Balanced / Best performance preference, not proof of effective runtime power-management state;
- plan/source/configured-mode/Battery-Saver changes are surfaced as provenance;
- failure to collect optional context does not invalidate otherwise clean DPC/ISR evidence or become a fake zero;
- runtime context does not silently alter `baseline-quality-v1`.

Still open:

- physical controlled-idle + real-world five-window evidence;
- quiet-sequencing behavior on the real compositor/workload;
- runtime CPU/active-plan/configured-mode plausibility on the physical machine;
- thermal context only if an authoritative low-overhead source is identified and physical evidence demonstrates the need;
- future optimizer integration must consume `IsValidForComparison` before a mutation/keep recommendation can be enabled.

## 2.4 Evidence UX — SOURCE IMPLEMENTED, PHYSICAL UX CLOSURE OPEN

Implemented in source:

- read-only service/safety status;
- five-second observation;
- counts, p99/max and sample-gated p99.9;
- explicit `Need ≥1k` p99.9 state;
- capture-integrity warning state;
- CPU concentration and bounded module contributors;
- exact-value tail-rate visualization using lightweight visual bars instead of misleading progress-control semantics;
- explicit local-bucket wording for `>1 ms` / `>3 ms`;
- Real-world / Controlled idle / Before-after scenarios;
- scenario-specific guidance and stale-evidence invalidation;
- runtime CPU/power context summary plus power-context-change warning;
- repeated baseline progress/verdict/reasons source;
- read-only representative device-evidence inspector;
- explicit configuration vs allocated-resource vs runtime-evidence wording;
- keyboard accelerators (`Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Ctrl+E`);
- accessibility/high-contrast resources and automation metadata;
- adaptive narrow/wide workspace source;
- manual JSON evidence export;
- evidence schema `latencypilot-evidence-v6`;
- baseline export now fail-closes on capture/window/runtime-window count mismatch, non-contiguous evidence ordering, timestamp mismatch, empty/duplicate capture `RequestId`, quality-window mismatch or baseline-method mismatch;
- source revision is recorded from release/assembly metadata when available;
- unresolved `ulong` routine addresses serialize as hexadecimal strings;
- export serialization/file I/O stays outside authoritative baseline capture windows.

Still open:

- physical JSON-vs-visible-evidence audit and SHA-256 retention;
- narrow-window/text-scaling sanity on physical WinUI;
- keyboard focus/screen-reader sanity;
- physical evidence that warning/sample-insufficient/runtime-context/scenario invalidation states present correctly;
- physical validation of the new device-evidence inspector;
- remove/simplify duplicate explanatory surfaces only if the physical UI pass proves they are redundant.

## Permanent critical suite — 8 / 10

Current durable contracts cover:

1. experiment lifecycle transition safety;
2. benchmark verdict/guardrail matrix;
3. repeated baseline quality/sequence/exact-window behavior;
4. non-finite metric rejection;
5. canonical percentile estimator;
6. fail-closed framing plus protocol-v5 correlation/under-supported p99.9 round-trip behavior;
7. read-only observation command surface;
8. real Windows read-only inventory/topology/runtime-context/representative-selector consistency.

Two slots remain intentionally unreserved for later higher-blast-radius parser/recovery/mutation risks.

## Stage B — physical Windows 11 observation validation — OPEN

Runbook: `docs/PHYSICAL_VALIDATION.md`.

Required closure evidence eventually includes:

- exact source revision + completed green Tests run for deterministic contracts;
- real Windows Service/ETW behavior;
- controlled-idle + real-world observations;
- evidence JSON + SHA-256 + RequestId correlation;
- active-console authorization behavior;
- cleanup/recovery/no-stale-ETW-session behavior;
- module/processor plausibility;
- representative GPU/NIC/xHCI inventory/resource evidence through the read-only inspector;
- explicit zero-mutation evidence;
- accessibility/responsive sanity;
- package/uninstall evidence only when release/package validation is intentionally resumed.

Stage B cannot close from CI/VM evidence alone. Per the current owner instruction, this implementation loop does not run App/Service builds as a substitute for that later physical validation.

## Stage C — repeated baseline quality — SOURCE COMPLETE, PHYSICAL CLOSURE OPEN

Current implementation-loop work is source-only:

1. continue Phase 2 contract/architecture/checklist step-back review;
2. close remaining source-only inconsistencies without inventing physical evidence;
3. use the hosted **Tests** workflow only for deterministic contracts;
4. keep physical controlled-idle and real-world baseline execution deferred until the owner explicitly resumes runtime validation;
5. do not add speculative thermal/observer-overhead machinery without physical evidence.

When physical validation is resumed, Stage C requires controlled-idle and repeatable real-world five-window evidence, exported v6 JSON/hashes and plausible runtime context. Stage C closes only when the real App → Service → ETW path distinguishes trustworthy repeated evidence from unstable/incomplete evidence.

## Stage D — Phase 2 evidence UX — SOURCE IMPLEMENTED, PHYSICAL UX CLOSURE OPEN

Current source work may still simplify or correct semantics/accessibility where code review identifies a concrete issue. Physical closure later verifies responsive/text-scaling, keyboard/screen-reader behavior, device-evidence usability and JSON auditability.

## After Phase 2

Phase 3 remains blocked until Stage B/C/D closure evidence exists. When Phase 3 legitimately begins:

1. create the concrete persistence project together with SQLite schema/migrations/journal/recovery contract;
2. add mutation-specific authorization/allowlisting;
3. implement Detect → Snapshot → Validate → Journal → Apply → Verify;
4. implement interruption/reboot recovery and forced-failure rollback;
5. only then implement the first supported GPU interrupt-affinity/MSI experiment.

No mutation work may bypass Phase 2 closure.

## Release discipline

- source version: `0.0.2`;
- `v0.0.1` is historical/reserved and never reusable;
- hosted workflow supplies permanent-test evidence only;
- hosted CI does not own App/Service build, publish/package, GUI smoke or release publication;
- published versions are immutable;
- `main` is the default target for owner-directed automation; do not create/switch branches unless the owner explicitly requests it.

## Hard test rule

Permanent automated tests may not exceed **10** without explicit owner approval plus an ADR explaining why staying within 10 creates greater risk. Temporary implementation/debug tests may be created and removed before finalization.

## Completion/reporting discipline

Before closing a subsection, re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints.

Use **implemented but not closed** when physical/runtime/package evidence is still missing. Never convert an implementation checkbox into a physical-validation claim. Every progress report must state what completed, its evidence, what remains open, the exact next stage and what follows it.
