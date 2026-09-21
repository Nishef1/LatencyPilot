# LatencyPilot v1 Interrupt-Affinity Target Graph

Status: **Authoritative v1 topology input; whole-system optimizer design superseded for v1**  
Last updated: 2026-09-21

ADR 0006 narrows the v1 automatic product. ADR 0007 owns the current GPU measurement/search/ranking method. LatencyPilot v1 does **not** attempt to optimize every DPC/ISR-producing subsystem.

Its automatic path is intentionally limited to the GPU interrupt target and the primary input device's exact xHCI/controller path, with exact rollback and runtime verification.

Broad kernel observation remains useful: unsupported contributors can still be shown as evidence. Presence in inventory never grants mutation authority.

## 1. v1 runtime dependency graph

```text
Windows session
 ├─ GPU path
 │   └─ target hardware display adapter
 │       └─ GPU interrupt target logical CPU
 │
 └─ primary input path
     └─ Raw Input device
         └─ PnP ancestry
             └─ USB hub / exact port
                 └─ xHCI interrupt-owning controller
                     └─ selected non-GPU logical CPU
```

CPU topology remains a shared dependency:

```text
processor group
→ physical core
→ logical processor / SMT sibling
→ CPU-set eligibility / allocation context
```

Candidate generation uses Windows topology and current CPU-set evidence. Even/odd CPU-number assumptions are invalid. CPU0 is not globally excluded.

## 2. GPU path

Current Gate A supports only cases where the target GPU can be resolved conservatively without guessing. Multi/hybrid-GPU routing remains fail-closed until the workload-to-adapter route is directly proven.

Current paired-v2 search shape:

```text
bounded Original qualification
→ Stage A: one eligible logical representative per physical core
→ direct Original-before → Candidate → Original-after 10 s local pairs
→ Stage B: refine still-untested SMT siblings on up to 3 promising physical cores
→ Stage C: advance up to 3 logical-CPU finalists
→ 3 independent valid 30 s local pairs per accepted finalist
→ practical-tie-aware finalist authority
→ final ETW target-only ISR placement proof
→ Keep or exact RestoreOriginal
```

Short-screen authority uses paired 1%-low effect. Finalist authority uses median paired 1%-low effect plus the persisted decision floor and guardrails defined by ADR 0007. There is no hidden weighted score and no pseudo-normalized FPS.

Windows default is exact recovery/reference state, not a fixed minimum-improvement opponent.

## 3. Input / USB / xHCI path

Current read-only source provides:

- Raw Input device identity;
- stable PnP ancestry;
- documented USB hub interface/IOCTL enumeration;
- exact unique driver-key → hub/port correlation where available;
- xHCI controller identity;
- host-observable Raw Input timing;
- xHCI module-attributed DPC/ISR evidence.

The evidence boundary remains:

```text
host-observable Raw Input timing
!= physical switch latency
!= click-to-photon latency
```

After a verified GPU Keep, v1 selects a separate CPU from remaining interrupt headroom using DPC duration, ISR duration and tail spikes. Counts remain visible context rather than the sole decision rule. The mutation target is the exact interrupt-owning xHCI/controller, not blindly the leaf mouse.

System-changing xHCI affinity remains unarmed until GPU Gate A physically proves the shared mutation/recovery substrate and the integrated controller runtime-verification path exists.

## 4. Shared-controller guardrails

A USB controller can own more than the selected input device. Before xHCI mutation, LatencyPilot retains the exact controller route and treats other active devices on that controller as collateral context/guardrails. It must not pretend the mouse is an isolated interrupt source when the controller is shared.

## 5. GPU final-placement guardrail

During screening, missing optional ETW/PresentMon evidence may remain explicit context when benchmark-owned frame evidence and stored-state identity are valid. Healthy ETW proving wrong/off-target GPU placement invalidates the candidate.

Final Keep is stricter: it requires clean ETW, attributable GPU ISR samples, selected logical CPU as the target, zero resolved off-target ISR and verified terminal stored state. Missing proof means RestoreOriginal.

## 6. Diagnostic GPU scopes

The developer UI may expose:

- **Full search** — authoritative machine-wide paired-v2 search subject to physical Gate A;
- **Selected CPUs · restore Original** — real paired screening on an exact subset, diagnostic-only, no Keep;
- **Original only · no system changes** — no mutation/restart, measures pre-search Original variability only.

Subset/Original-only paths are methodology tools, not shortcuts around machine-wide physical validation.

## 7. What stays observable but is not v1 automatic mutation

The repository may continue to inventory/measure:

- NIC/RSS/network drivers;
- audio endpoints/adapters;
- Wi-Fi/Bluetooth;
- storage and system drivers;
- profile/Pareto/future workload policies.

These sources are useful for diagnostics and future work, but v1 does not automatically mutate them. There is no generic “optimize every driver” traversal and no hidden cross-domain weighted score.

## 8. Future target graph

Post-v1 work may expand into network, audio or multi-subsystem orchestration only after each domain has a documented supported mutation mechanism, independent physical evidence, exact rollback and clear guardrails. The old whole-system optimizer concept is historical design context, not a v1 completion requirement.

## 9. Current execution order

```text
exact-head green CI
→ physical paired-v2 GPU Gate A
→ repeat full search / explicit instability
→ Stop safely + supported recovery exercise
→ real Windows result/accessibility inspection
→ typed mutation-specific IPC / physical client-service proof
→ normal-user GPU arming
→ automatic USB CPU selection
→ reversible xHCI mutation + physical proof
→ combined one-reboot GPU+xHCI verification
→ before/after UX + Restore original settings
→ release/accessibility/recovery closure
```

`ROADMAP.md` and `PROJECT_STATUS.md` own exact progress/blockers.

## 10. Platform references

- Microsoft processor topology / CPU Sets for processor identity and eligibility.
- Microsoft DXGI/PnP for display-adapter identity.
- Microsoft Raw Input and USB hub/IOCTL documentation for input route topology.
- Microsoft interrupt-affinity policy documentation for supported affinity semantics.
- Microsoft ETW/WPT semantics for runtime DPC/ISR evidence.
- Intel/GameTechDev PresentMon as an independent frame-cadence cross-check.

External docs define platform semantics; local measurement decides the selected CPU on the tested machine.