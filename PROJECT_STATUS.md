# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-12

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current release target: **Phase 2 read-only observation engine**
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service exists for read-only kernel observation; mutation commands do not exist**
- Permanent automated tests: **9 / hard maximum 10**
- Latest green CI: **run `34710672075`**, commit `afa9951946bd576c4f2f149cf9f76b40be8cc0dc`
- Latest green CI result: Release build + 9/9 permanent tests + App publish + Service publish + combined artifact upload all succeeded.

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the buildable solution, domain/comparison foundation, non-mutating app vertical slice and focused critical suite.

Historical note: Phase 1 originally closed with WPF. ADR 0002 later superseded the UI choice with WinUI 3. The historical WPF evidence remains valid as Phase 1 evidence and is not rewritten.

## Architecture changes completed during Phase 2

### WinUI 3 migration — COMPLETE

Decision: `docs/adr/0002-winui3.md`.

Implemented:

- `LatencyPilot.App` migrated from WPF to WinUI 3;
- Windows App SDK pinned to Stable `2.4.0`;
- unpackaged deployment via `WindowsPackageType=None`;
- Windows App SDK self-contained deployment enabled;
- app pinned to `win-x64`;
- normal-user/non-elevated UI boundary preserved;
- no MVVM framework, second UI toolkit or MSIX identity added.

### Privileged observation service — IMPLEMENTED FOR PHASE 2

Decision: `docs/adr/0003-privileged-observation-service.md`.

Implemented:

- Windows Service hosts privileged kernel observation while WinUI remains non-elevated;
- local Named Pipe transport;
- versioned typed protocol;
- allowlisted Phase 2 commands only: service status and kernel-latency observation;
- generic command-name/shell/registry/process primitives do not exist;
- service mutation capability remains `false`;
- App and Service are both published in the Windows x64 artifact;
- client and server I/O are deadline-bounded so an idle/hung connection cannot wait forever.

Evidence:

- service/IPC foundation: run `34709891520`;
- UI → Service → ETW vertical slice: run `34710054683`;
- bounded client/server IPC deadlines: run `34710502158`;
- observation naming + p99.9 UI: run `34710672075`.

## Phase 2 — IN PROGRESS

Goal: trustworthy, strictly read-only Windows observation before any system mutation.

### 2.1 Inventory and evidence provenance

Implemented and CI-verified:

- [x] processor-group-aware package/core/logical processor topology implementation;
- [x] stable present PnP device instance IDs through SetupAPI;
- [x] driver provider/version/INF metadata through unified device properties;
- [x] stored interrupt configuration inspection (`MSISupported`, message limit, affinity policy/mask where present);
- [x] interrupt-configuration availability provenance so inaccessible hardware keys are not silently treated as “no configuration”;
- [x] allocated IRQ/resource assignment reader through Configuration Manager `ALLOC_LOG_CONF`, preserving API-unavailable/no-allocated-configuration states and resource-handle cleanup.

Still required before 2.1 can close:

- [ ] physical Windows 11 validation of topology and representative GPU/xHCI/NIC inventory;
- [ ] physical validation of allocated resource snapshots on representative devices;
- [ ] distinguish line/message interrupt evidence only where authoritative Windows source data permits it.

Important semantics:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

### 2.2 ETW observation

Implemented and CI-verified:

- [x] controlled kernel ETW session creation with LatencyPilot-owned collision-resistant session names;
- [x] DPC collection;
- [x] ISR collection;
- [x] ETW processor-number attribution and per-processor aggregation;
- [x] p50/p95/p99/p99.9/max duration distributions;
- [x] event-loss / invalid-event / event-limit provenance;
- [x] cancellation, timeout and cleanup paths;
- [x] privileged Service execution with non-elevated WinUI client;
- [x] bounded typed IPC response rather than transferring the raw event set to the UI.

Still required before 2.2 can close:

- [ ] authoritative native module/driver attribution for DPC/ISR routine addresses;
- [ ] kernel image mapping that includes modules already loaded before the observation begins, using kernel image events plus the appropriate rundown/CAPTURE_STATE path;
- [ ] preserve unresolved routine addresses as unknown instead of guessing driver names;
- [ ] physical Windows 11 validation of Service → ETW → IPC capture and cleanup;
- [ ] physical validation of per-processor attribution, including explicit multi-processor-group evidence if hardware is available.

Context7/PerfView documentation re-check on 2026-09-12 confirmed that native code address resolution requires kernel ImageLoad evidence and past-state/CAPTURE_STATE when the image predates the trace. Therefore module attribution intentionally remains open until that mapping is authoritative.

### 2.3 Baseline quality — NOT STARTED

A single five-second capture is deliberately called an **observation**, not a baseline.

Required:

- [ ] repeated baseline windows;
- [ ] stable run protocol and minimum number of valid windows;
- [ ] measured noise floor per relevant metric;
- [ ] inter-window drift detection;
- [ ] outlier/invalid-window handling without silently deleting inconvenient measurements;
- [ ] background-load warning where observable;
- [ ] thermal/power-state warning where observable and reliable enough to use;
- [ ] baseline validity verdict with reasons;
- [ ] invalid/inconclusive baseline cannot unlock optimization.

### 2.4 UX — PARTIAL

Implemented:

- [x] WinUI service-health status;
- [x] explicit read-only safety boundary;
- [x] five-second kernel **observation** action through the Service;
- [x] DPC/ISR event counts;
- [x] p99 and p99.9 display;
- [x] ETW loss/invalid/limit-reached status;
- [x] count of processors observed;
- [x] no single capture is mislabeled as a trustworthy baseline.

Still required:

- [ ] per-processor concentration view/ranking;
- [ ] top DPC/ISR module/driver contributors after authoritative attribution exists;
- [ ] inspectable raw/auditable aggregates beyond the compact summary;
- [ ] repeated-baseline/noise/drift UX;
- [ ] baseline quality verdict/reason;
- [ ] clear evidence-level labels for stored configuration vs assigned resource vs runtime behavior.

## Current execution ladder

The following sequence is authoritative unless a new finding forces a step-back change.

### Stage A — authoritative module/driver attribution — CURRENT

Work in order:

1. capture kernel image mapping needed for native routine-address attribution;
2. include already-loaded kernel images via supported rundown/CAPTURE_STATE behavior rather than relying only on future ImageLoad events;
3. model image ranges with base address, size and authoritative module/file identity;
4. resolve each DPC/ISR routine address only when it falls inside an authoritative image range;
5. preserve unresolved addresses as unknown/raw hexadecimal evidence;
6. aggregate DPC/ISR counts and duration distributions by resolved module;
7. expose only the bounded aggregate through Protocol/Service;
8. perform step-back review for image unload/range reuse, lost image events, overflow, session ordering and cleanup;
9. run Release CI without adding a permanent test unless a genuinely high-blast-radius invariant justifies replacing/using the final 10th slot.

**Stage A closes only when:** module attribution exists on `main`, CI is green, unresolved addresses remain explicitly unresolved, and no guessed driver attribution exists.

**Immediately after Stage A:** Stage B physical Windows 11 observation validation.

### Stage B — physical Windows 11 observation validation

Work in order:

1. install/run the published App + Service artifact on a physical Windows 11 x64 machine;
2. verify Service startup and non-elevated App connection;
3. capture representative idle and controlled-load observations;
4. verify DPC/ISR events, processor distribution and module attribution are plausible and internally consistent;
5. verify zero-change behavior: registry/MSI/affinity/power/network configuration remains untouched;
6. force cancellation/client disconnect/service shutdown and verify ETW/session cleanup;
7. compare representative driver/module findings against a trusted external observation such as LatencyMon/PerfView where practical;
8. record machine/Windows/CPU/device/driver context with the validation evidence.

**Stage B closes only when:** the real machine produces usable read-only observations and cleanup/privilege boundaries hold under failure paths.

**Immediately after Stage B:** finish remaining Phase 2.1 physical inventory/resource validation, then Stage C baseline quality.

### Stage C — repeated baseline and quality engine

Work in order:

1. define repeated-window capture protocol;
2. collect multiple valid windows;
3. compute per-metric variability/noise floor;
4. detect drift between windows;
5. define invalid/inconclusive baseline reasons;
6. add background/thermal quality signals only where the evidence is reliable;
7. prevent optimization/recommendation when the baseline quality gate fails;
8. expose the quality verdict and reasons in WinUI.

**Stage C closes only when:** LatencyPilot can distinguish a trustworthy repeated baseline from a single noisy observation.

**Immediately after Stage C:** Stage D Phase 2 UX completion.

### Stage D — Phase 2 evidence UX completion

Work in order:

1. per-processor concentration/ranking view;
2. top DPC/ISR module contributors;
3. raw/auditable aggregate inspection;
4. evidence-level labels for configuration, allocated resources and runtime ETW;
5. baseline quality verdict/reasons and invalid-state presentation;
6. responsive/keyboard/accessibility sanity pass for the Phase 2 surfaces.

**Stage D closes only when:** all remaining 2.4 requirements are met and the Phase 2 exit gate can be exercised on physical Windows 11.

**Immediately after Stage D:** Phase 2 closure review, then Phase 3.

### Stage E — Phase 3 safety substrate, before the first mutation

The Service and typed Named Pipe already exist; Phase 3 does **not** recreate them.

Next work will be:

1. durable SQLite experiment journal/recovery schema;
2. explicit mutation-specific authorization/allowlist extension;
3. Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
4. reboot-required and recovery-required states;
5. forced-failure rollback path;
6. only then the first supported GPU interrupt-affinity/MSI experiment.

No mutation work is allowed to jump ahead of Stage C/D or the Phase 3 safety substrate.

## Comparison-core correction discovered during step-back review

A prior comparator path returned `NoMeasurableDifference` before evaluating guardrails when the primary metric was inside its noise threshold. That could hide a collateral regression. It was corrected so a neutral primary plus materially regressed guardrail is `Regressed`.

Evidence: commit `83aa8b18ee17e3c9476ed735741812fc0bedab82`, run `34705597798`.

This justified one high-blast-radius permanent test; the suite remains at 9/10.

## Hard test rule

Permanent automated tests may not exceed **10**. Temporary implementation/debug tests may be created and removed. Exceeding 10 requires explicit owner approval plus an ADR explaining why staying within 10 creates greater risk.

## Completion and reporting discipline

Before closing a subsection, perform the mandatory step-back review from `AGENTS.md`: re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test cap, docs drift and current owner constraints.

Every progress report must state:

- what completed now;
- evidence;
- what remains open;
- the exact next stage and its ordered substeps;
- what follows that next stage.

If required evidence is missing, use **implemented but not closed**, not “done”.
