# LatencyPilot Whole-System Optimizer Target Graph

Status: **Authoritative design input for Phases 3–6**  
Last updated: 2026-09-15

LatencyPilot must not become a GPU-only affinity tool. The one-click optimizer is a whole-system experiment orchestrator. It observes every DPC/ISR contributor it can attribute, then applies only narrow, supported, reversible mutations to the active hardware paths that can plausibly affect the current workload.

The core rule is:

```text
Discover actual active path
→ identify dependencies and shared hardware
→ measure
→ choose a narrow supported experiment
→ snapshot exact original state
→ journal
→ apply one change
→ verify actual applied state
→ measure target + guardrails
→ keep or revert
```

A device being present is not enough reason to mutate it. Disabled radios, unused adapters, inactive GPUs and unrelated audio endpoints are inventory/context unless runtime evidence makes them relevant.

## 1. Universal observation, selective mutation

LatencyPilot should not maintain a brittle hardcoded list of "bad drivers". Kernel observation remains universal: every attributable DPC/ISR module is eligible to become a suspect regardless of whether it belongs to a currently supported optimizer domain.

Known latency-sensitive domains are used for topology mapping, guardrails and supported experiments:

- graphics/display adapters;
- audio adapters and active render/capture endpoints;
- keyboard, mouse and other HID input devices;
- USB hubs/controllers, especially xHCI;
- wired network adapters;
- Wi-Fi adapters/radios;
- Bluetooth radios/transports;
- storage/NVMe/SATA/SCSI controllers when they appear in latency evidence;
- system/ACPI/bus devices as context and attribution targets;
- CPU topology, processor groups, SMT and heterogeneous core classes.

Unknown or unsupported contributors remain visible with raw evidence. They must never be converted into guessed tweaks.

## 2. Runtime dependency graph

The optimizer reasons about paths, not isolated rows in Device Manager.

Conceptually:

```text
workload process
 ├─ presentation path
 │   ├─ active DXGI adapter / GPU
 │   ├─ display output / monitor
 │   └─ PresentMon frame/display metrics
 │
 ├─ audio path
 │   └─ active audio endpoint
 │       └─ underlying audio adapter/function
 │           ├─ GPU HDMI/DisplayPort audio
 │           ├─ onboard/PCIe audio
 │           ├─ USB audio → USB hub → xHCI
 │           └─ Bluetooth audio → Bluetooth radio/transport
 │
 ├─ input path
 │   └─ keyboard/mouse/HID
 │       ├─ USB → hub/port → xHCI
 │       └─ Bluetooth → Bluetooth radio/transport
 │
 ├─ network path
 │   └─ active interface
 │       ├─ Ethernet NIC → NDIS/RSS
 │       └─ Wi-Fi adapter/radio → WLAN/NDIS
 │
 └─ CPU execution/interrupt path
     ├─ processor group
     ├─ physical core
     ├─ SMT sibling(s)
     └─ heterogeneous efficiency/performance class
```

Current source captures PnP parent identity and provides a bounded `DeviceRelationshipGraph` for ancestor/shared-parent reasoning. Orchestration must use those relationships rather than treating shared transports as independent.

A combo Wi-Fi/Bluetooth device, USB audio plus mouse on one xHCI controller, or GPU plus HDMI audio are not independent when they share hardware or restart semantics.

## 3. GPU and multiple-adapter systems

Windows can expose several graphics adapters, including integrated GPUs, discrete GPUs and software adapters. LatencyPilot must enumerate all real hardware adapters and resolve which adapter is actually presenting the target workload before arming a GPU mutation.

Current source includes DXGI graphics-adapter identity plus PresentMon graphics-device introspection/correlation. The end-to-end experiment still must fail closed whenever workload-to-adapter identity is not authoritative enough for mutation.

Required behavior:

1. enumerate DXGI adapters and retain stable adapter identity/LUID where available;
2. exclude software render adapters from hardware tuning candidates;
3. correlate the workload/present stream with the active adapter using PresentMon/DXGI evidence;
4. account for hybrid/cross-adapter presentation instead of assuming the dGPU owns every displayed frame;
5. mutate only the resolved target adapter;
6. treat other GPUs as context/guardrails unless the workload uses them;
7. fail closed when adapter identity is ambiguous.

An iGPU plus dGPU is therefore not an error case and does not imply that both should be tuned.

## 4. GPU-backed HDMI/DisplayPort audio

Audio must be tied to the actual active endpoint, not to a generic "sound card" assumption.

Current source can read the default render endpoint roles and walk the Core Audio device-topology connection far enough to retain the connected hardware-topology device ID when Windows exposes it. That evidence must be reconciled with PnP ancestry before it becomes a mutation guardrail.

For systems where sound is rendered through a monitor over HDMI/DisplayPort, the active audio endpoint can belong to the GPU/display-audio path. LatencyPilot must therefore resolve:

```text
default/selected audio render endpoint
→ endpoint topology
→ underlying adapter/function
→ PnP ancestry/shared GPU relationship
```

