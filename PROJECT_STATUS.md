# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-13

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current product version: **0.0.2 pre-alpha**
- Current execution stage: **Stage C baseline-quality implementation is underway; Stage B physical validation remains open**
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service exists for read-only kernel observation; mutation commands do not exist**
- Permanent automated tests: **8 / hard maximum 10**
- GitHub Actions policy: **Tests only**. Hosted Actions must not build/publish App, Service, Setup, portable distributions or releases.
- Stage C deterministic test evidence: **Tests run `34744320630` succeeded** on commit `2070bbb6b55465e71d569a12930a298318259a2d` with the eight-test suite.
- App/WinUI Stage C source is **implemented but not closed** because owner-local Windows compile/run evidence is still required.
- `v0.0.1` was historically published but was removed during a failed cloud replacement attempt; it remains a permanently reserved historical version and must not be reused.
- The latest prerelease currently present on GitHub Releases is **`v0.0.0`**. The next publishable candidate from current source is **`v0.0.2`**, after exact-commit green Tests plus owner-local build/package validation.

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the buildable solution, domain/comparison foundation, non-mutating app vertical slice and focused critical suite.

Historical note: Phase 1 originally closed with WPF. ADR 0002 later superseded the UI choice with WinUI 3. Historical Phase 1 build/CI evidence remains historical evidence and is not rewritten by the current test-only CI policy.

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

Current implementation:

- Windows Service hosts privileged kernel observation while WinUI remains non-elevated;
- local typed/versioned Named Pipe transport;
- Phase 2 commands are allowlisted to service status and kernel-latency observation;
- generic shell/registry/process execution primitives do not exist;
- `ServiceBoundary.MutationAvailable` remains `false`;
- request/response frames are bounded and unknown JSON members fail closed;
- network identities are denied by pipe ACL;
- Phase 2 observation access is limited to interactive local identities plus required service identities rather than all authenticated users;
- client disconnect or unexpected extra client data cancels active capture work instead of intentionally leaving detached privileged work running;
- client/server deadlines remain bounded;
- the current Phase 2 ACL is explicitly **not** future mutation authorization.

### Structured diagnostics — IMPLEMENTED

Contract: `docs/DIAGNOSTICS.md`.

Implemented:

- bounded compact-JSON App logs under `%LOCALAPPDATA%\LatencyPilot\Logs\App`;
- bounded compact-JSON Service logs under `%PROGRAMDATA%\LatencyPilot\Logs\Service`;
- daily/size rolling and retained-file limits;
- asynchronous non-blocking file sink configuration;
- App → IPC → Service correlation through protocol `RequestId`;
- stable Service EventIds for capture lifecycle and protocol failures;
- logging initialization failure does not prevent App/Service startup;
- raw per-event DPC/ISR logging is prohibited from the measurement hot path.

### Portable privileged-path hardening — IMPLEMENTED

`Install-Service.ps1` no longer registers LocalSystem against an ordinary portable extraction path.

Current behavior:

- portable App may remain in a user-controlled extracted directory;
- elevated service install copies the Service payload to `%ProgramFiles%\LatencyPilot\Service`;
- Service registration explicitly uses `LocalSystem` and the protected path;
- normal installer use detects that source/destination are already the same protected directory and avoids copying onto itself;
- uninstall removes both service registration and the protected managed Service copy.

### Local development and release ownership — IMPLEMENTED

Current automation contract:

- GitHub Actions contains a **Tests** workflow only;
- the Tests workflow runs the permanent critical suite for every `main` revision and pull request;
- hosted App/Service builds, WinUI publish, GUI smoke, Setup/portable creation, artifact upload and cloud release publication were removed;
- normal UI development uses Debug + Visual Studio Hot Reload or `dev.ps1`;
- `scripts/Publish-Release.ps1` is the explicit owner-run local Windows release path;
- the local publisher requires a clean/up-to-date `main` and green Tests evidence for the exact commit before local build/package/publication;
- the owner-local publisher verifies the WinUI PRI and launch-smoke-tests the published App before packaging/publication;
- successful package metadata records the matching Tests run and `app_launch_smoke=passed`;
- published semantic versions are immutable; the publisher has no delete/replace mode and refuses an existing release/tag;
- release creation uses GitHub CLI `gh release create --target <exact-commit>` so a missing tag is created at the exact tested commit without a separate tag push;
- prereleases are explicitly published with `--latest=false`;
- `docs/RELEASING.md` documents the owner-run flow.

This separation is intentional: test CI supplies automated correctness evidence; build/package evidence belongs to the owner's Windows machine.

