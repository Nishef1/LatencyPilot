# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-21

## Overall

- Product version: **0.0.2 pre-alpha** (`RELEASE_VERSION`).
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- Automatic GPU method: **`gpu-affinity-benchmark-v1`**.
- Automatic GPU evidence/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v1`**.
- Current v1 product/design authority: **ADR 0006**, including the 2026-09-20 time-local measurement amendment.
- Gate A external frame cross-check: pinned standalone **PresentMon console**; a separately installed PresentMon Service/API is not required.
- Hosted GitHub Actions is **software-contract evidence only**. It cannot prove physical interrupt placement, device restart behavior, rendered UI/accessibility, LocalSystem behavior or package/signing behavior.

## Current v1 direction

LatencyPilot v1 remains a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet check
→ deep ETW baseline
→ GPU logical-CPU screen in bounded groups
→ time-local Original controls around screening blocks
→ normalized decision aggregates + explicit uncertainty
→ noise-aware finalist re-test
→ select by 1% low → AVG → lower p99 → 0.1% rare-tail context
→ reject frame/interrupt-tail regressions and fall through to the next clean finalist
→ final ETW GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore original settings
```

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power changes and the cross-subsystem Pareto optimizer are outside the v1 automatic path. Existing source in those areas may remain for future/read-only/recovery use.

## What is implemented now

### Measurement/recovery foundations

Implemented source includes:

- processor-group-aware CPU topology and CPU-set evidence;
- PnP/device/driver inventory;
- bounded kernel ETW DPC/ISR capture with per-CPU/module attribution and integrity accounting;
- evidence provenance/export contracts;
- durable SQLite mutation journal, CAS lifecycle and recovery ownership;
- exact stored-state snapshot/restore primitives;
- normal-user App + narrow privileged helper/service boundaries;
- GPU interrupt-affinity mutation, target restart and exact rollback;
- Raw Input → USB hub/port → xHCI read-only topology and input-host timing source;
- NIC/RSS read-only source retained outside the v1 automatic path.

### GPU auto-affinity measurement contract

The current source follows ADR 0006 plus the temporal-stability hardening learned from owner hardware evidence:

```text
capture exact Original/default state
→ deterministic D3D12 calibration / frozen workload
→ Original non-scored warm-up/reference
→ establish Original scored repeatability/noise (3 runs, one bounded replacement if needed)
→ screen every eligible logical CPU in blocks of at most four:
     apply/restart/verify
     non-scored warm-up
     one scored screen
     exact rollback + Original-state verification
     fresh Original block control after each full block when candidates remain
→ fresh Original control after final screening block
→ normalize rankable screening decision metrics against the time-local controls
→ preserve raw trials unchanged and carry control movement as uncertainty
→ if effective 1%-low variability >15%:
     retain diagnostic screening evidence, skip finalists, RestoreOriginal
→ otherwise rank normalized decision aggregates and re-test a bounded shortlist
→ prefer stable 3-of-up-to-4 evidence; otherwise retain all four and carry measured variance
→ fresh Original control after finalist re-tests; merge phase movement into uncertainty
→ evaluate finalists against Original + repeatability + time-local uncertainty + guardrails
→ apply highest-ranked clean winner once
→ final benchmark-only warm-up + ETW-backed placement verification
→ Keep only with clean attributable target-only GPU ISR placement
   otherwise exact RestoreOriginal
```

Important properties:

- Windows default is reference/recovery, not a fixed winner gate.
- CPU0 and eligible SMT siblings are first-class candidates; no even/odd CPU assumption exists.
- Ordinary gradual Original-control movement is **not** treated as a structural experiment failure. It is measured, used to normalize candidate decision aggregates and carried forward as uncertainty.
- Raw candidate/control observations remain separate from normalized decision evidence.
- Time-local uncertainty raises the final improvement/guardrail threshold; it is never credited as candidate benefit.
- Effective 1%-low variability above 15% still blocks expensive finalist confirmation and automatic Keep; exact Original is retained.
- Structural evidence failures remain fail-closed: invalid artifact/session identity, source/state divergence, failed mutation/recovery ownership, healthy ETW proving wrong placement, or unverified terminal state are not normalized away.
- No separate SMT sibling-refinement phase and no ABBA/BAAB confirmation loop exist in v1.
- Final Keep is stricter than screening: missing/unhealthy ETW or missing target-only ISR proof restores Original.
- PresentMon remains an independent best-effort cross-check. Current `FrameTime` and legacy `MsBetweenPresents` are accepted for cadence; `MsBetweenAppStart` is not treated as equivalent.
- `PresentMonWorkloadCaptureStatus.NoSwapChains` remains explicit diagnostic evidence and cannot fabricate frame/guardrail data.

### Gate A result experience

The development completion path now has one authority-preserving result flow:

1. validate report schema/session/source eligibility;
2. package the validated session into a shareable evidence ZIP when possible;
3. build `GateAResultPresentation` from the authoritative report + bundle result;
4. render that model inside Overview;
5. leave raw JSON as an explicit user action rather than automatically opening Explorer.

