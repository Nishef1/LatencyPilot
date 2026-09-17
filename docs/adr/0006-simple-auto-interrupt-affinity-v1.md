# ADR 0006 — Simple automatic interrupt-affinity v1

Status: **Accepted** (owner-directed, 2026-09-18)

Supersedes ADR 0005 for ranking order, finalist confirmation and v1 USB/xHCI product direction.

## Product goal

LatencyPilot v1 automates the useful parts of the manual workflow commonly assembled from AutoGpuAffinity, LatencyMon/ETW and Interrupt Affinity Policy Tool:

```text
quiet/preflight → baseline interrupt evidence
→ benchmark GPU on eligible physical cores
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
2. Generate one eligible logical representative for each physical core from Windows topology; do not assume even/odd CPU numbering and do not ban CPU0.
3. For every physical-core candidate:
   - apply exact GPU interrupt affinity;
   - restart/activate and verify stored state;
   - run a 5 s non-scored warm-up;
   - run **one** scored screening measurement;
   - restore and verify the exact original state.
4. Rank valid screening candidates by:
   1. higher 1% low;
   2. higher 0.1% low;
   3. higher AVG FPS;
   4. lower frame-p99 only as deterministic diagnostic/tie context.
5. Re-test the best up-to-three candidates with **two additional scored runs each** after a fresh apply/restart/warm-up.
6. Rank finalists from the median of all three scored observations. A finalist with materially unstable repeated 1% lows is not rankable.
7. There is no active SMT sibling-refinement phase in v1 and no ABBA/BAAB confirmation loop.

Windows default is the exact recovery/reference state, not an opponent that every forced CPU must beat by a fixed percentage.

## Screening versus final verification

Screening must remain resilient:

- PresentMon is a best-effort independent frame-cadence cross-check.
- Kernel ETW is a best-effort ISR/DPC guardrail during screening.
- Missing PresentMon or missing ETW is recorded visibly and does not by itself abort ranking when the controlled benchmark artifact, stored state and continuity are valid.
- If ETW is healthy and proves wrong/off-target ISR placement, that candidate is invalid.

**Keep is stricter than screening.** After selecting the ranked winner, LatencyPilot applies it once more and performs a final kernel-ETW verification capture. Keep is allowed only when:

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

The normal-user product should expose a small workflow such as `Optimize Interrupt Affinity`, not internal Gate A/B/C terminology. It should show the selected GPU CPU, selected input/xHCI CPU, relevant before/after numbers, confidence/verification state and a prominent `Restore Windows Defaults` action.

The development Gate A UI may retain detailed diagnostics, but ranking bars and summaries must reflect the actual decision order: 1% low first, then 0.1% low and AVG, with p99 as context.
