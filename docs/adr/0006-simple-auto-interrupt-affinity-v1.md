# ADR 0006 — Simple automatic interrupt-affinity v1

Status: **Accepted; GPU measurement/search/ranking superseded by ADR 0011**  
Originally accepted: 2026-09-18  
Reconciled: 2026-09-25

ADR 0006 remains authoritative for the narrow v1 **product scope, mutation/recovery ownership, GPU→xHCI sequencing, public arming gates and completion shape**.

ADR 0011 supersedes the former GPU measurement, screening, ranking and finalist-confirmation details that previously lived here. ADR 0010 remains the historical v5 contract and ADRs 0007–0009 remain earlier historical method contracts. Do not use historical GPU-method text from older revisions of ADR 0006 as current authority.

## Product goal

LatencyPilot v1 automates the useful parts of the manual workflow commonly assembled from AutoGpuAffinity, LatencyMon/ETW and Interrupt Affinity Policy Tool:

```text
quiet/preflight
→ baseline interrupt evidence
→ measure GPU interrupt-affinity hypotheses
→ Keep a verified GPU target or Restore exact Original
→ measure remaining interrupt headroom
→ resolve primary input to its exact xHCI controller
→ choose/apply a separate xHCI interrupt target
→ reboot once when required
→ verify effective GPU + xHCI runtime placement
→ show comparable before/after evidence
→ Restore original settings
```

It is not a whole-system tweak suite.

## Explicit v1 boundaries

Included:

- Windows processor/device/USB topology discovery;
- ETW DPC/ISR attribution and timing evidence;
- controlled GPU benchmark evidence;
- reversible GPU interrupt-affinity mutation;
- exact original-state snapshot, durable journal ownership, rollback and recovery;
- Raw Input → USB → exact xHCI resolution;
- separate xHCI interrupt-headroom selection;
- reversible xHCI affinity once the shared mutation substrate is physically proven;
- one reboot when required;
- runtime placement verification;
- before/after evidence and explicit Restore original settings;
- non-elevated App with a narrow privileged boundary.

Outside the v1 automatic path:

- NIC/RSS mutation;
- audio interrupt-affinity mutation;
- BIOS changes;
- HAGS changes;
- automatic MSI-mode toggles;
- power-plan tuning;
- generic debloating;
- generic cross-subsystem/Pareto optimization;
- undocumented kernel mutation;
- broad multi-vector/MSI-X search inferred only from registry/resource folklore;
- mouse/keyboard `DataQueueSize` tuning;
- timer/HPET or processor-performance-interval tweaks;
- automatic polling-rate changes;
- storage/NVMe interrupt-affinity mutation;
- PCI bridge/root-complex affinity mutation.

Existing read-only diagnostics in those areas may remain, but speculative benchmark/profile/optimizer code must not be kept merely for hypothetical future scope.

### Development-only manual HDAudio MSI exception

Owner-directed development tooling may expose a **manual**, journaled PCI High Definition Audio controller MSI experiment without adding MSI to the automatic v1 optimizer.

Eligibility and Keep are deliberately narrow:

- target must be a present PCI device using the `HDAudBus` service;
- `MSISupported` must already exist as a documented REG_DWORD with value 0 or 1;
- LatencyPilot may set only `MSISupported=1`; it never creates/tunes `MessageNumberLimit`;
- the exact original MSI registry state is journaled before the write;
- Windows must restart the target in place or explicitly request a reboot;
- after activation, allocated interrupt resources must carry `CM_RESOURCE_INTERRUPT_MESSAGE`; stored registry intent alone is not success;
- failed active verification rolls back exact Original or remains recovery-required;
- this lab capability does not arm public mutation IPC and is not an automatic recommendation.

### v1 simplicity rule

GPU and primary-input xHCI are solved **sequentially**, not with a joint weighted score or Pareto allocator. GPU evidence owns the GPU decision. After the GPU reaches a verified terminal state, one fresh bounded quiet ETW capture owns xHCI CPU-headroom selection. GPU ranking is useful context, but it is not reinterpreted as a USB ranking and v1 does not run a second per-CPU USB tournament.