Historical release incident: cloud release run `34743717696` successfully completed restore/build/tests/App publish/PRI/GUI smoke/Service publish/Setup/portable/artifact upload, then removed the previous `v0.0.1` prerelease/tag and failed to push the replacement tag because the GitHub App token lacked workflow-update permission. Cloud release publication was subsequently removed. `v0.0.1` remains reserved rather than being reused.

## Phase 2 — IN PROGRESS

Goal: trustworthy, strictly read-only Windows observation before any system mutation.

### 2.1 Inventory and evidence provenance

Implemented and automated-test/previous-local validated where applicable:

- [x] processor-group-aware package/core/logical processor topology implementation;
- [x] stable present PnP device instance IDs through SetupAPI;
- [x] driver provider/version/INF metadata through unified device properties;
- [x] stored interrupt configuration inspection (`MSISupported`, message limit, affinity policy/mask where present);
- [x] interrupt-configuration availability provenance;
- [x] allocated IRQ/resource assignment reader through Configuration Manager `ALLOC_LOG_CONF`;
- [x] optional property/resource failures degrade individual devices to partial evidence where safe instead of invalidating the entire inventory;
- [x] explicit read-failure provenance for optional interrupt configuration/resource paths.

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

Implemented:

- [x] controlled kernel ETW session creation with LatencyPilot-owned collision-resistant session names;
- [x] DPC and ISR collection;
- [x] ETW processor-number attribution and per-processor aggregation;
- [x] canonical p50/p95/p99/p99.9/max distributions using `LatencyPilot.Benchmarking.Statistics.Percentiles`;
- [x] event-loss / invalid-event / event-limit provenance;
- [x] cancellation, timeout and cleanup paths;
- [x] privileged Service execution with non-elevated WinUI client;
- [x] bounded typed IPC response rather than transferring the raw event set to the UI;
- [x] authoritative module/driver attribution from kernel image ranges;
- [x] stop-time kernel image rundown for images already loaded before the observation;
- [x] image lifetime handling for load/unload/address reuse;
- [x] ambiguous/invalid/missing mappings remain unresolved rather than guessed;
- [x] bounded module contributor and unresolved-routine aggregates;
- [x] fail-closed protocol parsing and response validation;
- [x] active capture cancellation when the client disconnects or violates the one-request connection contract;
- [x] structured lifecycle/error diagnostics correlated by `RequestId`.

Still required before 2.2 can close:

- [ ] physical validation of current hardened Service → ETW → IPC capture and cleanup;
- [ ] physical validation of module attribution against a trusted external observer where practical;
- [ ] physical validation of per-processor attribution, including explicit multi-processor-group evidence if suitable hardware is available.

### 2.3 Observer-overhead reduction — IMPLEMENTED, PHYSICAL PROFILING STILL OPEN

The 2026-09-13 hardening wave reduced avoidable observer allocation without changing event/statistical semantics:

- `KernelLatencyEvent` is a readonly value type rather than one heap object per captured event;
- module attribution updates the existing capture buffer instead of allocating a second full attributed event array;
- processor/module/unresolved-routine aggregation no longer materializes a full event array per contributor solely to compute duration summaries;
- duration buffers already owned by aggregation are sorted in place rather than copied again;
- canonical percentile semantics remain unchanged.

Evidence:

- value-type event storage commit `7f31cf4ae5a52d12a23c15c4cadaec648a90e455`;
- in-place attribution commit `fe47c57b186ac03c455ad7a15c34a7bd92a33b24`;
- reduced aggregation materialization commit `e3cab4f30b9c95d8d3163d84168eed93457e95bf`;
- in-place duration-buffer sorting commit `6ade8eac29ea9fc6539b3c7fc84867e71963795d`.

This is a scoped allocation correction, **not** a claim that observer overhead is negligible. Deeper pooling/ref/streaming work still requires profiler/physical evidence.

### 2.4 Baseline quality — IMPLEMENTED IN SOURCE, NOT CLOSED

A single five-second capture remains an **observation**, not a baseline.

Completed now in deterministic source:

- `LatencyPilot.Benchmarking.Baselines.BaselineQualityAnalyzer` owns repeated-baseline interpretation;
- method version is `baseline-quality-v1`;
- five required windows are the current App capture policy;
- a metric needs at least 20 events per clean window and a finite positive p99;
- relative noise floor is `(P90 - P10) / |median|` across window-level p99 values;
- noise above 30% is inconclusive;
- early/late relative median drift above 20% is inconclusive;
- >50% per-window deviation from the median is explicitly reported as extreme and never silently deleted;
- unavailable/non-zero ETW loss, invalid latency/image events or event-limit hit invalidate a capture window;
- overall result is only `Valid` when all required windows and both DPC/ISR p99 metric gates pass; otherwise it is `Inconclusive` with reasons.

Important bug fixed during the Stage C review:

- the previous single-observation UI considered any *known* ETW loss count clean, even when `EventsLost > 0`;
- current source now requires zero loss and reports non-zero loss as a capture-integrity failure;
- a lossy window cannot qualify for the repeated baseline.

Automated evidence:

- one new high-blast-radius permanent test consolidates stable, drifted and ETW-loss baseline scenarios;
- Tests run `34744320630` passed on commit `2070bbb6b55465e71d569a12930a298318259a2d`;
- permanent suite is now 8/10.

Still open before baseline quality can close:

- [ ] owner-local Windows compile of the current WinUI App source;
- [ ] owner-local execution of the five-window baseline flow through the real Service;
- [ ] physical evidence that quality output behaves sensibly across idle/controlled-load runs;
- [ ] authoritative low-overhead background-load warning if a defensible signal is chosen;
- [ ] authoritative low-overhead thermal/power-state warning if a defensible signal is chosen;
- [ ] future optimizer/recommendation integration must explicitly require `IsValidForComparison`; no optimizer currently exists to wire this gate into.

### 2.5 UX — PARTIAL

Implemented and previously validated:

- [x] WinUI service-health status;
- [x] explicit read-only safety boundary;
- [x] product version display;
- [x] five-second kernel observation through the Service;
- [x] DPC/ISR event counts and p99/p99.9 display;
- [x] ETW loss/invalid/limit-reached status;
- [x] processor concentration view/ranking;
- [x] resolved/unresolved module-attribution coverage;
- [x] bounded top module contributor view;
- [x] no single capture is mislabeled as a trustworthy baseline.

Implemented in source but awaiting owner-local WinUI build/run evidence:

- five-window `Build baseline` action;
- progress and per-window integrity/DPC/ISR evidence rows;
- baseline `Valid` / `Inconclusive` quality verdict;
- explicit noise, drift, sample-adequacy and integrity reasons;
- explicit dashboard label separating stored interrupt configuration, allocated IRQ/resource assignment and runtime DPC/ISR evidence;
- keyboard accelerators for refresh (`Ctrl+R`), single observation (`Ctrl+O`) and repeated baseline (`Ctrl+B`);
- UI Automation names for capture progress, module coverage and evidence lists;
- version-specific safety text removed so the read-only boundary does not drift when product version changes.

Still required:

- [ ] inspectable raw/auditable aggregates beyond the compact summary;
- [ ] owner-local validation of the new repeated-baseline and keyboard/accessibility source;
- [ ] responsive-layout implementation and narrow-window/text-scaling sanity pass;
- [ ] final screen-reader/focus-order sanity pass on physical WinUI after the current source compiles/runs locally.

## Permanent critical suite — 8 / 10

Current durable contracts cover:

1. experiment lifecycle rejects illegal transitions;
2. benchmark verdict matrix preserves primary/guardrail semantics;
3. repeated baseline quality gate rejects drift/capture-integrity failure and accepts stable evidence;
4. non-finite measurements are rejected;
5. one documented percentile estimator is authoritative;
6. pipe framing fails closed on malformed/unknown/oversized/truncated input;
7. observation protocol command surface remains read-only;
8. real Windows read-only inventory/topology capture remains internally coherent.

Two slots remain below the hard cap. They are intentionally not pre-reserved; later recovery/mutation/parser risks may replace or consolidate lower-value tests.

## Stage A — authoritative module/driver attribution — CLOSED

Historical closure evidence includes:

- attribution implementation commit `6d63e1b2c12eaa0081d8babd846f8ca2cc7776b8`;
- TraceEvent compatibility correction `4e83ab9d0c460828dce4cead22aa47858b9708b9`;
- event-limit rundown correction `c58cd5fa9c15debb4541a7e49d550a950a14dc2d`;
- Stage A CI run `34713147459`.

Physical plausibility validation remains part of Stage B rather than retroactively reopening Stage A implementation work.

## Stage B — physical Windows 11 observation validation — OPEN

Runbook: `docs/PHYSICAL_VALIDATION.md`.

Existing local evidence from 2026-09-12:

- installed self-contained Service + non-elevated Debug App produced three clean five-second observations;
- one observation succeeded after intentional Service stop/start recovery;
- UI showed processor concentration, bounded module contributors and 100% resolved attribution on that host.

That evidence is useful but does **not** close Stage B for current `main`.

### Stage B blockers now

