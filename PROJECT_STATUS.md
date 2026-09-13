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
- Evidence schema: **latencypilot-evidence-v5**
- Permanent automated tests: **8 / hard maximum 10**
- Current stage: **Stage B physical validation open; Stage C source implemented but physical/runtime closure open; Stage D evidence UX implemented in source but physical UX closure open**

## Current automated evidence

The hosted **Tests** workflow is intentionally **test-only**. It runs the eight permanent critical tests on `main` and pull requests.

Hosted Actions do **not** build or publish the WinUI App, build or publish the Service, create installers/portable packages, run GUI launch smoke, upload release artifacts or publish releases. This is an explicit repository policy in `AGENTS.md`.

Therefore:

- deterministic contract correctness may use the hosted Tests workflow as evidence;
- App/Service compile evidence must come from an owner-local Windows build;
- package/launch-smoke evidence must come from the owner-local release path;
- hardware behavior and latency claims require physical Windows 11 evidence.

Rapid source commits may cancel superseded workflow runs through Actions concurrency. Final reporting must use a completed Tests run for the exact revision being claimed; a cancelled superseded run is not green evidence for a newer revision.

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the solution, deterministic comparison/domain foundation, focused permanent suite and first non-mutating desktop vertical slice. ADR 0002 later superseded the historical WPF UI with WinUI 3.

## Phase 2 architecture/hardening now implemented

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
- after connection, client Windows session must match the active console session;
- failure to establish client-session identity fails closed;
- RDP/multi-session observation is not implied by the current Phase 2 rule;
- disconnect/protocol activity cancels active capture work;
- deadlines remain bounded;
- current observation authorization is explicitly not future mutation authorization;
- the App now requires the status response to prove the host is the installed Windows Service with expected kernel-capture privilege context before enabling capture; an arbitrary IPC responder is not treated as healthy.

### Structured diagnostics

Contract: `docs/DIAGNOSTICS.md`.

- App CLEF/compact-JSON logs under `%LOCALAPPDATA%\LatencyPilot\Logs\App`;
- Service logs under `%PROGRAMDATA%\LatencyPilot\Logs\Service`;
- bounded rolling/retention;
- async non-blocking file sinks;
- `live.ps1` can stream the structured App/Service logs with component prefixes during development;
- App → IPC → Service correlation through `RequestId`;
- protocol-v5 capture evidence preserves the same `RequestId`;
- expected capture-unavailable paths retain structured failure provenance;
- raw per-event DPC/ISR logging remains prohibited from the measurement hot path.

### Protected Service installation and local development

- portable App may remain in a user-controlled extracted directory;
- Service payload is copied to `%ProgramFiles%\LatencyPilot\Service` before LocalSystem registration;
- normal installer use avoids copying the protected payload onto itself;
- uninstall removes Service registration and the protected managed Service copy;
- `run.ps1` builds locally, updates the protected Service through UAC and launches the App non-elevated;
- `live.ps1` keeps the App non-elevated, uses `dotnet watch` for the App and keeps privileged ETW inside the installed Service;
- `live.ps1` remote `origin/main` auto-pull is disabled by default; `-AutoPull` is an explicit opt-in because shared/Service changes can trigger privileged Service replacement;
- `scripts/Publish-Release.ps1` remains the owner-run build/package/release path.

Historical release note: `v0.0.1` was publicly published and later removed during the failed cloud replacement flow. It remains permanently reserved and must not be reused. The next candidate is `v0.0.2`.

## 2.1 Inventory and evidence provenance — IMPLEMENTED IN SOURCE, PHYSICAL VALIDATION OPEN

Implemented:

- processor-group-aware topology;
- stable present PnP instance IDs;
- driver provider/version/INF metadata;
- stored interrupt configuration with availability/error provenance;
- ConfigMgr allocated IRQ/resource evidence;
- optional per-device property/resource failures degrade to partial evidence instead of invalidating the entire inventory.

Still open:

- physical representative GPU/xHCI/NIC validation;
- allocated-resource plausibility checks;
- line/message interrupt distinction only where an authoritative Windows source supports the claim;
- representative multi-processor-group evidence where hardware exists.

Authoritative semantic boundary:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

## 2.2 ETW observation — IMPLEMENTED IN SOURCE, PHYSICAL VALIDATION OPEN

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
- p99.9 exposed only with at least **1,000 samples in that distribution**;
- capture-integrity provenance (loss/invalid/image-invalid/event-limit);
- bounded protocol-v5 aggregate response;
- capture `RequestId` retained for exported-evidence/log correlation;
- fail-closed client response validation, including distribution ordering, contributor bounds and threshold-count consistency;
- active-console client-session authorization;
- expected capture failures retain structured root-cause provenance.

Interpretation rules:

- `DPC >100 µs` and `ISR >25 µs` are Microsoft driver guidance;
- `>1 ms` and `>3 ms` are LatencyPilot local diagnostic tail buckets, not official Windows pass/fail or user-impact severity boundaries;
- an integrity-warning capture cannot receive a healthy classification merely because threshold counts are low;
- raw counts, p99/max, attribution and capture integrity remain authoritative; color/badges are supplemental interpretation cues.

Still open:

- physical current-Service capture/cleanup validation;
- active-session rejection validation with a second local session where practical;
- attribution plausibility against an independent observer where practical;
- observer overhead profiling if physical evidence shows a meaningful measurement risk.

## 2.3 Baseline quality — IMPLEMENTED IN SOURCE, NOT CLOSED

`baseline-quality-v1` currently uses:

- five requested windows;
- minimum 20 events per metric/window;
- clean capture integrity;
- <=30% relative P10–P90 spread;
- <=20% early/late drift;
- no >50% extreme-window deviation;
- contiguous `WindowNumber` (`1..N`) as the authoritative sequence;
- `StartedAtUtc` as provenance rather than a monotonic sequencing clock.

The consolidated baseline test covers stable, drifted, ETW-loss, gapped-window and backwards-wall-clock scenarios without increasing the permanent test count.

### Quiet repeated-measurement flow

The current WinUI source deliberately reduces observer activity during the five-window baseline sequence:

- the chosen measurement scenario is frozen while capture is busy;
- the UI is given a settle delay before the first capture;
- no full observation card, module list, processor list, health chart or baseline-window list is redrawn between authoritative capture windows;
- only lightweight progress/status text is updated between captures;
- the final observation rendering occurs only after the fifth capture is complete;
- the baseline verdict still uses all five windows, not merely the final displayed observation;
- partial baseline evidence remains exportable after a stopped sequence but cannot become valid merely because it was exported.

### Low-overhead runtime context

Best-effort runtime context is sampled immediately before and after each capture, outside the authoritative ETW window:

- system CPU busy percentage is derived from `GetSystemTimes` cumulative idle/kernel/user deltas;
- AC/DC source, battery state and Battery Saver are captured through `GetSystemPowerStatus`;
- active power-plan GUID/friendly name are captured through `PowerGetActiveScheme` / `PowerReadFriendlyName`;
- power-plan/source/Battery-Saver changes during a capture or across a baseline sequence are surfaced to the user;
- runtime context is exported with observation/baseline evidence;
- failure to collect optional context does **not** invalidate otherwise clean DPC/ISR evidence and is not silently converted into a zero value;
- this context is provenance, not a new baseline pass/fail gate.

This is source-level observer-noise/context hardening, not proof that UI overhead is negligible. Physical profiling/validation remains authoritative.

Still open:

- owner-local/physical five-window execution through the real Service;
- physical idle + controlled-load quality evidence;
- verify that quiet sequencing behaves correctly with the real compositor/workloads;
- verify system-CPU/power-plan context against the physical machine during idle and real-world scenarios;
- add thermal context only if a defensible authoritative low-overhead source is identified and physical evidence shows it is needed;
- future optimizer integration must require `IsValidForComparison` before any mutation/keep recommendation can be enabled.

## 2.4 Evidence UX — IMPLEMENTED IN SOURCE, PHYSICAL VALIDATION OPEN

Implemented in source:

- read-only service/safety status;
- five-second observation;
- counts, p99/max and sample-gated p99.9;
- explicit `Need ≥1k` p99.9 state when fewer than 1,000 samples support that tail percentile, while p99/max remain visible;
- capture-integrity warning state;
- CPU concentration and bounded module contributors;
- latency-health badge plus exact-value tail-rate visualization;
- tail-rate bars are lightweight `Grid`/`Border` visuals rather than `ProgressBar` controls, so they do not expose misleading operation-progress semantics to assistive technology;
- `>1 ms` / `>3 ms` wording explicitly marked as local diagnostic buckets rather than Windows severity thresholds;
- explicit selectable measurement scenarios: **Real-world workload**, **Controlled idle**, **Before / after comparison**;
- scenario-specific guidance tells the user when apps should stay open or be closed;
- scenario changes invalidate stale visible/export evidence so an old capture cannot be presented under a new context;
- scenario card shows best-effort average system CPU busy time, power source, active power plan and Battery Saver context after capture;
- repeated baseline progress/verdict/reasons UI;
- explicit configuration vs assigned-resource vs runtime-evidence wording;
- keyboard accelerators (`Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Ctrl+E`);
- accessibility/high-contrast resources and automation metadata;
- adaptive narrow/wide workspace source;
- manual JSON evidence export;
- evidence schema `latencypilot-evidence-v5`;
- observation/baseline export contains bounded capture aggregates, `RequestId`, protocol/product version, selected scenario, runtime CPU/power context and bounded non-personal environment provenance;
- source revision is recorded from build/assembly source-revision metadata when available;
- unresolved `ulong` routine addresses serialize as hexadecimal strings;
- export serialization/file I/O stays outside authoritative baseline capture windows.

