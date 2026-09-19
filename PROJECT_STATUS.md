# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-19

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
→ GPU logical-CPU screen
→ post-sweep Original drift control
→ noise-aware finalist re-test
→ select by 1% low → AVG → p99 → 0.1% rare-tail context
→ reject frame/interrupt-tail regressions and fall through to the next clean finalist
→ final ETW GPU ISR placement proof
→ measure free CPU interrupt headroom
→ Raw Input → USB → xHCI resolution
→ reversible xHCI affinity
→ one reboot when required
→ verify GPU + xHCI placement
→ before/after + Restore original settings
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
→ each eligible logical CPU:
     apply/restart/verify
     5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
     1 scored screen
     exact rollback
→ 5 s Original warm-up → fresh scored Original control after the sweep; discard the sweep if Original leaves its repeatability band
→ rank valid screens by higher 1% low, then AVG, lower p99, then 0.1% rare-tail context
→ re-test the best three plus every additional candidate inside max(1%, observed Original noise)
→ require stable 3-of-up-to-4 evidence with at most one rejected score
→ 5 s Original warm-up → fresh scored Original control after finalist re-tests; reject finalist evidence if 1%/AVG/p99 drift
→ evaluate finalists in rank order against Original + frame and noise-aware attributable DPC/ISR p99 guardrails
→ apply the highest-ranked clean winner once
→ final 5 s benchmark-only warm-up → 5 s ETW-backed verification capture
→ Keep only with clean ETW + attributable target-only GPU ISR placement
   otherwise exact RestoreOriginal
