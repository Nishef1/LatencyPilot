# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-13

## Overall

- Product version: **0.0.2 pre-alpha**
- Product completion: **Phases 0–1 closed; Phase 2 physical closure open**
- Mutation capability: **None by design**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **read-only Windows Service; mutation commands do not exist**
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`)
- Evidence schema: **`latencypilot-evidence-v8`**
- Quick observation purpose: **`quick-diagnostic-snapshot`**
- Repeated baseline purpose: **`repeated-decision-baseline`**
- Baseline method: **`baseline-quality-v2`**
- Permanent automated tests: **8 / hard maximum 10**
- Hosted CI: **test-only**; App/Service compile/runtime/release evidence remains owner-local
- Current stage: **Phase 2 source/methodology hardening is frozen pending exact-revision CI plus owner-local physical closure**

## Why the Phase 2 methodology changed

Earlier 0.0.2 builds used five-second captures for both quick diagnosis and repeated baseline evidence. Physical runs proved useful parts of the observation path — App → Service → ETW, RequestId correlation, attribution and source provenance — but a step-back review found that one short duration was serving two different jobs.

The current contract separates them:

```text
Quick diagnostic snapshot
  1 × 5 s
  integrity / attribution / concentration / hypothesis generation
  never a health or optimization verdict

Repeated decision baseline — baseline-quality-v2
  workload already warmed/repeatable when applicable
  5 s LatencyPilot/service settle
  5 × 20 s authoritative windows
  750 ms inter-window settle
  duration + integrity + sample + noise + drift gates
```

This restores the original product principle: measurement quality must be established before later mutation/Keep/Revert decisions are allowed.

## Current measurement contract

### Quick diagnostic snapshot

- one five-second DPC/ISR capture;
- explicit `quick-diagnostic-snapshot` purpose in evidence-v8;
- capture integrity, module attribution, CPU concentration and local tail/reference context;
- no stability, health, improvement, regression or optimizer claim.

### Repeated decision baseline — `baseline-quality-v2`

Every authoritative window requires:

- requested duration >=20,000 ms;
- actual duration >=95% of request;
- clean ETW/capture integrity;
- >=1,000 DPC events;
- >=1,000 ISR events;
- finite positive DPC/ISR p99.

Exactly five contiguous windows are required. For both DPC p99 and ISR p99:

- P10–P90 relative spread <=30%;
- relative drift between early and late windows <=20%;
- no >50% extreme-window deviation;
- no silent deletion of inconvenient windows.

`Valid` means repeatable enough for the current comparison method. It does **not** mean the machine is globally healthy or optimally configured.

### p99.9 adequacy

Protocol v6 exposes p99.9 only when that individual distribution has at least **10,000 samples**. This is an adequacy floor, not a confidence guarantee.

## Phase 2 source state — FROZEN FOR PHYSICAL VALIDATION

### Inventory/provenance

Implemented:

- processor-group-aware CPU topology;
- stable present PnP IDs;
- driver provider/version/INF metadata;
- stored interrupt configuration with availability/error provenance;
- allocated ConfigMgr IRQ/resource evidence;
- partial-evidence behavior for optional device/resource failures;
- representative GPU/display, NIC and actual `USBXHCI` evidence inspector;
- clean/dirty source revision provenance through `BUILD_INFO.txt` and the App header.

Semantic boundary remains:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

### Privileged read-only observation

Implemented:

- protected Windows Service observation host;
- typed/bounded/fail-closed protocol v6;
- status + kernel observation commands only;
- `MutationAvailable=false`;
- no generic shell/process/registry mutation primitive;
- network identities denied and active-console client-session authorization;
- disconnect/deadline/Service-stop cancellation;
- bounded ETW session lifecycle and cleanup;
- DPC/ISR duration/count collection;
- per-processor aggregation;
- authoritative image-lifetime/rundown-aware module attribution;
- unresolved addresses remain unresolved;
- bounded module/unresolved lists;
- capture-integrity provenance;
- RequestId correlation across App/Service/evidence;
- p50/p95/p99/max plus >=10,000-sample p99.9.

Interpretation remains deliberately narrow:

- DPC >100 µs / ISR >25 µs are Microsoft driver-duration guidance references;
- >1 ms / >3 ms are LatencyPilot local diagnostic buckets;
- CPU0 concentration is evidence/hypothesis, not a fault classification.

### Baseline and evidence

Implemented:

- workload-preparation checks for repeated decision baselines;
- five-second LatencyPilot/service pre-sequence settle;
- five 20-second authoritative windows;
- lightweight inter-window UI activity only;
- `baseline-quality-v2` duration/sample/integrity/noise/drift contract;
- Real-world / Controlled idle / Before-after scenario provenance;
- stale evidence invalidation when scenario changes;
- best-effort runtime CPU/power provenance;
- evidence-v8 explicit purpose field;
- capture/window/runtime-window alignment validation;
- unique RequestIds and exact clean source revision where available;
- SHA-256 after evidence save;
- independent `scripts/Verify-Evidence.ps1` verification;
- strict `-RequireCleanCapture` and `-RequireValidBaseline` gates.

### UX hardening

Implemented in source:

- quick action labeled **Quick snapshot · 5 s**;
- repeated action labeled **Build baseline · ~2 min**;
- snapshot card uses diagnostic/reference language instead of a transient “latency health” verdict;
- p99.9 insufficient-sample state uses the protocol-v6 10,000-sample floor;
- no Microsoft guidance reference is presented as a global pass/fail score;
- runtime context and evidence purpose remain explicit;
- keyboard paths `Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Ctrl+E`;
- High Contrast/theme/accessibility metadata and adaptive layout source;
- representative device-evidence inspector;
- static XAML and runtime copy agree on five × 20-second baseline timing.

No new permanent test was added during this hardening pass. Count remains **8/10**.

## Automated evidence policy

Hosted **Tests** intentionally exercises the permanent deterministic suite only. It does not build or run the WinUI App, build/run the Windows Service, start ETW, launch the GUI, package a release or validate physical hardware.

Therefore:

```text
hosted Tests
→ deterministic Core/Benchmarking/Protocol/Platform.Windows contracts

