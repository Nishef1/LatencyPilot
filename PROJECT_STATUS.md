# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current files/runtime are evidence of what exists; plans and prior chat are context only.

Last updated: 2026-09-13

## Overall

- Product version: **0.0.2 pre-alpha**
- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Mutation capability: **None by design**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 / unpackaged self-contained**
- Privileged boundary: **read-only Windows Service; mutation commands do not exist**
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`)
- Evidence schema: **`latencypilot-evidence-v8`**
- Quick observation purpose: **`quick-diagnostic-snapshot`**
- Repeated baseline purpose: **`repeated-decision-baseline`**
- Baseline method: **`baseline-quality-v2`**
- Permanent automated tests: **8 / hard maximum 10**
- Hosted CI: **test-only**; it does not build/run/publish App or Service
- Current stage: **Phase 2 measurement methodology/source hardening is open again; physical closure follows on one final exact revision**

## Why Phase 2 methodology was reopened

The earlier implementation used one five-second observation and a repeated baseline of five five-second windows. Physical captures proved that the App → Service → ETW path, module attribution, RequestId correlation and source provenance were working, and repeatedly exposed a graphics/CPU0 concentration hypothesis.

A step-back review found that the same five-second duration was being asked to serve two different jobs:

1. quick diagnosis/integrity/attribution;
2. decision-grade stability evidence.

That was too weak for the second job and contradicted the project's original experimental contract: repeated runs, measured noise, A/B confirmation and Keep/Revert rather than one-shot tweak logic.

External review reinforced the correction:

- Microsoft remains authoritative for ETW/API/resource semantics and driver guidance, but default interrupt placement is not treated as a universal optimization oracle;
- PresentMon exposes frame, CPU/GPU busy/wait, GPU/display latency and related metrics needed for later GPU guardrails;
- AutoGpuAffinity uses much longer per-candidate measurement and workload/cache settling than a five-second sample;
- community reports are inconsistent across machines, which supports machine-specific experiments rather than rules such as “CPU0 is always bad”.

Therefore old five-second captures are retained as **diagnostic evidence**, not promoted to benchmark verdict evidence.

## Current measurement contract

### Quick diagnostic snapshot

```text
1 × 5 seconds
```

Purpose:

- capture integrity;
- module/routine attribution;
- CPU concentration;
- obvious long-tail buckets;
- hypothesis generation.

It must not claim system health, stability, improvement, regression, or an optimizer recommendation.

### Repeated decision baseline — `baseline-quality-v2`

```text
workload already warmed/repeatable by user when applicable
5 s LatencyPilot/service settle
5 windows × 20 s
750 ms inter-window settle
= 100 s authoritative measurement
```

Every authoritative window requires:

- requested duration >= 20,000 ms;
- actual duration >= 95% of request;
- clean ETW/integrity evidence;
- >=1,000 DPC events;
- >=1,000 ISR events;
- finite positive DPC/ISR p99.

The five-window stability screen retains:

- <=30% P10–P90 relative spread;
- <=20% early/late relative drift;
- no >50% extreme-window deviation;
- no silent window deletion.

`Valid` means repeatable enough for the current comparison contract; it does **not** mean the machine is healthy or optimally configured.

### p99.9 adequacy

Protocol v6 withholds p99.9 until the distribution contains at least **10,000 samples**. The previous 1,000-sample display floor was rejected as too weak for prominent tail interpretation.

## Source implementation currently present

### App / UX

- normal-user WinUI 3 App;
- explicit Real-world / Controlled idle / Before-after scenarios;
- two-check preparation gate only for decision baseline;
- Real-world preparation keeps issue-reproducing apps open and asks for a warmed/repeatable workload point;
- five-second quick snapshot remains available without baseline-preparation checks;
- quick-snapshot copy is being normalized to diagnostic/reference semantics rather than “latency health” pass/fail language;
- repeated baseline captures five 20-second authoritative windows and renders detailed UI after the sequence rather than between windows;
- runtime CPU/power context brackets capture windows without becoming a fake pass/fail criterion;
- keyboard paths: `Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Ctrl+E`;
- read-only representative GPU/NIC/xHCI evidence inspector;
- build header/source provenance from `BUILD_INFO.txt`.

### Observation Service

- Windows Service hosts privileged kernel ETW observation;
- allowlisted commands remain status + kernel observation only;
- `ServiceBoundary.MutationAvailable == false`;
- no generic shell/process/registry mutation primitive;
- active-console/session authorization remains fail-closed;
- request-specific deadlines and disconnect cancellation remain bounded;
- DPC/ISR aggregation by processor and image/module attribution are implemented;
- unresolved addresses remain unresolved rather than guessed;
- event loss/invalid events/event-limit state are preserved.

### Protocol/evidence

- protocol v6;
- p99.9 sample floor = 10,000;
- evidence v8 explicitly distinguishes `quick-diagnostic-snapshot` from `repeated-decision-baseline`;
- source revision, RequestIds, scenario and environment/runtime context remain auditable;
- JSON export computes SHA-256 after save;
- baseline export checks capture/window/runtime-window alignment including timestamp and requested/actual duration;
- `scripts/Verify-Evidence.ps1` is a strict v8/v2 verifier rather than a generic “JSON looks valid” script.

### Baseline analysis

`baseline-quality-v2` owns the decision-baseline duration/sample/noise contract. The existing permanent baseline test slot was expanded to cover the v2 duration and undersampling failure cases instead of adding another permanent test.

Permanent test count remains **8/10**.

## Historical physical evidence retained as diagnostic evidence

The owner has already physically launched/runs earlier 0.0.2 revisions on Windows 11. Multiple clean five-second Real-world captures had:

- zero ETW loss;
- zero invalid latency/image events;
- no event-limit hit;
- complete or effectively complete module attribution;
- repeated graphics-stack dominance (`nvlddmkm.sys` / `dxgkrnl.sys`);
- strong CPU0 DPC/ISR concentration;
- no >1 ms DPC/ISR events in the cited captures.

One recorded clean revision `1ffaf4ab5ad5cffc8526097bf675c4efecbb5ee0` produced evidence with RequestId `fad7475c-3ea6-40e4-aae3-f94c048c4fac` and independently computed SHA-256 `8090962e42ff2ff974cfb32ca5ccf00b7c6dd77879eaf429652dffa246f170d9`.

That capture reported roughly:

- DPC p99 `131.434 µs`, max `153.9 µs`;
- ISR p99 `65.414 µs`, max `81.5 µs`;
- CPU0 ~63.7% of DPC and ~94.7% of ISR events;
- almost all cited guidance exceedances in the NVIDIA/DirectX graphics stack.

A later clean five-second capture on revision `3324a87a15f4962528870b8ce91bf01cd5d87f6f` again showed graphics/CPU0 concentration.

Interpretation after methodology review:

> These captures prove the observation path and strengthen a graphics/CPU0 hypothesis. They do **not** prove that GPU affinity should be changed, that CPU0 is faulty, or that any candidate setting improves the user's workload.

They do not satisfy the v8/v2 decision-baseline closure gate.

## Phase 0 — CLOSED

Governance, licensing, contribution/security policy, architecture, benchmark methodology, roadmap/status tracking and repository templates exist.

## Phase 1 — CLOSED

The solution/domain/comparison foundation and first non-mutating desktop vertical slice exist. WinUI 3 superseded the historical WPF UI decision.

## Phase 2 — OPEN

### 2.1 Inventory/provenance — source implemented, physical validation open

Implemented:

- processor-group-aware topology;
- stable present PnP IDs;
- driver provider/version/INF metadata;
- stored interrupt configuration with availability/error provenance;
- allocated ConfigMgr IRQ/resource evidence;
- partial-evidence behavior for optional failures;
- representative GPU/display, NIC and actual `USBXHCI` devices;
- centralized Windows product labeling;
- clean/dirty source revision provenance.

Semantic boundary remains:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Physical GPU/NIC/xHCI plausibility remains open.

### 2.2 ETW observation — source implemented, physical final-candidate validation open

Implemented:

- bounded kernel ETW lifecycle;
- DPC/ISR durations/counts;
- per-processor distributions;
- image lifetime/rundown-aware module attribution;
- bounded module/unresolved lists;
- canonical percentile estimator;
- capture-integrity provenance;
- RequestId correlation;
- protocol-v6 validation;
- p99.9 only with >=10,000 samples.

Interpretation rules:

- 100 µs DPC / 25 µs ISR are Microsoft driver-duration reference lines;
- >1 ms / >3 ms are LatencyPilot local diagnostic buckets;
- none of these alone is a system-health/impact verdict;
- CPU0 concentration is a hypothesis signal, not a fault classification.

### 2.3 Decision baseline — v2 source contract implemented, physical closure open

Required physical evidence on the final exact revision:

1. Real-world `5 × 20 s` decision baseline;
2. Controlled-idle `5 × 20 s` decision baseline;
3. v8 JSON export for each;
4. exact source revision + SHA-256 reconciliation;
5. `Verify-Evidence.ps1 -RequireCleanCapture -RequireValidBaseline` PASS for each.

### 2.4 Evidence UX — source hardening in progress, physical closure open

Required physical checks:

- quick snapshot visibly described as diagnostic only;
- 10,000-sample p99.9 withholding state;
- baseline readiness and ~2 minute sequence copy;
- no misleading “healthy because under Microsoft threshold” verdict;
- JSON-visible-data reconciliation;
- responsive/text scaling;
- Light/Dark/High Contrast;
- keyboard and screen-reader/UI Automation sanity;
- device-evidence inspector.

### 2.5 Failure/security boundary — physical closure open

Validate:

- App close during capture;
- Service stop/restart;
- no stale kernel ETW session;
- partial baseline never becomes Valid;
- active-console authorization;
- protocol mismatch/rejection where practical;
- zero mutation.

## Current CI rule

Hosted **Tests** is intentionally test-only. A green run proves the deterministic contract for Core/Benchmarking/Protocol/Platform.Windows referenced by `LatencyPilot.CriticalTests`; it does not prove WinUI App compilation or physical Service/ETW behavior.

Rapid pushes may cancel superseded runs. Final claims require a completed successful Tests run for the exact final source revision.

## Exact next execution ladder

After methodology/source hardening stops changing the candidate:

1. Confirm final `main` has a completed green eight-test workflow.
2. On the owner's Windows 11 machine, clean-pull that exact SHA and run:

   ```powershell
   .\run.ps1
   ```

3. Confirm header source revision, Service connected/read-only and protocol compatibility.
4. Take one five-second Real-world quick diagnostic snapshot; export v8 and run:

   ```powershell
   .\scripts\Verify-Evidence.ps1 <observation.json> `
     -ExpectedCommit <final-sha> `
     -RequireCleanCapture
   ```

5. Put the real workload at a warmed/repeatable point and run the five × 20-second Real-world decision baseline.
6. Export and verify with:

   ```powershell
   .\scripts\Verify-Evidence.ps1 <baseline.json> `
     -ExpectedCommit <final-sha> `
     -RequireCleanCapture `
     -RequireValidBaseline
   ```

