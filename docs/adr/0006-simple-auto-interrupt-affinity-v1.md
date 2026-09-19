# ADR 0006 — Simple automatic interrupt-affinity v1

Status: **Accepted** (owner-directed, 2026-09-18)

Supersedes ADR 0005 for ranking order, finalist confirmation and v1 USB/xHCI product direction.

## Product goal

LatencyPilot v1 automates the useful parts of the manual workflow commonly assembled from AutoGpuAffinity, LatencyMon/ETW and Interrupt Affinity Policy Tool:

```text
quiet/preflight → baseline interrupt evidence
→ benchmark GPU on every eligible logical CPU
→ keep the best repeatable GPU core
→ choose a separate low-interrupt-load CPU for the input xHCI controller
→ apply supported reversible policies
→ reboot once when required
→ verify effective runtime placement and show before/after evidence
```

It is not a whole-system tweak suite. NIC/RSS, audio, BIOS, HAGS, MSI-mode and power-plan mutation are outside the v1 automatic path.

## GPU benchmark signal

The built-in D3D12 workload records its own **controlled loop frame period**. This period includes the workload's CPU recording, GPU execution/present path and the benchmark's synchronization policy. It is intentionally deterministic and useful for comparing affinity candidates, but it is not claimed to be identical to an arbitrary game's end-to-end frame time.

For each scored run LatencyPilot derives:

- AVG FPS;
- 1% low FPS;
- 0.1% low FPS;
- frame-p99 as diagnostic context.

LatencyPilot defines lows from the controlled benchmark's frame-period distribution and documents that method directly; it does not claim bit-for-bit equivalence with AutoGpuAffinity's historical low calculation.

## Candidate search

1. Capture one non-scored original/default warm-up to establish benchmark/workload continuity.
2. Generate one candidate for every eligible logical processor from Windows topology; do not assume even/odd CPU numbering, do not ban CPU0, and do not silently collapse SMT siblings.
3. For every logical-CPU candidate:
   - apply exact GPU interrupt affinity;
   - restart/activate and verify stored state;
   - run a 5 s non-scored warm-up (benchmark only; no PresentMon/ETW);
   - run **one** scored screening measurement;
   - restore and verify the exact original state.
4. After the full logical-CPU sweep, run one fresh 5 s non-scored Original warm-up, then capture one fresh scored Original control. If its 1% low leaves the Original ±3% repeatability band, discard the sweep and retain exact Original rather than ranking across time/thermal/background drift.
5. Rank with explicit practical-equivalence margins rather than false precision:
   1. 1% low, treating <=1% relative difference as tied;
   2. AVG FPS, treating <=1% as tied;
   3. lower frame-p99, treating <=1% as tied;
   4. 0.1% low only when the relative difference exceeds 5%;
   5. deterministic passive topology/pressure fallback only if the measured metrics remain tied.
6. If post-sweep Original 1%-low noise exceeds 15%, retain the complete screening table but skip exhaustive finalist confirmation and RestoreOriginal; otherwise re-test the best three plus candidates within **min(3%, max(1%, observed Original noise))** of the third-place 1%-low cutoff, capped at five finalists. Finalist order is deterministically shuffled and each finalist gets a fresh apply/restart/warm-up, one 30 s scored run, and exact rollback in each round.
7. Prefer the tightest stable three-run cluster within ±3%. Original/finalist sampling is capped at four scored attempts. If four valid runs still do not form the preferred cluster, retain all four and carry their observed per-metric variance into shortlist, drift and Keep thresholds instead of terminating the search.
8. Run a second 5 s non-scored Original warm-up after finalist re-tests, then capture a second scored Original control. Its 1% low, AVG and frame-p99 must remain inside the Original repeatability band or finalist evidence is discarded and exact Original is retained.
9. Evaluate finalists in ranking order against exact Original. AVG, frame-p99 and 0.1% low remain guardrails. When both sides provide at least three usable interrupt-tail runs, GPU-driver DPC/ISR p99 is evaluated per run and the median regression must remain within max(10%, observed Original tail noise, observed finalist tail noise). A rejected first-place finalist does not prevent the next ranked clean improvement from being considered.
10. Eligible SMT/hyperthread siblings are first-class logical-CPU candidates in the main sweep. There is no separate SMT-refinement phase and no ABBA/BAAB confirmation loop.