owner-local Windows
→ App/Service compile + runtime + ETW + physical UX/security validation

owner-local release path
→ publish/package/launch-smoke/checksums/release
```

Rapid source commits can cancel superseded workflow runs. The physical candidate must use a completed successful Tests run for the exact revision being built.

## Historical physical evidence — DIAGNOSTIC ONLY

The owner has physically launched earlier 0.0.2 revisions on Windows 11 and exported clean short Real-world observations. Those runs established that the observation path can work on the target machine and repeatedly exposed a graphics-stack/CPU0 concentration hypothesis.

One preserved earlier evidence artifact from clean revision `1ffaf4ab5ad5cffc8526097bf675c4efecbb5ee0` had:

- RequestId `fad7475c-3ea6-40e4-aae3-f94c048c4fac`;
- SHA-256 `8090962e42ff2ff974cfb32ca5ccf00b7c6dd77879eaf429652dffa246f170d9`;
- zero ETW loss and zero invalid latency/image events;
- no event-limit hit;
- DPC p99 about `131.434 µs`, max `153.9 µs`;
- ISR p99 about `65.414 µs`, max `81.5 µs`;
- strong CPU0 concentration;
- NVIDIA/DirectX graphics-stack dominance in the cited reference exceedances;
- no >1 ms DPC/ISR events in that capture.

These old five-second captures strengthen a hypothesis and prove historical observation-path behavior. They do **not** satisfy current evidence-v8 / baseline-quality-v2 closure and do not prove that GPU affinity should be changed.

## Physical Phase 2 closure — OPEN

All following evidence must come from the **same final clean source revision**:

1. completed green hosted eight-test workflow for the exact revision;
2. owner-local `run.ps1` build/install/launch success;
3. App header shows exact source revision and Service reports connected/read-only;
4. one clean five-second Real-world quick snapshot exported as evidence-v8 and verified;
5. one valid warmed/repeatable Real-world five × 20-second baseline-v2, exported and strictly verified;
6. one valid Controlled-idle five × 20-second baseline-v2, exported and strictly verified;
7. representative GPU/NIC/xHCI evidence and broad attribution plausibility;
8. App-close/Service-restart/stale-ETW recovery checks;
9. active-console authorization sanity;
10. Light/Dark/High Contrast, responsive/text-scaling, keyboard and UI Automation/screen-reader sanity;
11. JSON-visible-data/SHA/source-revision reconciliation;
12. zero unrelated system mutation.

Do not close Phase 2 from CI, VM evidence or historical five-second observations alone.

## Exact next owner-local sequence

After the final exact revision has green Tests:

```powershell
.\run.ps1
```

Then:

1. confirm the exact revision in the App header and read-only Service status;
2. Real-world quick snapshot → export → `Verify-Evidence.ps1 -RequireCleanCapture`;
3. warm the Real-world workload to the intended repeatable point;
4. Real-world baseline → export → `-RequireCleanCapture -RequireValidBaseline`;
5. Controlled-idle baseline → export → the same strict verifier gates;
6. finish device/plausibility, failure/recovery/session and accessibility checks;
7. reconcile the full Phase 2 exit gate.

## After Phase 2

Phase 3 starts with safety infrastructure, not random tweaks:

```text
Concrete SQLite persistence/journal/recovery schema
→ mutation-specific authorization/allowlist
→ Detect applicability
→ Snapshot exact original state
→ Validate candidate
→ Journal pending experiment
→ Apply one narrow change
→ Verify actual applied state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
```

The first GPU experiment retains current/default state as a control, generates topology-aware physical-core candidates, screens candidates in bounded fashion and confirms finalists with balanced/interleaved repeated A/B ordering such as ABBA/BAAB. PresentMon frame/CPU/GPU/latency metrics become guardrails/targets where they actually observe the relevant graphics workload.

Later direction remains:

- Phase 4 — USB/xHCI + host-observable input timing;
- Phase 5 — NIC/RSS optimization;
- Phase 6 — bounded cross-subsystem optimizer/profiles/Pareto/Restore Baseline;
- Phase 7 — productization/release hardening.
