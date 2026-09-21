# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-21

## Overall

- Product version: **0.0.2 pre-alpha** (`RELEASE_VERSION`).
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- GPU benchmark method: **`gpu-affinity-benchmark-v2`**.
- GPU evidence envelope/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v2`**. The benchmark envelope schema remains v1 because its shape did not change; method identity is independently versioned to v2.
- Product/safety authority: **ADR 0006**.
- GPU measurement/search/ranking authority: **ADR 0007**.
- Hosted GitHub Actions is **software-contract evidence only**. It cannot prove physical interrupt placement, device restart behavior, rendered WinUI/accessibility, LocalSystem behavior or package/signing behavior.

## Current v1 direction

LatencyPilot remains a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet check
→ deep ETW baseline
→ bounded Original qualification
→ paired GPU screening: Original before → Candidate → Original after
→ physical-core representatives → promising SMT siblings → up to 3 finalists
→ three independent 30 s local pairs per finalist
→ final target-only GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore original settings
```

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power changes and a generic cross-subsystem optimizer remain outside the v1 automatic path.

## Implemented source

### Measurement/recovery foundations

Implemented:

- processor-group-aware topology and CPU-set eligibility evidence;
- PnP/device/driver inventory;
- bounded kernel ETW DPC/ISR capture with per-CPU/module attribution and integrity accounting;
- evidence provenance/export contracts;
- durable SQLite mutation journal, compare-and-set lifecycle and recovery ownership;
- exact stored-state snapshot/restore primitives;
- non-elevated App plus narrow privileged helper/service boundaries;
- GPU interrupt-affinity mutation, target restart, renderer recreation and exact rollback;
- Raw Input → USB hub/port → xHCI read-only topology and input-host timing source.

### GPU paired-v2 contract

Current source implements ADR 0007:

1. 5 s non-scored Original warm-up.
2. 10 s scored Original qualification: require a three-run 1%-low cluster, ±3% preferred; observations four/five may recover within the bounded ±6% band. No valid cluster after five stops before candidate mutation and retains Original.
3. Fresh 10 s Original control `O0`.
4. Local screening pairs `O0 → C1 → O1 → C2 → O2 ...` with exact rollback between candidates.
5. Pair effect uses the equal-weight geometric mean of adjacent Original controls. Raw values remain untouched; there is no pseudo-normalized FPS.
6. Pair drift budget is `clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)`.
7. One retry is allowed for an unstable pair. Two consecutive candidates that exhaust the retry stop safely and retain Original.
8. Stage A screens one representative logical CPU per eligible physical core.
9. Stage B screens untested siblings only on the best two physical-core hypotheses, plus a third when within the 1% practical-equivalence margin; hard cap three cores.
10. Stage C advances the best two logical CPUs plus one within the 1% margin; hard cap three finalists.
11. Each finalist must obtain three independent 30 s valid local pairs in deterministically shuffled order.
12. A finalist is improvement-capable only when at least two of three primary effects are positive, median 1%-low effect exceeds `max(1%, median local-control movement)`, and primary/AVG/p99/interrupt-tail guardrails do not materially regress.
13. Finalists within one percentage point are a **practical tie**. Passive pressure/topology ordering may choose the operational target but must not be presented as proof of speed superiority.
14. Final Keep requires exact stored state plus clean attributable target-only GPU ISR placement. Otherwise exact Original is restored.

The report persists raw trials, pair capture ids, pair attempts/effects/control movement/drift budget/verdict, finalist aggregates and `DecisionFloor`, requested/validated processors, realized candidate/finalist order, durations, provenance and terminal state. Execution-order metadata is derived from persisted raw pair/trial evidence to avoid a second mutable source of truth.

### Diagnostic scopes

The developer UI provides:

- **Full search** — the authoritative paired-v2 machine search.
- **Selected CPUs · restore Original** — real paired screening for an exact subset; diagnostic-only, no finalist Keep, always restores Original, cannot close Gate A.
- **Original only · no system changes** — five 10 s Original observations with no affinity mutation or device restart.

CPU selection uses current topology/CPU-set eligibility; unsupported topology fails safely. The dialog is a WinUI `ContentDialog` owned by the current `XamlRoot`.

### Gate A result experience

The validated completion path is:

```text
validate report/session/source
→ package evidence ZIP when possible
→ build GateAResultPresentation
→ render authoritative result in Overview
```

The result surface now distinguishes:

- source/evidence eligibility from the performance verdict (`Evidence eligible`, not “Closure eligible”);
- persisted optimizer rank from shuffled execution order;
- raw trial history from paired decision effects;
- direct `Original before → Candidate → Original after` evidence;
- finalist median effects/decision floor from short-screen evidence;
- `Winner`, `Practical tie`, `No measured winner`, `Inconclusive` and custom diagnostic outcomes;
- exact terminal machine state.

