# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-12

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current product version: **0.0.1 pre-alpha**
- Current execution stage: **Stage B — physical Windows 11 observation validation**
- Current published release: **`v0.0.1` GitHub prerelease**, release `387708633`
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service exists for read-only kernel observation; mutation commands do not exist**
- Permanent automated tests: **9 / hard maximum 10**
- Latest green CI: **run `34719399094`**, commit `14bb77a883ba6671dd479b88278f9a2c849465f7`
- Release workflow evidence: **run `34719732367` succeeded**, including version validation, stale-tag preflight, restore, Release build, 9/9 tests, App publish, GUI smoke, Service publish, setup/portable creation, artifact upload and GitHub prerelease publication.
- Published setup: **`LatencyPilot-0.0.1-win-x64-setup.exe`**, 97,848,937 bytes, GitHub SHA-256 `affd6b35c533bd321af26b03e1e8a9afbb6961dc6f5ffcf38c5309201237295d`.
- Published portable bundle: **`LatencyPilot-0.0.1-win-x64-portable.zip`**, 149,896,015 bytes, GitHub SHA-256 `5ef3fcaf1a3b0f70b06ee7ae422f18f9c19272a6abb3382b124546313651eaaf`.

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

### Local WinUI development loop — COMPLETE

Implemented:

- Visual Studio launch profile `LatencyPilot.App (Hot Reload)` with `hotReloadEnabled: true`;
- native debugging disabled for the Hot Reload profile so managed WinUI Hot Reload is not mixed with native debugging;
- `HotReloadAutoRestart` enabled only for Debug builds;
- root `dev.ps1` supports cached restore, incremental Debug build/run and `dotnet watch` without forcing restore on every edit cycle;
- `GenerateLatencyPilotIcon` is incremental and no longer reruns when its source script/output are unchanged;
- release/publish/install workflows remain separate from the daily Debug inner loop;
- no permanent test was added; suite remains 9/10.

Evidence:

- implementation commit `14bb77a883ba6671dd479b88278f9a2c849465f7`;
- CI run `34719399094` succeeded through Release build, 9/9 tests, WinUI publish-resource validation, GUI smoke, setup/portable packaging and artifact upload;
- release workflow stale-tag null handling corrected in commit `7222add0675b773d9103ec77961179210c703bda` after failed release run `34719622420`; replacement release run `34719732367` succeeded end-to-end.

### Privileged observation service — IMPLEMENTED FOR PHASE 2

Decision: `docs/adr/0003-privileged-observation-service.md`.

Implemented:

- Windows Service hosts privileged kernel observation while WinUI remains non-elevated;
- local Named Pipe transport;
- versioned typed protocol; attribution expansion moved the observation contract to protocol v2;
- allowlisted Phase 2 commands only: service status and kernel-latency observation;
- generic command-name/shell/registry/process primitives do not exist;
- service mutation capability remains `false`;
- App and Service are both published in the Windows x64 artifact;
- client and server I/O are deadline-bounded so an idle/hung connection cannot wait forever.

Evidence:

- service/IPC foundation: run `34709891520`;
- UI → Service → ETW vertical slice: run `34710054683`;
- bounded client/server IPC deadlines: run `34710502158`;
- observation naming + p99.9 UI: run `34710672075`;
- authoritative module-attribution Stage A closure: run `34713147459`;
- first packaged prerelease: release run `34713425026`, tag `v0.0.0`;
- current Stage B prerelease: release run `34719732367`, tag `v0.0.1`.

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
- [x] bounded typed IPC response rather than transferring the raw event set to the UI;
- [x] native module/driver attribution for DPC/ISR routine addresses using authoritative image ranges;
- [x] stop-time kernel image rundown for modules already loaded before the observation;
- [x] image lifetime handling for load/unload and address-range reuse;
- [x] ambiguous, invalid or missing image mappings remain unresolved rather than guessed;
- [x] bounded module contributor and unresolved-routine aggregates through Protocol/Service;
- [x] event-limit path stops the ETW session on a separate thread so final rundown is not discarded by early consumer termination.

Still required before 2.2 can close:

- [ ] physical Windows 11 validation of Service → ETW → IPC capture and cleanup;
- [ ] physical validation of module attribution against a trusted external observer where practical;
- [ ] physical validation of per-processor attribution, including explicit multi-processor-group evidence if hardware is available.