If the active endpoint is GPU-backed, a GPU restart or interrupt experiment must include audio guardrails such as endpoint continuity, glitch/dropout evidence where available and unexpected endpoint/device reset. It must not independently optimize an unused onboard audio controller just because it is present.

If the active endpoint is USB or Bluetooth, the corresponding transport/controller becomes part of the dependency graph instead.

## 5. Keyboard and mouse latency

Input optimization is not a single registry tweak.

Current source now includes:

- Raw Input device discovery and stable PnP route correlation;
- documented USB hub interface/IOCTL enumeration;
- unique driver-key → hub/port correlation with explicit ambiguity/unavailability;
- exact xHCI controller ancestry where it can be proven;
- bounded host-observable Raw Input timing capture;
- median/p95/p99 interval, observed report rate, tail jitter, long-gap and burst/coalescing evidence;
- xHCI module-attributed DPC/ISR readiness evidence;
- App inspector wiring for route/port evidence and explicit on-demand host timing capture.

The evidence boundary remains strict:

```text
host-observable Raw Input dispatch timing
!= physical switch latency
!= click-to-photon latency
```

The source can therefore characterize the Windows-host input path without claiming a hardware latency measurement it does not have.

A system-changing xHCI/controller-affinity experiment remains deliberately unarmed and its mutation source remains deferred until the shared GPU mutation substrate passes physical Gate A. When that gate is satisfied, any xHCI candidate must still account for collateral devices sharing the controller and preserve exact rollback/recovery semantics.

## 6. Ethernet, Wi-Fi and Bluetooth

Networking is path-aware.

For wired Ethernet, current read-only/readiness source already provides:

- authoritative `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData` state;
- RSS enabled/support state, MSI/MSI-X provider fields, queue/message counts, profile, processor range and processor/indirection evidence when exposed;
- conservative provider→PnP correlation;
- vendor-driver DPC/ISR attribution kept distinct from generic NDIS evidence;
- a controlled local benchmark interpretation contract for RTT, jitter, loss, throughput and CPU guardrails;
- Internet observations treated as supplemental rather than authoritative local adapter evidence.

For heterogeneous CPUs, Windows RSS behavior can itself be topology-aware. LatencyPilot must not blindly force a P-core or E-core policy when the active RSS profile is designed to balance across heterogeneous processors. Windows/default behavior remains a control candidate.

A system-changing RSS/affinity experiment remains deliberately unarmed and its mutation source is deferred until Gate A proves the shared mutation/recovery substrate physically.

For Wi-Fi:

- inventory the adapter even when disconnected/disabled;
- if the radio/interface is off or unused, skip mutation rather than turning it on;
- when active, add WLAN/NDIS connection-quality and latency evidence before candidate testing.

For Bluetooth:

- inventory the radio and dependent input/audio devices;
- if Bluetooth is off and no active workload dependency uses it, skip it;
- when active, treat Bluetooth audio/input transport as a latency domain and guardrail.

Combo Wi-Fi/Bluetooth hardware must be represented as shared hardware when PnP ancestry shows that relationship.

## 7. Storage and other drivers

Storage is not a primary tuning domain merely because it exists, but NVMe/SATA/SCSI/storage drivers can create meaningful DPC/ISR or workload stalls. Therefore:

- storage controllers/devices are part of latency-sensitive inventory;
- storage modules remain eligible suspects from ETW attribution;
- workload I/O context may promote storage into an active domain;
- no storage mutation is allowed until a documented, reversible mechanism with clear guardrails exists.

The same rule applies to system/ACPI/bus drivers: observe broadly, mutate only when a specific supported experiment exists.

## 8. Heterogeneous CPUs: P-cores, E-cores and beyond

LatencyPilot must not hardcode Intel marketing labels into the core model. Windows exposes an `EfficiencyClass`; higher numerical classes represent intrinsically faster but less power-efficient cores, while lower classes represent more efficient cores. This is a relative topology property, not proof that a given interrupt or workload should always run on the highest class.

Current source captures processor topology together with CPU-set state including efficiency/scheduling class, parked/allocated flags and processor-group identity. GPU candidate generation consumes that evidence while remaining bounded and single-group for the current KAFFINITY writer.

Current policy:

- retain physical-core and SMT identity;
- retain processor group identity;
- retain CPU-set availability/parked/allocated context;
- expose heterogeneous-core detection;
- bounded candidate screening must represent distinct efficiency classes instead of silently sampling only one class;
- within a physical core, avoid pretending SMT siblings are independent physical candidates;
- never hard-ban CPU 0;
- measured pressure and repeated outcome decide finalists;
- multi-group machines remain fail-closed for the current single-group GPU affinity writer until a group-correct mutation model exists.

## 9. One-click orchestration order

The intended user experience is one high-level action, but internally it is dependency-aware:

