# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-13

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current product version: **0.0.1 pre-alpha**
- Current execution stage: **Stage B — physical Windows 11 observation validation**
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service exists for read-only kernel observation; mutation commands do not exist**
- Permanent automated tests: **7 / hard maximum 10**
- Latest green code-validation CI: **run `34743464368`**, commit `6ade8eac29ea9fc6539b3c7fc84867e71963795d`
- Latest `main` after that validation contains documentation-only methodology updates; no code change follows the green code-validation commit.
- Current published prerelease: **`v0.0.1`**, release `387708633`, published before the 2026-09-13 hardening/performance wave.
- Therefore the existing public `v0.0.1` artifacts are historical Stage B candidates, **not** evidence that the current hardened `main` distribution has been physically validated.

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the buildable solution, domain/comparison foundation, non-mutating app vertical slice and focused critical suite.

Historical note: Phase 1 originally closed with WPF. ADR 0002 later superseded the UI choice with WinUI 3. The historical WPF evidence remains valid as historical evidence and is not rewritten.

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
- client disconnect or unexpected extra client data cancels the active capture instead of intentionally leaving detached privileged work running;
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

### Release/CI hardening — IMPLEMENTED

Current CI/release rules:

- CI uses pinned revisions for official GitHub Actions;
- PR validation includes App publish, WinUI PRI check, GUI smoke, Service publish, setup build and portable build;
- release-workflow changes themselves trigger CI validation;
- release publication requires an explicit release request rather than a workflow-file edit;
- release runs use `cancel-in-progress: false`;
- a replacement prerelease/tag is not removed until restore/build/tests/package/upload have succeeded;
- workflow inputs are passed through environment variables before PowerShell consumption rather than direct script interpolation.

The hardened publication flow has **not** been intentionally rerun to replace the public prerelease during this work. CI validates build/package behavior; it does not prove GitHub publication semantics end-to-end.

## Phase 2 — IN PROGRESS

Goal: trustworthy, strictly read-only Windows observation before any system mutation.

### 2.1 Inventory and evidence provenance

Implemented and CI-verified:

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

Implemented and CI-verified:

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

- `KernelLatencyEvent` is now a readonly value type rather than one heap object per captured event;
- module attribution updates the existing capture buffer instead of allocating a second full attributed event array;
- processor/module/unresolved-routine aggregation no longer materializes a full event array per contributor solely to compute duration summaries;
- duration buffers already owned by aggregation are sorted in place rather than copied again;
- canonical percentile semantics remain unchanged.

Evidence:

- value-type event storage commit `7f31cf4ae5a52d12a23c15c4cadaec648a90e455`;
- in-place attribution commit `fe47c57b186ac03c455ad7a15c34a7bd92a33b24`;
- reduced aggregation materialization commit `e3cab4f30b9c95d8d3163d84168eed93457e95bf`;
- in-place duration-buffer sorting commit `6ade8eac29ea9fc6539b3c7fc84867e71963795d`;
- full green CI run `34743464368` on `6ade8eac...`.

This is a scoped allocation correction, **not** a claim that observer overhead is now negligible. Deeper pooling/ref/streaming work requires profiler/physical evidence because additional value-type copying or aggregation complexity can create new trade-offs. `docs/BENCHMARK_METHODOLOGY.md` now treats observer overhead as a measurement-validity concern.

### 2.4 Baseline quality — NOT STARTED

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

### 2.5 UX — PARTIAL

Implemented:

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

Still required:

- [ ] inspectable raw/auditable aggregates beyond the compact summary;
- [ ] repeated-baseline/noise/drift UX;
- [ ] baseline quality verdict/reason;
- [ ] clear evidence-level labels for stored configuration vs assigned resource vs runtime behavior;
- [ ] responsive/keyboard/accessibility sanity pass for the Phase 2 evidence surfaces.

## Permanent critical suite — 7 / 10

The permanent suite was consolidated so test count does not grow with every historical bug.

Current durable contracts cover:

1. experiment lifecycle rejects illegal transitions;
2. benchmark verdict matrix preserves primary/guardrail semantics;
3. non-finite measurements are rejected;
4. one documented percentile estimator is authoritative;
5. pipe framing fails closed on malformed/unknown/oversized/truncated input;
6. observation protocol command surface remains read-only;
7. real Windows read-only inventory/topology capture remains internally coherent.

Three slots remain below the hard cap. They are not pre-reserved; future recovery/mutation/parser risks may replace or consolidate lower-value tests.

