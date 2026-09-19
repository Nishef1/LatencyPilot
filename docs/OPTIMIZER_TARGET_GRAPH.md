# LatencyPilot v1 Interrupt-Affinity Target Graph

Status: **Authoritative v1 topology input; whole-system optimizer design superseded for v1**  
Last updated: 2026-09-18

ADR 0006 narrows the v1 automatic product. LatencyPilot v1 does **not** attempt to optimize every DPC/ISR-producing subsystem. Its automatic path is intentionally limited to the GPU interrupt target and the primary input device's exact xHCI/controller path, with rollback and runtime verification.

Broad kernel observation remains useful: unsupported contributors can still be shown as evidence. Presence in inventory never grants mutation authority.

## 1. v1 runtime dependency graph

```text
Windows session
 ├─ GPU path
 │   └─ target hardware display adapter
 │       └─ GPU interrupt target CPU
 │
 └─ primary input path
     └─ Raw Input device
         └─ PnP ancestry
             └─ USB hub / exact port
                 └─ xHCI interrupt-owning controller
                     └─ selected non-GPU CPU
```

CPU topology remains a shared dependency:

```text
processor group
→ physical core
→ logical processor / SMT sibling
→ CPU-set eligibility / topology context
```

Candidate generation must use Windows topology rather than even/odd CPU-number assumptions. CPU0 is not automatically excluded.

## 2. GPU path

Current Gate A supports only cases where the target GPU can be resolved conservatively without guessing. Multi/hybrid-GPU routing remains fail-closed until the workload-to-adapter route is directly proven.

GPU automatic search:

```text
all eligible physical-core representatives
→ one scored screen each after apply/restart/warm-up
→ rank by 1% low → 0.1% low → AVG; p99 context
→ two additional scored re-tests for best up to three
→ finalist repeatability
→ final ETW target-only ISR placement proof
→ Keep or exact RestoreOriginal
```

Windows default is exact recovery/reference state, not a fixed minimum-improvement winner gate.

## 3. Input / USB / xHCI path

Current read-only source already provides:

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

After the GPU winner is fixed, v1 selects a separate CPU from remaining interrupt headroom using DPC duration, ISR duration and tail spikes. Counts are visible context rather than the sole decision rule. The mutation target is the exact interrupt-owning xHCI/controller, not blindly the leaf mouse.

System-changing xHCI affinity remains unarmed until the GPU Gate A mutation/recovery substrate is physically proven. This is sequencing, not a change in v1 scope.

## 4. Shared-controller guardrails

A USB controller can own more than the selected input device. Before xHCI mutation, LatencyPilot must retain the exact controller route and treat other active devices on that controller as collateral context/guardrails. It must not pretend the mouse is an isolated interrupt source when the controller is shared.

## 5. GPU final-placement guardrail

Screening can continue when ETW is unavailable, provided controlled benchmark evidence and stored-state identity are valid. Healthy ETW proving wrong/off-target GPU placement invalidates the candidate.

Final Keep is different: it requires clean ETW, attributable GPU ISR samples and zero resolved off-target ISR. Missing proof means RestoreOriginal.

## 6. What stays observable but is not v1 automatic mutation

The repository may continue to inventory/measure:

- NIC/RSS/network drivers;
- audio endpoints/adapters;
- Wi-Fi/Bluetooth;
- storage and system drivers;
- profile/Pareto/future workload policies.

These sources are useful for diagnostics and future work, but v1 does not automatically mutate them. There is no generic “optimize every driver” traversal and no hidden cross-domain weighted score.

## 7. Future target graph

Post-v1 work may expand into network, audio or multi-subsystem orchestration only after each domain has a documented supported mutation mechanism, independent physical evidence, exact rollback and clear guardrails. The old whole-system optimizer concept is historical design context, not a v1 completion requirement.

## 8. Current execution order

```text
exact-head green CI
→ physical GPU Gate A
→ typed mutation-specific IPC / physical client-service proof
→ normal-user GPU arming
→ automatic USB CPU selection
→ reversible xHCI mutation + physical proof
→ combined one-reboot GPU+xHCI verification
→ before/after UX + Restore original settings
→ release/accessibility/recovery closure
```

`ROADMAP.md` and `PROJECT_STATUS.md` own exact progress/blockers.

## 9. Platform references

- Microsoft processor topology / CPU Sets for processor identity and eligibility.
- Microsoft DXGI/PnP for display-adapter identity.
- Microsoft Raw Input and USB hub/IOCTL documentation for input route topology.
- Microsoft interrupt-affinity policy documentation for supported affinity semantics.
- Microsoft ETW/WPT semantics for runtime DPC/ISR evidence.
- Intel/GameTechDev PresentMon as an independent frame-cadence cross-check.

External docs define platform semantics; local measurement decides the selected CPU on the tested machine.