Windows default is the exact recovery/reference state, not an opponent that every forced CPU must beat by a fixed percentage.

## Benchmark process lifetime

LatencyPilot intentionally keeps one calibrated benchmark process alive for the complete GPU search. Its renderer uses a three-buffer flip chain and two frame contexts, so the CPU can keep up to two controlled frames in flight without the old full-fence drain after every Present. A GPU configuration restart invalidates the D3D12 device, so the renderer/device is recreated once immediately after each affinity-triggered restart. The subsequent non-scored warm-up and scored run reuse that same recreated renderer/device, while process identity, frozen workload, seed and worker map remain unchanged.

This differs from launching a fresh benchmark subject for every candidate. Re-launching would reintroduce process startup, .NET JIT and cold-cache state as additional variables. The controlled process remains fixed; only GPU interrupt affinity and the required D3D12 device recreation change.

## Screening versus final verification

Screening must remain resilient:

- PresentMon is a best-effort independent frame-cadence cross-check.
- Kernel ETW is a best-effort ISR/DPC guardrail during screening.
- System CPU busy is measured from the existing Windows system-time snapshots and reported when it drifts materially from the Original trials.
- Missing PresentMon or missing ETW is recorded visibly and does not by itself abort ranking when the controlled benchmark artifact, stored state and continuity are valid.
- If ETW is healthy and proves wrong/off-target ISR placement, that candidate is invalid.
- Comparable GPU-driver DPC/ISR p99 evidence participates only as a Keep guardrail; sparse/missing samples are not manufactured into a regression claim.

**Keep is stricter than screening.** After selecting the ranked winner, LatencyPilot applies it once more and performs a fresh 5 s benchmark-only warm-up and then a final kernel-ETW verification capture. Keep is allowed only when:

- exact stored candidate state is verified before/after the capture;
- ETW integrity is clean with no event loss;
- attributable GPU ISR samples exist;
- ISR execution is confined to the selected logical processor with zero resolved off-target ISR.

If final placement cannot be proved, LatencyPilot restores and verifies the exact original state instead of keeping an unverified winner.

## PresentMon contract

The standalone pinned PresentMon collector stays an independent cross-check; a separately installed PresentMon Service is not required.

PresentMon's documented fields are not interchangeable:

- `MsBetweenPresents` is the interval between Present() calls;
- `MsBetweenAppStart` describes a different CPU frame boundary.

The console parser therefore accepts the current `FrameTime` field or legacy `MsBetweenPresents` for frame cadence and must not silently substitute `MsBetweenAppStart` as the same metric. Failed raw CSVs are useful for owner diagnostics but retention is bounded rather than accumulating forever.

## USB/xHCI direction

The second half of the v1 product workflow is automatic **after the GPU mutation substrate passes physical Gate A**:

1. resolve the primary Raw Input device through PnP/USB topology to its exact xHCI interrupt-owning controller;
2. measure per-CPU DPC/ISR load after the GPU winner is fixed;
3. exclude the GPU winner by default;
4. choose USB CPU headroom from DPC duration + ISR duration + tail spikes, with counts shown as context rather than treated as the sole truth;
5. apply a supported reversible xHCI/controller affinity policy;
6. reboot once when required and verify effective runtime placement;
7. restore the baseline if verification fails.

The existing read-only USB/xHCI topology and timing work is reused. Product USB mutation remains **unarmed** until the shared GPU mutation/recovery substrate has passed its physical gate; this is sequencing, not a change in the v1 product goal.

## UX direction

The normal-user product should expose a small workflow such as `Optimize Interrupt Affinity`, not internal Gate A/B/C terminology. It should show the selected GPU CPU, selected input/xHCI CPU, relevant before/after numbers, confidence/verification state and a prominent `Restore original settings` action.

The development Gate A UI may retain detailed diagnostics, but ranking bars and summaries must reflect the actual decision order: 1% low first, then AVG FPS, then lower frame-p99, with 0.1% low used only as rare-tail tie context.
