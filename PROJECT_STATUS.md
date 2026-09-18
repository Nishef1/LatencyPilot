# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-18

## Overall

- Product version: **0.0.2 pre-alpha**.
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- Automatic GPU method: **`gpu-affinity-benchmark-v1`**.
- Automatic GPU evidence/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v1`**.
- Current v1 product/design authority: **ADR 0006** (`docs/adr/0006-simple-auto-interrupt-affinity-v1.md`).
- Gate A external frame cross-check: pinned standalone **PresentMon 2.5.1 console**, official SHA-256 verified; a separately installed PresentMon Service/API is not required.
- Hosted GitHub Actions remains **test-only**. It cannot prove physical interrupt placement, device restart behavior, rendered UI/accessibility, LocalSystem behavior or package/signing behavior.

## Current v1 direction

LatencyPilot v1 is a narrow automatic interrupt-affinity workflow, not a generic whole-PC optimizer:

```text
preflight / quiet check
→ deep ETW baseline
→ GPU physical-core screen
→ top-3 re-test
→ select by 1% low → 0.1% low → AVG (p99 context)
→ final ETW GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore Windows Defaults
```

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power changes and the cross-subsystem Pareto optimizer are outside the v1 automatic path. Existing source in those areas may remain for future/read-only use.

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

### Simplified GPU auto-affinity source

The current source now follows ADR 0006:

```text
capture exact original/default state
→ deterministic D3D12 calibration / frozen workload
→ 5 s original non-scored warm-up/reference (benchmark only; no PresentMon/ETW)
→ each eligible physical core:
     apply/restart/verify
     5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
     1 scored screen
     exact rollback
→ rank valid screens by higher 1% low, then 0.1% low, AVG, then lower p99 context
→ re-test best up to three with 2 additional scored runs each
→ rank finalists from three-run medians; unstable repeated 1% lows are rejected
→ apply winner once
→ final 5 s benchmark-only warm-up → 5 s ETW-backed verification capture
→ Keep only with clean ETW + attributable target-only GPU ISR placement
   otherwise exact RestoreOriginal
```

Important current properties:

- Windows default is reference/recovery, not a fixed 3% winner gate.
- CPU0 is eligible.
- Physical-core representatives come from actual topology; no even/odd CPU assumption.
- No active SMT sibling-refinement phase in v1.
- No ABBA/BAAB confirmation loop.
- Missing PresentMon/ETW during screening is visible context and does not by itself abort benchmark ranking.
- If healthy ETW proves off-target placement during screening, that candidate is invalid.
- **Final Keep is stricter:** missing/unhealthy ETW or missing target-only ISR proof restores Original.
- D3D12 controlled frame periods produce AVG / 1% low / 0.1% low; they are a deterministic comparison signal, not claimed to be identical to an arbitrary game's end-to-end frame time.
- PresentMon parses current `FrameTime` or legacy `MsBetweenPresents` for present cadence; `MsBetweenAppStart` is no longer treated as an interchangeable frame interval.
- retained failed PresentMon CSV diagnostics are bounded rather than accumulating without limit.
- development ranking UI now uses 1% low as the primary bar/order and shows 0.1%/AVG/p99 context.

## Verification state right now

### Automated CI

A TDD characterization run on `main` intentionally failed before production changes because the old implementation selected CPU0 by p99 when the new contract expected CPU2 by stronger lows. The legacy 22 tests passed in that run; the temporary direction test was the only behavioral failure. That established a real RED before the production rewrite.

**Fresh exact-final-HEAD green CI is still required after the remaining test/doc reconciliation. Do not treat earlier green runs as evidence for the current HEAD.**

### Physical GPU Gate A

Still **OPEN**.

Historical owner runs proved several safety/substrate properties, including journal-owned apply/rollback and clean exact-original restoration. The most recent pre-ADR-0006 reports do not establish the best CPU under the current simplified method and must not be reinterpreted as such.

Next physical Gate A must prove on one exact clean green revision:

1. every expected physical core receives one scored screening run;
2. best up-to-three receive two additional scored runs;
3. winner is selected by the documented low-FPS order;
4. rollback succeeds between every candidate block;
5. final ETW proves target-only GPU ISR placement before Keep;
6. Stop safely and one supported failure restore exact Original with `unresolved=0`;
7. a repeated whole search is practically reproducible or reports instability explicitly.

Product mutation IPC remains unarmed until this physical gate passes.

## Roadmap progress snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Fresh final CI/docs consistency check |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI v1 preflight |
| 2 Baseline | **ETW engine exists** | Wire the deep baseline into one-button v1 workflow |
| 3 GPU search | **Simplified source implemented** | Canonical test reconciliation, exact-head green CI, physical Gate A + repeat |
| 4 GPU Keep | **Internal verified-keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only topology/evidence exists** | Automatic headroom ranking and controller-selection orchestration |
| 6 USB apply | **Open / gated** | Reversible xHCI mutation + physical apply/verify/rollback evidence |
| 7 Reboot verify | **Recovery primitives exist** | Combined GPU+xHCI pending session + post-login verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated v1 before/after capture/report |
| 9 UX/release | **Development UI/release foundations exist** | One-button product UX, accessibility, signed/package/recovery closure |

## Immediate execution ladder

1. Reconcile the permanent critical tests with ADR 0006 without growing the permanent test-method count.
2. Remove the temporary TDD characterization test once its contract is represented by the canonical GPU session tests.
3. Reconcile remaining canonical methodology/system-design/physical-validation prose with ADR 0006.
4. Obtain a fresh hosted **Tests** success on the exact final `main` HEAD.
5. Rerun physical GPU Gate A on that exact revision.
6. Only after Gate A passes, implement/arm the shared mutation-specific IPC and then the automatic USB/xHCI mutation stages.

## Completion rule

Repository/source completion means every v1 source outcome is implemented or explicitly blocked by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical read-only closure, GPU mutation gates, automatic USB/xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation, and signed package/install/upgrade/uninstall evidence.