## GPU measurement authority

Current GPU method authority is:

[`0011-restart-canonicalized-gpu-affinity-v6.md`](0011-restart-canonicalized-gpu-affinity-v6.md)

In brief, current source uses:

```text
verify exact Original → one in-place GPU restart with Original unchanged → recreate renderer
→ 3–5 scored Original observations for robust median/MAD variability
→ direct Original-before → Candidate → Original-after local pairs
→ one retry for high local drift while structurally valid evidence remains rankable
→ physical-core representatives with bounded uncertainty-aware refinement
→ observed top four logical CPUs rechecked, plus at most one uncertainty-overlapping fifth challenger
→ top two finalists
→ two shuffled 15 s local pairs per finalist
→ one third 15 s round only while uncertainty still overlaps
→ always rank structurally valid finalist evidence
→ confidence describes uncertainty
→ Keep is a separate guardrail + final target-only kernel-ETW decision
```

New method identity is `gpu-affinity-benchmark-v6`; report schema remains `latencypilot-gpu-auto-affinity-report-v3`. Historical v1–v5 evidence remains historical and is never reinterpreted as v6.

The broad product rule is unchanged: **a measured performance result is not sufficient for Keep; runtime placement and terminal state must verify.**

## Mutation and recovery ownership

Every system-changing operation follows the same ownership model:

```text
capture exact original state
→ create durable journal ownership
→ apply one narrow supported change
→ activate/restart only the target when required
→ verify expected stored state
→ measure/verify runtime behavior
→ Keep or exact Restore
→ verify terminal state
→ resolve journal ownership
```

Required properties:

- exact pre-change values are captured, including existence/type/data where relevant;
- writes are narrow and allowlisted;
- mutation ownership survives interruption/restart;
- rollback uses the captured exact original state rather than a guessed Windows default;
- cancellation is rollback-biased;
- unresolved/diverged state blocks unsafe follow-on mutation;
- recovery re-reads actual machine state rather than trusting stale in-memory intent;
- failure to prove a safe terminal state remains visible and fail-closed.

Never delete or rewrite journal evidence merely to make validation pass.

## Privileged boundary

The normal product App remains non-elevated. Privileged capability is narrow, typed and allowlisted.

Public protocol v6 remains observation-only until physical GPU Gate A closes and the mutation-specific product boundary is deliberately armed.

`ServiceBoundary.MutationAvailable=false` is the current product truth.

There is no generic privileged shell, arbitrary process launcher or arbitrary registry write surface.

## GPU → xHCI sequencing

The automatic v1 path is intentionally sequential rather than a generic multi-subsystem optimizer.

1. Establish/retain one verified GPU state first.
2. Stop the GPU workload and capture one fresh bounded quiet interrupt-headroom window after that GPU state; do not reuse GPU rank as a USB score.
3. Resolve the primary Raw Input route through USB topology to the exact interrupt-owning xHCI controller.
4. Exclude the whole physical core containing a kept GPU target when choosing the xHCI target.
5. Choose xHCI placement from measured interrupt duration/tail evidence, with counts as context rather than cost by themselves.
6. Apply xHCI affinity only after the shared mutation/recovery substrate is physically proven and the integrated controller-specific verification path exists. Runtime attribution must be unambiguous; a shared `USBXHCI.sys` module stream is insufficient when multiple present controllers cannot be distinguished.
7. If xHCI verification fails, restore the xHCI state while preserving a separately proven GPU state when that ownership can be demonstrated safely; otherwise fail closed to the broader baseline.

The selected target is the interrupt-owning controller, not blindly the leaf mouse device.

## Reboot policy

Prefer target device restart/activation when supported and sufficient. A machine reboot is requested only when Windows reports that one is required or when the supported verification contract cannot complete without it.

The product should aggregate required reboot work into **one** user-visible reboot when possible rather than rebooting independently for each subsystem.

Pending-reboot state must be durably owned and resumed by re-reading actual stored/runtime state after login.