```

Important current properties:

- Windows default is reference/recovery, not a fixed 3% winner gate.
- CPU0 is eligible.
- Every eligible logical CPU from actual Windows topology is screened; no even/odd CPU assumption and no silent SMT sibling omission.
- No separate SMT sibling-refinement phase is needed because eligible siblings are first-class candidates.
- No ABBA/BAAB confirmation loop; the superseded ABBA/BAAB orchestrator, decision engine, confirmation engine and evidence collector were removed from source.
- The old generic GPU screening/confirmation result models were pruned; the live v1 decision path has one owner: `GpuAutoAffinitySession`.
- A fresh post-sweep Original warm-up + scored control rejects a moving benchmark environment before finalist ranking.
- A second post-finalist Original warm-up + scored control rejects drift in 1% low, AVG or frame-p99 before any Keep decision.
- System CPU busy is measured from Windows system-time snapshots; material drift is surfaced rather than hard-coded false.
- Missing PresentMon/ETW during screening is visible context and does not by itself abort benchmark ranking.
- If healthy ETW proves off-target placement during screening, that candidate is invalid.
- **Final Keep is stricter:** missing/unhealthy ETW or missing target-only ISR proof restores Original.
- D3D12 controlled frame periods produce AVG / 1% low / 0.1% low; they are a deterministic comparison signal, not claimed to be identical to an arbitrary game's end-to-end frame time.
- PresentMon parses current `FrameTime` or legacy `MsBetweenPresents` for present cadence; `MsBetweenAppStart` is no longer treated as an interchangeable frame interval.
- retained failed PresentMon CSV diagnostics are bounded rather than accumulating without limit.
- development ranking UI now uses 1% low as the primary bar/order and shows 0.1%/AVG/p99 context.
- Gate A source state is classified centrally as **EvidenceReady**, **DevelopmentOnly**, or **Blocked** and the App/helper use the same policy.
- a dirty `main` checkout can run an explicitly development-only hardware validation, but its report is stamped `SourceState=DevelopmentOnly` and `GateAClosureEligible=false`.
- wrong branch, invalid/mismatched revision, or an unverifiable final source state cannot produce closure-eligible evidence; only an exact clean `main` revision can do so.

## Verification state right now

### Automated CI

The Gate A source-state change was developed with an explicit RED→GREEN cycle on `main`: Tests run `35434578911` failed because the centralized source assessment did not yet exist, then implementation run `35435018844` passed the consolidated critical contract on Windows. The permanent MSTest entrypoint count did not grow; the new behavior is folded into the existing consolidated audit contract.

The exact final documentation/source HEAD must still be green before it is used for authoritative physical Gate A evidence. Hosted Tests remain software-contract evidence only, not hardware proof.

### Physical GPU Gate A

Still **OPEN**.

Historical owner runs proved several safety/substrate properties, including journal-owned apply/rollback and clean exact-original restoration. The most recent pre-ADR-0006 reports do not establish the best CPU under the current simplified method and must not be reinterpreted as such.

Development convenience and closure evidence are now deliberately separate: a dirty `main` checkout may run Gate A to exercise real hardware, mutation, verification, rollback and recovery, but that report is non-authoritative development evidence and cannot close this gate or arm product mutation. The authoritative run still requires one exact clean green `main` revision from start through final source verification.

Next physical Gate A must prove on one exact clean green revision:

1. every expected eligible logical CPU receives one scored screening run;
2. a fresh post-sweep Original control remains inside the Original repeatability band;
3. the best three plus every candidate inside the measured-noise cutoff receive two additional scored runs;
4. repeatability never hides more than one rejected score and never exceeds four scored attempts per Original/finalist;
5. the highest-ranked finalist that clears Original, frame and available GPU-driver DPC/ISR p99 guardrails is selected;
6. rollback succeeds between every candidate block;
7. final ETW proves target-only GPU ISR placement before Keep;
8. Stop safely and one supported failure restore exact Original with `unresolved=0`;
9. a repeated whole search is practically reproducible or reports instability explicitly.

Product mutation IPC remains unarmed until this physical gate passes.

## Roadmap progress snapshot

| Phase | Current source state | What remains |
| --- | --- | --- |
| 0 Scope/safety | **Source complete** | Exact-final-HEAD hosted Tests after status reconciliation |
| 1 Preflight | **Most primitives exist** | Integrated quiet check + combined GPU/xHCI v1 preflight |
| 2 Baseline | **ETW engine exists** | Wire the deep baseline into one-button v1 workflow |
| 3 GPU search | **Simplified source + source-state UX implemented** | Physical Gate A + repeat on an exact clean green revision |
| 4 GPU Keep | **Internal verified-keep source implemented** | Physical proof, typed product IPC, arming gates |
| 5 USB selection | **Read-only automatic recommendation implemented** | Physical representative-hardware evidence + later normal-user rendering |
| 6 USB apply | **Internal reversible substrate implemented / product-gated** | Physical apply/verify/rollback evidence before public arming |
| 7 Reboot verify | **Recovery/reboot primitives implemented** | Combined GPU+xHCI physical reboot/resume verification |
| 8 Before/after | **Metric/report primitives exist** | Integrated v1 before/after capture/report |
| 9 UX/release | **Development validation UX improved; release foundations exist** | One-button product UX, accessibility, signed/package/recovery closure |

## Immediate execution ladder

1. Obtain hosted **Tests** success on the exact final `main` HEAD after this source/status reconciliation.
2. On the owner machine, use **Development only** mode freely while local changes exist to debug hardware behavior; never treat those reports as physical-gate closure evidence.
3. Commit/clean the checkout, confirm **Evidence-ready**, then run the authoritative physical GPU Gate A on that exact green revision.
4. Repeat the whole search and exercise Stop safely plus one supported failure/recovery path with `unresolved=0`.
5. Only after the physical Gate A passes, arm the typed allowlisted product mutation boundary; then close combined xHCI/reboot/before-after physical validation.

## Completion rule

Repository/source completion means every v1 source outcome is implemented or explicitly blocked by a documented physical safety prerequisite, canonical docs match actual source, and the exact final HEAD is green in hosted Tests.

True v1 completion additionally requires physical read-only closure, GPU mutation gates, automatic USB/xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation, and signed package/install/upgrade/uninstall evidence.


### Post-GPU USB/xHCI recommendation

After a verified GPU winner is kept in Gate A, LatencyPilot now stops the benchmark, captures a 10 s quiet kernel-ETW window, resolves Raw Input mouse routes through exact USB hub/port evidence to xHCI, excludes the whole physical core containing the GPU winner, and ranks remaining logical CPUs by total DPC+ISR duration, p99 interrupt tail, then event count. The recommendation is read-only and is persisted in the Gate A report; ambiguous mouse-to-controller routing returns NotReady rather than guessing.


### GPU finalist measurement shape

The finalist stage now uses two independent re-test rounds. Each shortlisted CPU is freshly applied/restarted/warmed and scored once per round, with deterministic round-order shuffling. The calibrated benchmark process stays alive across the session while its D3D12 renderer/device is recreated after GPU restarts; this holds process/JIT/workload identity constant while isolating each affinity transition.


### Noise-aware GPU selection and buffered renderer

The GPU search now treats <=1% relative differences in 1% low, AVG FPS and frame-p99 as practical ties instead of manufacturing a winner from decimal noise. 0.1% low is allowed to break a remaining tie only when its relative difference exceeds 5%. Screening advances the best three plus every additional candidate within max(1%, observed Original cluster noise) of the third-place cutoff. One fresh Original control after the full sweep rejects time/thermal/background drift before finalist ranking, and a second Original control after finalist re-tests must remain comparable in 1% low, AVG and frame-p99 before Keep. Original/finalist robust sampling is capped at four scored attempts with at most one rejected run; a fifth score cannot rescue evidence with two outliers. When each side has at least three usable attributable runs, GPU-driver DPC/ISR p99 tails are compared per run by median with a noise-aware threshold, and a top-ranked finalist that fails them falls through to the next ranked clean improvement.

The controlled D3D12 renderer now uses a three-buffer flip chain and two frame contexts with per-context command allocators/lists, timestamps and fences. At most two benchmark frames are kept in flight; command resources are reused only after the matching fence completes. This removes the previous full GPU drain after every Present while retaining bounded latency and auditable per-frame GPU timestamp evidence.

## Audit closure status — 2026-09-19

Software/source closure is implemented for F1–F11 plus conservative MSI and reversible xHCI mutation substrate. The live GPU decision path is now consolidated on `GpuAutoAffinitySession`; the superseded ABBA/BAAB execution stack was deleted. Critical contracts cover owner-thread rendering, deterministic noise-aware ranking, post-sweep Original drift rejection, next-clean-finalist fallback, DPC/ISR tail guardrails, serialized mutation/recovery, optional collector semantics, tri-state runtime placement, full eligible-logical-CPU enumeration, primary-input USB identity, composite route correlation, capture-quality gating, driver-wide xHCI attribution, reboot-pending states, exact rollback, and fail-closed Gate A source-evidence eligibility.

**Still not a physical-product completion claim:** public mutation remains disabled until an exact clean green revision passes real Windows hardware validation across the required Intel/AMD and USB/xHCI scenarios. Dirty-development Gate A runs are useful diagnostic evidence but cannot satisfy that gate. Hosted GitHub Actions cannot prove physical interrupt placement, reboot activation, or performance benefit.