## Stage A — authoritative module/driver attribution — CLOSED

Stage A remains closed in CI. Historical closure evidence includes:

- attribution implementation commit `6d63e1b2c12eaa0081d8babd846f8ca2cc7776b8`;
- TraceEvent compatibility correction `4e83ab9d0c460828dce4cead22aa47858b9708b9`;
- event-limit rundown correction `c58cd5fa9c15debb4541a7e49d550a950a14dc2d`;
- Stage A CI run `34713147459`.

Physical plausibility validation remains part of Stage B rather than retroactively reopening Stage A implementation work.

## Stage B — physical Windows 11 observation validation — CURRENT

Runbook: `docs/PHYSICAL_VALIDATION.md`.

Existing local evidence from 2026-09-12:

- installed self-contained Service + non-elevated Debug App produced three clean five-second observations;
- one observation succeeded after intentional Service stop/start recovery;
- UI showed processor concentration, bounded module contributors and 100% resolved attribution on that host.

That evidence is useful but does **not** close Stage B.

### Stage B blockers now

1. Produce an explicit packaged prerelease from the **current hardened `main`** before treating package-level physical validation as current evidence. The existing public `v0.0.1` predates this hardening wave.
2. Verify the selected setup/portable checksum and `BUILD_INFO.txt` commit.
3. Install/register the protected-path Service and launch the App non-elevated.
4. Capture representative idle and controlled-load observations.
5. Compare representative module/driver attribution against PerfView or LatencyMon where practical.
6. Exercise App disconnect, Service stop/start and recovery; verify no stale `LatencyPilot-Kernel-*` ETW session remains.
7. Record explicit zero-mutation evidence for interrupt affinity/MSI/CPU Sets/power/network/device policy.
8. Validate representative topology, stored interrupt configuration and allocated-resource evidence on GPU/xHCI/NIC devices.
9. Where suitable hardware exists, capture multi-processor-group evidence.
10. Profile LatencyPilot observer allocation/GC/CPU overhead only as needed to determine whether deeper capture-path optimization is justified.

**Stage B closes only when:** the current packaged build produces usable read-only observations on physical Windows 11, attribution is plausible against trusted external evidence, inventory/resource evidence is coherent, cleanup/privilege/zero-mutation boundaries hold, and no observer-overhead issue invalidates the measurements.

**Immediately after Stage B:** Stage C repeated baseline and quality engine.

## Stage C — repeated baseline and quality engine

Work in order:

1. define repeated-window capture protocol;
2. collect multiple valid windows;
3. compute per-metric variability/noise floor;
4. detect drift between windows;
5. define invalid/inconclusive baseline reasons;
6. add background/thermal quality signals only where evidence is reliable;
7. prevent optimization/recommendation when the baseline quality gate fails;
8. expose quality verdict and reasons in WinUI.

**Stage C closes only when:** LatencyPilot can distinguish a trustworthy repeated baseline from a single noisy observation.

**Immediately after Stage C:** Stage D Phase 2 evidence UX completion.

## Stage D — Phase 2 evidence UX completion

Work in order:

1. raw/auditable aggregate inspection;
2. evidence-level labels for configuration, allocated resources and runtime ETW;
3. baseline quality verdict/reasons and invalid-state presentation;
4. responsive/keyboard/accessibility sanity pass for the Phase 2 surfaces.

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
- current source version is `0.0.1` in `Directory.Build.props`;
- `RELEASE_VERSION` is an explicit release request and must equal the source version;
- release publication is explicit; changing release workflow code alone does not publish a release;
- release workflow reruns restore/build/**7-test** gate and publishes both App and Service;
- release replacement is staged so existing prerelease/tag removal occurs only after new artifacts exist;
- CI validates setup and portable packaging on both main pushes and pull requests where relevant;
- daily development uses Debug + Visual Studio Hot Reload or `dev.ps1`; publish/setup are validation/release gates, not the normal edit loop;
- every release bundle contains version/build metadata and validation/diagnostics documentation;
- every published setup/portable asset has a SHA-256 companion;
- `0.0.x` releases remain prereleases until later exit gates justify stable semantics.

## Hard test rule

Permanent automated tests may not exceed **10**. Current count: **7**. Temporary implementation/debug tests may be created and removed. Exceeding 10 requires explicit owner approval plus an ADR explaining why staying within 10 creates greater risk.

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