## Runtime verification

Keep these layers distinct:

```text
stored interrupt policy
≠ allocated interrupt resources
≠ runtime DPC/ISR placement
```

Stored registry policy proves configuration intent only. ConfigMgr resource data is provenance/context. Runtime ETW evidence owns effective placement claims.

GPU Keep requires three distinct proofs under ADR 0011: the requested stored policy, translated allocated interrupt resources confined to the requested processor mask, and clean **direct display-driver** target-only ISR placement. A shared `dxgkrnl` WDDM ISR stream may remain diagnostic context but is not device-specific enough to authorize Keep.

The integrated xHCI path must independently establish controller-specific runtime placement before that subsystem can be called verified.

## Before/after evidence

The final product report must compare like-for-like evidence where possible and distinguish:

- raw observations;
- derived decision evidence;
- verified terminal state.

Relevant user-facing context may include:

- kept/restored GPU CPU;
- controlled GPU AVG FPS / 1% low / 0.1% low / p99 where comparable;
- xHCI CPU;
- DPC/ISR counts as context;
- interrupt total/tail durations;
- Raw Input host timing where comparable;
- provenance and verification state.

Do not call an unmeasured proxy click-to-photon or network latency.

## Restore original settings

The final product must expose a prominent Restore original settings path that restores the exact captured baseline for every owned v1 mutation and verifies the resulting machine state.

Upgrade/uninstall must not remove required recovery capability while LatencyPilot still owns an unresolved or retained mutation.

## Physical arming gates

Source/hosted CI success is necessary but insufficient.

The dependency chain is:

```text
exact-head green hosted Tests
→ restart-canonicalized observer-isolated v6 GPU physical Gate A
→ whole-search repeatability / explicit instability
→ Stop safely + supported recovery exercise
→ real Windows result/accessibility inspection
→ typed allowlisted product mutation IPC
→ physical App → Service mutation proof
→ integrated xHCI apply/runtime verification
→ combined reboot/resume/recovery
→ final before/after product flow
→ signed package/install/upgrade/uninstall closure
```

Physical GPU Gate A is defined in `docs/PHASE3_PHYSICAL_VALIDATION.md`.

Do not arm public mutation merely because hosted tests are green.

## Evidence honesty

LatencyPilot distinguishes **uncertainty** from **invalid evidence**.

Rules:

- structurally valid candidate evidence is ranked even when the environment is noisy;
- noise lowers `SelectionConfidence`; it does not erase the best-observed CPU;
- missing/invalid evidence stays missing/invalid;
- optional collector failure is not silently converted to zero;
- healthy contradictory placement evidence invalidates the relevant claim;
- practical ties remain explicit while rank 1 remains the best observed estimate;
- UI does not invent a second scoring system;
- source/evidence eligibility is not physical Gate A closure;
- historical evidence remains bound to its historical method/source revision;
- Keep remains independently conservative and may restore Original even when a best-observed CPU exists.

## Test policy

Permanent tests remain deliberately bounded. Add or extend a permanent critical contract only for stable high-blast-radius behavior that is not already covered. Temporary characterization may be created during TDD and removed/folded once the invariant is represented.

Hosted CI is software-contract evidence only. Physical restart/placement/recovery/render/accessibility/package behavior requires owner-local evidence.

## Consequences

### Positive

- one narrow product story instead of a tweak collection;
- exact rollback ownership is central rather than incidental;
- GPU and xHCI decisions remain separately measurable/verifiable;
- public privileged surface stays small;
- ambiguous/noisy evidence fails conservatively;
- future subsystems can be judged against a clear bar rather than being added because they are popular tweaks.

### Tradeoffs

- physical closure is slower than shipping registry guesses;
- the product may Restore Original often when evidence is noisy or placement cannot be proved;
- some supported machines/topologies remain fail-closed until explicitly handled;
- xHCI/product arming is blocked behind GPU physical proof rather than being developed as an independent unsafe shortcut.

These tradeoffs are intentional for an evidence-first latency tool.