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
- Permanent automated tests: **8 / hard maximum 10**
- Current stage: **Stage B physical validation open; Stage C source implemented but physical/runtime closure open; Stage D evidence UX partially implemented**

## Current automated evidence

The hosted **Tests** workflow is a validation-only gate. It now performs:

```text
8 permanent critical tests
→ Release compile LatencyPilot.Service
→ Release compile LatencyPilot.App
```

It does **not** publish the App/Service, run the published-App launch smoke, build Setup/portable distributions or publish GitHub releases.

Latest fully green code-affecting evidence before this documentation-sync wave:

- commit `94b33908ee22ebf8c343c6b227e3e701dc97bd78`;
- workflow run `34751602266`;
- eight critical tests passed;
- Service Release compile passed;
- WinUI App Release compile passed.

Documentation-only commits after that run do not replace the need for a green exact-HEAD validation run; final reporting must use the newest completed run for the final `main` revision.

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
- RDP/multi-session observation is **not** implied by the current Phase 2 rule;
- disconnect/protocol activity cancels active capture work;
- deadlines remain bounded;
- current observation authorization is explicitly not future mutation authorization.

### Structured diagnostics

Contract: `docs/DIAGNOSTICS.md`.

- App CLEF/compact-JSON logs under `%LOCALAPPDATA%\LatencyPilot\Logs\App`;
- Service logs under `%PROGRAMDATA%\LatencyPilot\Logs\Service`;
- bounded rolling/retention;
- async non-blocking file sinks;
- App → IPC → Service correlation through `RequestId`;
- protocol-v5 capture evidence preserves the same `RequestId`;
- expected capture-unavailable paths log failure kind/native error through EventId `1006`;
- rejected non-active client sessions log EventId `1007`;
- raw per-event DPC/ISR logging remains prohibited from the measurement hot path.

### Protected Service installation

- portable App may remain in a user-controlled extracted directory;
- Service payload is copied to `%ProgramFiles%\LatencyPilot\Service` before LocalSystem registration;
- normal installer use avoids copying the protected payload onto itself;
- uninstall removes Service registration and the protected managed Service copy.

### Development/release ownership

- normal development uses Visual Studio/`dev.ps1` or `live.ps1`;
- `live.ps1` keeps the App non-elevated and requests UAC only for protected Service installation/update;
- remote `origin/main` auto-pull in `live.ps1` is **disabled by default** because shared/Service changes can lead to a privileged Service rebuild/reinstall;
- `-AutoPull` is the explicit opt-in;
- `scripts/Publish-Release.ps1` is the owner-run publish/package path;
- release versions are immutable; existing release/tag identities are refused rather than replaced;
- owner-local publication still owns self-contained publish, PRI validation, App launch smoke, Setup/portable and GitHub prerelease publication.

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
- `>1 ms` and `>3 ms` are LatencyPilot local diagnostic tail buckets, **not** official Windows pass/fail or user-impact severity boundaries;
- an integrity-warning capture cannot receive a healthy classification merely because threshold counts are low.

Still open:

- physical current-Service capture/cleanup validation;
- active-session rejection validation with a second local session where practical;
- attribution plausibility against PerfView/LatencyMon where practical;
- observer overhead profiling only if physical evidence shows a meaningful measurement risk.

## 2.3 Baseline quality — IMPLEMENTED IN SOURCE, NOT CLOSED

`baseline-quality-v1` currently uses:

- five requested windows;
- minimum 20 events per metric/window;
- clean capture integrity;
- <=30% relative P10–P90 spread;
- <=20% early/late drift;
- no >50% extreme-window deviation.

Important sequence rule:

- contiguous `WindowNumber` (`1..N`) is authoritative;
- `StartedAtUtc` is provenance, not a monotonic clock;
- an NTP/VM/manual wall-clock adjustment must not crash or invalidate an otherwise contiguous in-process sequence.