7. Run and verify the five × 20-second Controlled-idle decision baseline.
8. Validate representative GPU/NIC/xHCI evidence and attribution plausibility.
9. Exercise disconnect/Service-restart/stale-ETW/session authorization paths.
10. Close responsive/accessibility/UI evidence checks.
11. Reconcile the whole Phase 2 exit gate. Only then close Phase 2.

## After Phase 2

Phase 3 introduces persistence and reversible mutation infrastructure, not random tweaks:

```text
Detect
→ Snapshot exact original state
→ Validate candidate
→ Journal
→ Apply one change
→ Verify actual applied state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
```

Initial GPU-affinity work should retain the Windows/default state as a control, use topology-aware/physical-core-aware candidate screening, and confirm finalists with balanced/interleaved repeated measurements such as ABBA/BAAB. A practical starting confirmation interval is around 30 seconds per authoritative candidate/control run, subject to versioned sample/noise requirements.

GPU recommendations must eventually combine DPC/ISR evidence with applicable PresentMon frame/CPU/GPU/latency metrics and relevant subsystem guardrails. Neither Microsoft defaults, Reddit advice nor AutoGpuAffinity rankings are accepted without local measured confirmation.

Later roadmap direction remains:

- Phase 4: USB/input experiments;
- Phase 5: NIC/RSS experiments;
- Phase 6: multi-objective optimizer / Pareto / Restore Baseline;
- Phase 7: productization/release hardening.