```text
Optimize this PC / Optimize this workload
    ↓
Preflight + current-state discovery
    ↓
Resolve active workload, GPU(s), audio endpoint, input transport, network path
    ↓
Build dependency graph and shared-controller constraints
    ↓
Reuse or acquire valid baseline
    ↓
Require explicit optimizer readiness
    ↓
Rank evidence-backed domains
    ↓
Run one reversible experiment at a time
    ↓
After every mutation: verify state + target + cross-domain guardrails
    ↓
Keep winner or restore exact original state
    ↓
Move to next independent domain only when safe
    ↓
Final combined confirmation
    ↓
Present raw deltas, trade-offs and Restore Baseline
```

The combined optimizer must not stack several unverified changes and then guess which one helped.

Evidence v9 and the App now make a critical distinction explicit before this flow starts:

```text
Valid for comparison
!=
Ready for optimization
```

The GPU optimizer target uses the shared `baseline-quality-v2` + `workload-stability-v1` readiness contract and serializes the resulting `gpu-affinity-v1` eligibility/reason.

## 10. Cross-domain guardrails

Examples of required dependency-aware guardrails:

- GPU affinity change → frame metrics + display latency + GPU-backed audio continuity + total DPC/ISR;
- xHCI/input change → Raw Input timing + USB controller DPC + any audio/storage device sharing that controller;
- NIC/RSS change → RTT/jitter/loss + throughput + CPU pressure + total DPC/ISR;
- Wi-Fi/Bluetooth shared transport change → both active radio-dependent paths;
- CPU/core-placement experiment → target metric plus contention on other active latency-sensitive domains.

A local win with a material collateral regression is `Tradeoff`, not `Improved`.

## 11. Current implementation sequence

Already present in source and therefore **not** future scaffolding:

- durable SQLite mutation journal/recovery substrate;
- processor topology + CPU-set evidence;
- present PnP inventory and parent relationships;
- representative GPU/NIC/xHCI evidence;
- Core Audio default-render route discovery;
- Raw Input device route discovery and bounded host timing;
- documented USB hub/port correlation and xHCI ancestry;
- StandardCimv2 RSS inventory/correlation and local network readiness interpretation;
- DXGI/PresentMon graphics-device correlation;
- PresentMon workload metric capture;
- bounded GPU-affinity candidate generation;
- shared baseline/workload optimizer-readiness contract;
- explicit evidence-v9 workload/optimizer readiness serialization;
- exact original/candidate stored-state apply/revert path;
- exact-target SetupAPI device refresh/restart checks;
- synchronized ETW + raw PresentMon GPU evidence;
- runtime GPU ISR processor-placement verification;
- screening/finalist policy and fixed ABBA+BAAB confirmation source;
- startup recovery classification;
- rollback-biased explicit recovery execution;
- global Restore Baseline planning/execution for retained GPU changes;
- owner-only non-shipping Phase 3 physical-validation harness.

Immediate remaining sequence is now physical-gate driven rather than source-churn driven:

1. close the remaining owner-local **Phase 2 read-only physical validation** on the exact current revision;
2. **Gate A:** physically prove startup/recovery, unresolved-state survival/classification, exact-target restart/reboot-required behavior, one bounded GPU candidate apply → stored verification → runtime ISR placement → exact rollback, and one supported forced-failure recovery while protocol v6 stays read-only;
3. **Gate B:** only after Gate A, implement mutation-specific typed/allowlisted IPC and mutation authorization while keeping the product unarmed during development;
4. **Gate C:** physically validate the real client/App → Service mutation path, including authorization, journal ownership, restart/recovery and exact rollback;
5. **Gate D:** arm the supported user-facing GPU workflow only after Gate C and the required target/guardrail UX are credible;
6. implement and physically validate the supported xHCI/controller-affinity mutation experiment using the proven shared safety substrate;
7. implement and physically validate the supported NIC/RSS mutation experiment;
8. combine only physically proven per-domain experiments into bounded one-click orchestration with Pareto/guardrail handling and Restore Baseline;
9. finish release/accessibility/representative-hardware closure for 1.0.

Until Gate A evidence exists, USB/NIC mutation source and product arming are sequencing targets, not permission to duplicate an unproven mutation path. `PROJECT_STATUS.md` owns the exact current execution ladder and physical blockers.

## 12. Primary references

- Microsoft `PROCESSOR_RELATIONSHIP` / `SYSTEM_CPU_SET_INFORMATION`: heterogeneous core efficiency-class semantics.
- Microsoft CPU Sets: current CPU-set state and assignment APIs.
- Microsoft system-defined device setup classes: Display, Media, Net, HID, Keyboard, Mouse, Bluetooth, USB, storage and system classes.
- Microsoft Raw Input APIs: keyboard/mouse/HID host-observable input.
- Microsoft USB hub/interface documentation: authoritative hub/port/controller correlation semantics.
- Microsoft Core Audio / MMDevice / DeviceTopology: active audio endpoints and adapter topology.
- Microsoft DXGI: multi-adapter enumeration.
- Microsoft RSS/NDIS documentation: processor distribution, RSS profiles and heterogeneous-CPU considerations.
- Intel/GameTechDev PresentMon: per-frame CPU/GPU/display/input metrics, device identity and multi-device telemetry.

Product decisions in this document remain LatencyPilot decisions; external documentation defines platform semantics, not universal optimization winners.