1. Owner-local build/package the current tested `main` when a new physical-validation artifact is needed; cloud build/release is intentionally absent.
2. Verify selected setup/portable checksum and `BUILD_INFO.txt` commit for packaged validation.
3. Install/register the protected-path Service and launch the App non-elevated.
4. Capture representative idle and controlled-load observations.
5. Compare representative module/driver attribution against PerfView or LatencyMon where practical.
6. Exercise App disconnect, Service stop/start and recovery; verify no stale `LatencyPilot-Kernel-*` ETW session remains.
7. Record explicit zero-mutation evidence for interrupt affinity/MSI/CPU Sets/power/network/device policy.
8. Validate representative topology, stored interrupt configuration and allocated-resource evidence on GPU/xHCI/NIC devices.
9. Where suitable hardware exists, capture multi-processor-group evidence.
10. Profile LatencyPilot observer allocation/GC/CPU overhead only as needed to decide whether deeper capture-path optimization is justified.

**Stage B closes only when:** the current locally built/package-identified source produces usable read-only observations on physical Windows 11, attribution is plausible against trusted external evidence, inventory/resource evidence is coherent, cleanup/privilege/zero-mutation boundaries hold, and no observer-overhead issue invalidates the measurements.

## Stage C — repeated baseline and quality engine — IMPLEMENTED IN SOURCE, NOT CLOSED

Implementation order completed:

1. repeated-window policy defined;
2. five-window App orchestration implemented;
3. per-metric variability/noise floor implemented;
4. drift detection implemented;
5. explicit invalid/inconclusive reasons implemented;
6. quality verdict/reasons UI source implemented;
7. deterministic high-blast-radius test added without exceeding the cap.

Still open:

1. owner-local WinUI compile/run;
2. physical repeated-baseline evidence;
3. reliable background/thermal quality signals where supportable;
4. explicit optimizer/recommendation gating when that subsystem exists.

**Stage C closes only when:** LatencyPilot can distinguish a trustworthy repeated baseline from a single noisy observation on the actual Windows App/Service path, not merely in deterministic tests.

**Immediately after Stage C:** Stage D Phase 2 evidence UX completion.

## Stage D — Phase 2 evidence UX completion

Work in order:

1. raw/auditable aggregate inspection;
2. owner-local verification of evidence-level labels and baseline invalid-state presentation;
3. responsive narrow-window/text-scaling implementation;
4. final keyboard/focus/screen-reader accessibility sanity pass.

**Stage D closes only when:** all remaining Phase 2 UX requirements are met and the Phase 2 exit gate can be exercised on physical Windows 11.

**Immediately after Stage D:** Phase 2 closure review, then Phase 3.

## Stage E — Phase 3 safety substrate, before first mutation

The Service and typed Named Pipe already exist; Phase 3 does **not** recreate them.

Work order:

1. durable SQLite experiment journal/recovery schema;
2. explicit mutation-specific authorization/allowlist extension;
3. Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
4. reboot-required and recovery-required states;
5. forced-failure rollback path;
6. only then the first supported GPU interrupt-affinity/MSI experiment.

No mutation work may jump ahead of Stage C/D or the Phase 3 safety substrate.

## Release discipline

- product versions are exactly `MAJOR.MINOR.PATCH`;
- current source version is `0.0.2` in `Directory.Build.props`;
- `RELEASE_VERSION` is an explicit local release request and must equal the source version;
- `v0.0.1` is historically published/reserved and must never be reused even though the release/tag is currently absent;
- GitHub Actions is test-only and runs the permanent critical suite;
- App/Service Release build, WinUI publish/PRI validation, published-App launch smoke, Setup and portable creation are owner-local Windows responsibilities;
- `scripts/Publish-Release.ps1` is the explicit owner-run publisher and requires green Tests evidence for the exact `main` commit;
- the publisher refuses existing tags/releases, does not replace/delete published versions, and creates a missing tag through `gh release create --target <exact-commit>`;
- prereleases are explicitly published with `--latest=false`;
- `docs/RELEASING.md` is the publication contract;
- daily development uses Debug + Visual Studio Hot Reload or `dev.ps1`;
- published distributions contain version/build metadata, launch-smoke provenance and SHA-256 companions;
- `0.0.x` releases remain prereleases until later exit gates justify stable semantics.

## Hard test rule

Permanent automated tests may not exceed **10**. Current count: **8**. Temporary implementation/debug tests may be created and removed. Exceeding 10 requires explicit owner approval plus an ADR explaining why staying within 10 creates greater risk.

## Owner-directed automation branch rule

For owner-directed automation work, changes go directly to **`main`** by default. Do not create or switch to a new branch unless the repository owner explicitly requests it. Normal contributor PR workflows remain unaffected.

## Completion and reporting discipline

Before closing a subsection, perform the mandatory step-back review from `AGENTS.md`: re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints.

Every progress report must state:

- what completed now;
- evidence;
- what remains open;
- the exact next stage and its ordered substeps;
- what follows that next stage.

If required evidence is missing, use **implemented but not closed**, not “done”.