Still open:

- owner-local compile/runtime validation of the current WinUI source;
- verify JSON content against visible observation/baseline and retain SHA-256;
- narrow-window/text-scaling sanity on physical WinUI;
- keyboard focus/screen-reader sanity;
- physical evidence that capture-warning, sample-insufficient p99.9, runtime-context and scenario-change invalidation present correctly;
- remove or simplify any duplicate explanatory surfaces only after the physical UI pass shows they are redundant.

## Permanent critical suite — 8 / 10

Current durable contracts cover:

1. experiment lifecycle transition safety;
2. benchmark verdict/guardrail matrix;
3. repeated baseline quality and sequence behavior;
4. non-finite metric rejection;
5. canonical percentile estimator;
6. fail-closed framing plus protocol-v5 correlation/under-supported p99.9 round-trip behavior;
7. read-only observation command surface;
8. real Windows read-only inventory/topology consistency.

Two slots remain. They are intentionally not pre-reserved. A future parser/recovery/mutation risk should replace or consolidate lower-value scenarios before exceeding the hard cap.

## Stage B — physical Windows 11 observation validation — OPEN

Runbook: `docs/PHYSICAL_VALIDATION.md`.

Required closure evidence includes:

- exact source revision + completed green Tests run for deterministic contracts;
- owner-local App/Service build on Windows 11;
- owner-local published-App launch smoke when release validation is performed;
- protected Service path and LocalSystem/SCM behavior;
- idle + controlled-load observations;
- exported evidence JSON + SHA-256 + RequestId correlation;
- active-console authorization behavior;
- cleanup/recovery/no-stale-ETW-session behavior;
- module/processor plausibility;
- representative inventory/resource evidence;
- explicit zero-mutation evidence;
- uninstall/service-removal evidence where packaging is being validated.

Stage B cannot close from CI/VM evidence alone.

## Stage C — repeated baseline quality — SOURCE COMPLETE, PHYSICAL CLOSURE OPEN

Next ordered work:

1. owner-local compile/run current `main`;
2. execute a five-window **Controlled idle** baseline with the quiet baseline flow;
3. execute a five-window **Real-world workload** baseline under one repeatable workload;
4. preserve complete/partial exported JSON and SHA-256 hashes;
5. confirm Valid/Inconclusive reasons match capture integrity/noise/drift reality;
6. verify runtime system-CPU/power-plan context matches the physical test state and stays stable where expected;
7. inspect whether the App itself measurably perturbs the baseline before adding any deeper observer-overhead machinery.

Stage C closes only when the real App → Service → ETW path distinguishes a trustworthy repeated baseline from an unstable/incomplete run.

## Stage D — Phase 2 evidence UX completion — SOURCE IMPLEMENTED, PHYSICAL UX CLOSURE OPEN

After current owner-local/physical evidence:

1. finish any responsive/text-scaling corrections exposed by real WinUI rendering;
2. finish keyboard focus/screen-reader corrections exposed by physical testing;
3. verify the scenario selector, health interpretation, runtime context and evidence export are understandable without hiding raw numbers;
4. verify evidence export usability/auditability against the visible UI;
5. perform the Phase 2 UX/claim step-back review.

## After Phase 2

Phase 3 begins with the safety substrate, not with a tweak:

1. SQLite durable experiment journal/recovery state;
2. mutation-specific authorization/allowlist extension;
3. Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
4. interruption/reboot recovery;
5. forced-failure rollback;
6. only then the first supported GPU interrupt-affinity/MSI experiment.

No mutation work may bypass Stage B/C/D or the Phase 3 safety substrate.

## Release discipline

- source version: `0.0.2`;
- `v0.0.1` is historical/reserved and never reusable;
- hosted workflow supplies permanent-test evidence only;
- owner-local Windows owns App/Service compile, publish/package and runtime launch-smoke evidence;
- published versions are immutable;
- `main` is the default target for owner-directed automation; do not create/switch branches unless the owner explicitly requests it.

## Hard test rule

Permanent automated tests may not exceed **10** without explicit owner approval plus an ADR explaining why staying within 10 creates greater risk. Temporary implementation/debug tests may be created and removed.

## Completion/reporting discipline

Before closing a subsection, re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints.

Use **implemented but not closed** when physical/runtime/package evidence is still missing. Every progress report must state what completed, its evidence, what remains open, the exact next stage and what follows it.