The Overview result surface includes:

- terminal result/eligibility summary;
- Original → candidate decision metrics;
- GPU candidate comparison using persisted decision aggregates;
- scored Original/comparison-candidate trial history;
- decision-evidence rows for primary improvement, repeatability/uncertainty, guardrails, runtime ISR placement and final state;
- `Open ZIP`, `Copy ZIP path`, `Open session folder`, and `Open raw report` actions.

The UI does **not** infer a winner from shuffled candidate collection order. Non-Keep comparisons are labelled diagnostic/comparison-only and the exact restored Original state remains the terminal truth.

### USB/xHCI recommendation

After a verified GPU Keep, current source can:

- stop the GPU benchmark;
- capture a quiet kernel-ETW headroom window;
- resolve primary Raw Input mouse routes through exact USB hub/port evidence to xHCI;
- exclude the whole physical core containing the GPU winner;
- rank remaining logical CPUs by total DPC+ISR duration, then p99 interrupt tail, then event-count context;
- persist the recommendation and route evidence in the Gate A report.

The recommendation remains read-only/product-gated until the shared mutation/recovery substrate closes physical Gate A and the xHCI verification path is integrated.

## Verification state

### Hosted software verification

The result-surface implementation was developed through repeated CI-driven correction rather than assuming the WinUI code compiled:

- initial contract RED proved Gate A completion did not package evidence;
- packaging was wired after report identity validation;
- a second RED proved the validated report still was not handed to the in-product result presentation;
- Overview handoff/charts/evidence actions were added;
- hosted compilation then exposed a real WinUI `UIElement`/`FrameworkElement` attached-property mismatch and analyzer findings, which were corrected in source.

The **exact final documentation/source HEAD still must be green** before authoritative physical evidence is collected. A prior or intermediate green run is not proof for a later HEAD.

### Physical GPU Gate A

Still **OPEN**.

Historical owner runs proved several safety/substrate properties including journal-owned apply/rollback and clean exact-Original restoration. The 2026-09-19 development run was safe but showed severe time/order background movement; raw candidate ranking was therefore not trustworthy and Original was correctly restored. That evidence motivated the current bounded time-local control/normalization method. It did not establish a winning CPU.

The next authoritative Gate A run must use one exact clean green `main` revision and prove:

1. every expected eligible logical CPU receives one scored screening run;
2. intermediate/final Original controls are captured and their movement is persisted;
3. normalized decision aggregates remove measured local background level without altering raw trial history;
4. local movement appears in uncertainty and raises Keep thresholds rather than becoming candidate benefit;
5. >15% effective variability restores Original without exhaustive finalist confirmation;
6. otherwise the bounded shortlist receives the documented independent re-tests;
7. repeatability remains capped at four scored attempts per Original/finalist decision set;
8. the selected finalist clears Original, repeatability, time-local uncertainty and guardrail thresholds;
9. exact rollback succeeds between candidate activations and on failure/cancellation;
10. final ETW proves target-only GPU ISR placement before Keep;
11. terminal state is verified with `unresolved=0`;
12. Stop safely plus one supported failure/recovery path restore exact Original;
13. a second whole search is practically reproducible or reports instability explicitly;
14. the rendered result surface is inspected on real Windows in relevant theme/text-scale/keyboard states.

Dirty `main` runs remain useful development evidence only: `SourceState=DevelopmentOnly`, `GateAClosureEligible=false`.

Public product mutation remains unarmed until this physical gate passes.

## Roadmap progress snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Exact-final-HEAD hosted Tests |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI v1 preflight |
| 2 Baseline | **ETW engine exists** | Wire deep baseline into one-button v1 workflow |
| 3 GPU search | **Time-local decision method + result source implemented** | Exact-head CI + physical Gate A + repeat |
| 4 GPU Keep | **Internal verified-keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only recommendation implemented** | Representative physical evidence + product rendering |
| 6 USB apply | **Internal reversible substrate / product-gated** | Integrated physical apply/verify/rollback evidence |
| 7 Reboot verify | **Recovery/reboot primitives implemented** | Combined GPU+xHCI reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated comparable final capture/report |
| 9 UX/release | **Development Gate A result UX implemented** | Real render/accessibility inspection + one-button product UX + release closure |

## Immediate execution ladder

1. Get hosted **Tests** green on the exact final `main` HEAD after source/doc reconciliation.
2. Run the new Gate A path locally and inspect the rendered Overview result, evidence ZIP/actions, light/dark/high-contrast/text-scale behavior and keyboard reachability without yet treating that run as closure if the checkout is dirty.
3. On one exact clean green revision, run physical Gate A and inspect time-local controls, normalized decision aggregates, uncertainty, terminal state, total runtime and any PresentMon diagnostics.
4. Repeat the whole search for practical reproducibility.
5. Exercise Stop safely plus one supported failure/recovery path with `unresolved=0`.
6. Only after physical Gate A passes, arm the typed allowlisted product mutation boundary; then close xHCI/reboot/before-after physical validation.

## Completion rule

Repository/source completion means every v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.