The existing consolidated baseline test covers stable, drifted, ETW-loss, gapped-window and backwards-wall-clock scenarios without increasing the permanent test count.

Still open:

- owner-local/physical five-window execution through the real Service;
- physical idle + controlled-load quality evidence;
- authoritative low-overhead background-load and thermal/power warnings if defensible signals are chosen;
- future optimizer integration must require `IsValidForComparison` before any mutation/keep recommendation can be enabled.

## 2.4 Evidence UX — IMPLEMENTED IN SOURCE, PHYSICAL VALIDATION OPEN

Implemented/compiled:

- read-only service/safety status;
- five-second observation;
- counts, p99/max and sample-gated p99.9;
- capture-integrity warning state;
- CPU concentration and bounded module contributors;
- repeated baseline progress/verdict/reasons UI;
- explicit configuration vs assigned-resource vs runtime-evidence wording;
- keyboard accelerators (`Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Ctrl+E`);
- accessibility/high-contrast improvements and automation names;
- adaptive narrow/wide workspace source;
- manual JSON evidence export;
- evidence schema `latencypilot-evidence-v3`;
- observation/baseline export contains bounded capture aggregates, `RequestId`, protocol/product version and bounded non-personal environment provenance;
- unresolved `ulong` routine addresses serialize as hexadecimal strings;
- export serialization/file I/O stays outside authoritative baseline capture windows.

Still open:

- owner-local runtime validation of the current WinUI source;
- verify JSON content against visible observation/baseline and retain SHA-256;
- narrow-window/text-scaling sanity on physical WinUI;
- focus/screen-reader sanity;
- physical evidence that capture-warning and sample-insufficient p99.9 states present correctly.

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

Required closure evidence now includes:

- exact package commit + green hosted validation run;
- owner-local published-App launch smoke;
- protected Service path;
- idle + controlled-load observations;
- exported evidence JSON + SHA-256 + RequestId correlation;
- active-console authorization behavior;
- cleanup/recovery/no-stale-ETW-session behavior;
- module/processor plausibility;
- representative inventory/resource evidence;
- explicit zero-mutation evidence;
- uninstall/service-removal evidence.

Stage B cannot close from CI/VM evidence alone.

## Stage C — repeated baseline quality — SOURCE COMPLETE, PHYSICAL CLOSURE OPEN

Next ordered work:

1. execute the five-window baseline on current packaged/owner-local source;
2. preserve complete/partial exported JSON and hashes;
3. confirm Valid/Inconclusive reasons match capture integrity/noise/drift reality;
4. repeat under one controlled workload;
5. decide from physical evidence whether additional background/thermal context is required before closure.

Stage C closes only when the real App → Service → ETW path distinguishes a trustworthy repeated baseline from an unstable/incomplete run.

## Stage D — Phase 2 evidence UX completion — PARTIAL

After Stage B/C physical evidence:

1. finish any responsive/text-scaling corrections exposed by physical UI;
2. finish focus/screen-reader corrections;
3. verify evidence export usability and auditability;
4. perform the Phase 2 UX/claim step-back review.

## After Phase 2

Phase 3 begins with the safety substrate, **not** with a tweak:

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
- hosted workflow supplies tests + App/Service compile evidence only;
- owner-local publisher supplies publish/package/runtime launch-smoke evidence;
- published versions are immutable;
- `main` is the default target for owner-directed automation; do not create/switch branches unless the owner explicitly requests it.

## Hard test rule

Permanent automated tests may not exceed **10** without explicit owner approval plus an ADR explaining why staying within 10 creates greater risk. Temporary implementation/debug tests may be created and removed.

## Completion/reporting discipline

Before closing a subsection, re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test-cap compliance, documentation drift and current owner constraints.

Use **implemented but not closed** when physical/runtime/package evidence is still missing. Every progress report must state what completed, its evidence, what remains open, the exact next stage and what follows it.