Context7 re-check on 2026-09-12 confirmed the general diagnostic-session requirement to stop a trace cleanly so end-of-session rundown/metadata can be delivered. The exact pinned `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.6 source was then used for ETW-specific API names and semantics. `TraceEventSession.Stop()` documents that it may be called from one thread while `Process()` runs on another, which is the basis for the event-limit stop path.

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
- [x] product version display;
- [x] five-second kernel **observation** action through the Service;
- [x] DPC/ISR event counts;
- [x] p99 and p99.9 display;
- [x] ETW loss/invalid/limit-reached status;
- [x] count of processors observed;
- [x] resolved/unresolved module-attribution coverage;
- [x] compact top resolved module summary;
- [x] no single capture is mislabeled as a trustworthy baseline.

Still required:

- [x] per-processor concentration view/ranking;
- [x] bounded top DPC/ISR module contributor view rather than only the compact top-module summary;
- [ ] inspectable raw/auditable aggregates beyond the compact summary;
- [ ] repeated-baseline/noise/drift UX;
- [ ] baseline quality verdict/reason;
- [ ] clear evidence-level labels for stored configuration vs assigned resource vs runtime behavior.

## Stage A — authoritative module/driver attribution — CLOSED

Completed:

1. kernel image mapping is captured alongside DPC/ISR observations;
2. stop-time `ImageDCStop` rundown supplies already-loaded image mappings;
3. ranges use image base + validated positive image size with overflow rejection;
4. routine addresses resolve only inside active image ranges at the event timestamp;
5. overlapping active ranges with conflicting identities resolve to unknown;
6. unloads close image lifetimes and conservative handling prevents guessed identity after uncertain unload evidence;
7. protocol v2 returns bounded module contributors and bounded unresolved routine contributors;
8. UI exposes attribution coverage and the top resolved module without pretending unresolved data is resolved;
9. event-limit, timeout and cancellation all stop the ETW session rather than relying on consumer termination for cleanup/rundown;
10. Release CI is green with the permanent suite still at 9/10.

Evidence:

- attribution implementation commit `6d63e1b2c12eaa0081d8babd846f8ca2cc7776b8`;
- TraceEvent 3.2.6 compatibility correction commit `4e83ab9d0c460828dce4cead22aa47858b9708b9`;
- UI analyzer correction commit `839c5db6fd7dfc0385926e553b00a9985a5f4932`;
- event-limit rundown correction commit `c58cd5fa9c15debb4541a7e49d550a950a14dc2d`;
- final Stage A CI run `34713147459` succeeded with 9/9 tests and complete Windows x64 packaging.

Step-back result: **closed in CI, not physically validated.** The physical-validation requirement intentionally moves to Stage B rather than being mislabeled as Stage A evidence.

## Stage B — physical Windows 11 observation validation — CURRENT

Runbook: `docs/PHYSICAL_VALIDATION.md`.

Release prerequisite completed:

- [x] `v0.0.1` prerelease published with offline setup EXE, portable ZIP and SHA-256 companions;
- [x] release workflow independently repeated version validation, restore, Release build, 9/9 tests, App publish, GUI smoke, Service publish and both distribution builds;
- [x] portable bundle contains `VERSION.txt`, `BUILD_INFO.txt` and the Stage B validation runbook;
- [x] post-`v0.0.0` WinUI publish-resource/GUI-smoke corrections are included in the Stage B release candidate.

Physical work in order:

1. download `v0.0.1` on the target Windows 11 x64 machine and verify the SHA-256 companion for the selected setup/portable asset;
2. confirm `VERSION.txt` and `BUILD_INFO.txt` identify the exact release and commit when using the portable bundle;
3. install the read-only observation Service through the setup, or register it from the extracted portable bundle;
4. launch the WinUI App as a normal non-elevated user and verify Service connectivity;
5. capture representative idle and controlled-load observations;
6. verify DPC/ISR events, processor distribution and module attribution are plausible and internally consistent;
7. compare representative driver/module findings against PerfView or LatencyMon where practical;
8. force client disconnect/service shutdown/recovery and verify ETW/session cleanup;
9. verify zero-mutation behavior: interrupt affinity/MSI/CPU Sets/power/network/device policy remain untouched;
10. record machine/Windows/CPU/device/driver context with all validation evidence;
11. validate representative topology, allocated resource and stored interrupt-configuration evidence from Phase 2.1.

Local Windows 11 validation completed on 2026-09-12 against the installed self-contained
Service and non-elevated Debug App: Service → ETW → IPC produced three clean five-second
observations, including one after an intentional Service stop/start recovery. The UI showed
processor concentration, bounded module contributors and 100% resolved attribution on this
host. This is useful host evidence, but does not close Stage B: trusted external-observer
comparison, explicit zero-mutation evidence and broader physical validation remain required.

**Stage B closes only when:** a physical Windows 11 x64 machine produces usable read-only observations, attribution is plausible against trusted external evidence, inventory/resource evidence is coherent, and cleanup/privilege/zero-mutation boundaries hold under failure paths.

**Immediately after Stage B:** Stage C repeated baseline and quality engine.

## Stage C — repeated baseline and quality engine

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

## Stage D — Phase 2 evidence UX completion

Work in order:

1. per-processor concentration/ranking view;
2. full top DPC/ISR module contributor view;
3. raw/auditable aggregate inspection;
4. evidence-level labels for configuration, allocated resources and runtime ETW;
5. baseline quality verdict/reasons and invalid-state presentation;
6. responsive/keyboard/accessibility sanity pass for the Phase 2 surfaces.

**Stage D closes only when:** all remaining 2.4 requirements are met and the Phase 2 exit gate can be exercised on physical Windows 11.

**Immediately after Stage D:** Phase 2 closure review, then Phase 3.

## Stage E — Phase 3 safety substrate, before the first mutation

The Service and typed Named Pipe already exist; Phase 3 does **not** recreate them.

Next work will be:

1. durable SQLite experiment journal/recovery schema;
2. explicit mutation-specific authorization/allowlist extension;
3. Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
4. reboot-required and recovery-required states;
5. forced-failure rollback path;
6. only then the first supported GPU interrupt-affinity/MSI experiment.

No mutation work is allowed to jump ahead of Stage C/D or the Phase 3 safety substrate.

## Release discipline

- product versions are exactly `MAJOR.MINOR.PATCH`;
- current source version is `0.0.1` in `Directory.Build.props`;
- `RELEASE_VERSION` is an explicit release request and must equal the source version;
- release workflow reruns restore/build/9-test gate and publishes both App and Service;
- daily development uses Debug + Visual Studio Hot Reload or `dev.ps1`; publish/setup are release gates, not the normal edit loop;
- every release bundle contains `VERSION.txt`, `BUILD_INFO.txt` and the Stage B validation runbook;
- every published setup/portable asset has a SHA-256 checksum companion;
- `v0.0.1` is the current prerelease for Stage B physical validation;
- `0.0.x` releases remain GitHub prereleases until later exit gates justify stable semantics.

## Comparison-core correction discovered during earlier step-back review

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