Candidate bars are centered on 0% paired 1%-low effect and never re-rank candidates in UI code. A RestoreOriginal result may show the best measured candidate as diagnostic context, but never as a kept winner.

### USB/xHCI recommendation

After a verified GPU Keep, current source can stop the GPU benchmark, capture quiet ETW headroom, resolve primary Raw Input routes to exact xHCI controllers, exclude the GPU winner's physical core, rank remaining CPUs by interrupt duration/tail evidence, and persist the recommendation.

The xHCI recommendation remains read-only/product-gated until the shared mutation/recovery substrate closes physical GPU Gate A and the integrated xHCI verification path is completed.

## Verification state

### Hosted software verification

The paired-v2 work is covered by the existing permanent critical-test budget rather than adding a new test family. TDD/CI has already caught and driven fixes for:

- stale v1 benchmark method identity;
- UI-side re-ranking rather than persisted `DecisionRank`;
- wrong finalist phase name in presentation;
- short-screen data incorrectly taking precedence over finalist authority;
- missing persisted finalist `DecisionFloor`;
- raw-FPS candidate bars instead of paired-effect bars;
- misleading “Closure eligible” copy;
- unsupported CPU-scope handling;
- stale progress warm-up semantics;
- hidden direct pair evidence.

The exact final documentation-reconciled HEAD still needs its own successful hosted Tests run. Older successful or cancelled runs are not proof for a later SHA.

### Physical GPU Gate A

**OPEN.**

Historical physical evidence at source revision `15879543ce54eadb8342a87e367d60a6d5d5f81f` proved useful v1 safety properties: journal-owned apply/restart/rollback, full old-method screening, exact verified Original restoration and zero unresolved recovery ownership. That run used the superseded v1 measurement/ranking method and therefore **cannot** validate paired-v2 or the current UI.

Physical Gate A for v2 requires one exact clean green current revision to prove:

1. bounded Original qualification either succeeds in at most five scored observations or stops before mutation;
2. Stage-A physical-core representative coverage is correct for the actual topology;
3. Stage-B sibling refinement targets only the selected promising cores without even/odd assumptions;
4. direct local pair controls/capture IDs/effects/movement/retries are correct and raw values remain unchanged;
5. repeated local instability fails safe rather than being converted into candidate benefit;
6. finalists receive the required three independent 30 s valid pairs or become inconclusive;
7. finalist `DecisionFloor`, guardrails and practical-tie semantics match the persisted report;
8. exact rollback succeeds between candidate activations and on failure/cancellation;
9. final ETW proves attributable target-only GPU ISR placement before Keep;
10. terminal state verifies with `unresolved=0`;
11. **Stop safely** and one supported failure/recovery path restore exact Original;
12. a second full paired-v2 search is practically reproducible or reports instability explicitly;
13. the real Windows result surface is inspected in relevant theme/text-scale/keyboard/accessibility states.

Dirty runs remain development evidence only. `GateAClosureEligible` is a source/evidence-eligibility field, not proof that the physical gate has closed.

Public mutation remains unarmed until this physical gate passes.

## Roadmap snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Keep exact-head verification current |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI preflight |
| 2 Baseline | **ETW engine exists** | Wire deep comparable baseline into one-button workflow |
| 3 GPU search | **Paired-v2 source/result contracts implemented** | Exact-head CI + physical Gate A + repeat + recovery/render inspection |
| 4 GPU Keep | **Internal verified-Keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only recommendation implemented** | Representative physical evidence + product rendering |
| 6 USB apply | **Internal reversible substrate / product-gated** | Integrated physical apply/verify/rollback evidence |
| 7 Reboot verify | **Recovery/reboot primitives implemented** | Combined GPU+xHCI reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated comparable final capture/report |
| 9 UX/release | **Development Gate A result UX implemented** | Real render/accessibility + one-button product UX + release closure |

## Immediate execution ladder

1. Obtain hosted **Tests** success on the exact final `main` HEAD after source/docs reconciliation.
2. Run Original-only and a small selected-CPU diagnostic on that exact clean revision to check physical measurement/restart/recovery behavior quickly.
3. Run one full paired-v2 Gate A search and inspect report, direct pair evidence, terminal state, renderer/device restart behavior and result UI.
4. Repeat the full search for practical reproducibility.
5. Exercise **Stop safely** plus one supported failure/recovery path with `unresolved=0` and verify exact Original.
6. Inspect the real WinUI result surface in light/dark/high-contrast, text scaling and keyboard flows.
7. Only after physical Gate A passes, arm the typed allowlisted product mutation boundary and proceed with integrated xHCI/reboot/before-after physical validation.

## Completion rule

Repository/source completion means every current v1 source outcome is implemented or explicitly gated by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical GPU Gate A, xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence.
