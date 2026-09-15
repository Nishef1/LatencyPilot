# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-15

LatencyPilot is complete only when it can safely measure a supported Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and keep or restore supported changes with trustworthy recovery.

`PROJECT_STATUS.md` owns the live execution/evidence state. This roadmap owns the required product outcomes. A checked source item means the repository implementation exists; it does **not** substitute for a physical exit gate.

## Product definition — 1.0

LatencyPilot 1.0 must provide:

- read-only hardware, CPU-topology, device, interrupt and driver evidence;
- ETW DPC/ISR measurement with per-CPU and authoritative module attribution;
- repeatable decision baselines with integrity, duration, sample, noise, drift and workload-stability gates;
- before/after comparison with raw metrics, tail percentiles and explicit uncertainty;
- narrowly supported, reversible interrupt experiments rather than generic tweaking;
- GPU experiment execution with PresentMon and runtime-placement guardrails;
- USB/xHCI route and high-polling input analysis;
- NIC/RSS inventory, attribution and local-network experiment evidence;
- transparent workload profiles and cross-subsystem guardrails without hidden weighted scores;
- durable journal, crash/restart recovery and verified rollback;
- non-elevated WinUI 3 App with a narrow privileged Service boundary;
- local diagnostics/evidence export and clear Keep/Restore/Inconclusive decisions;
- signed self-contained Windows 11 x64 release with safe install/upgrade/uninstall.

Out of scope unless an ADR explicitly changes it: generic debloating, registry-cleaner behavior, disabling Windows security, arbitrary service disabling, timer/HPET folklore, generic shell execution, undocumented bulk tweak packs or a generic privileged registry/process executor.

## Phase transition map

```text
Phase 0 governance
→ Phase 1 build/comparison foundation
→ Phase 2 trustworthy read-only observation + decision baseline
→ Phase 3 safe mutation substrate + GPU interrupt experiment
→ Phase 4 USB/xHCI + input analysis
→ Phase 5 NIC/RSS analysis/optimization
→ Phase 6 bounded cross-subsystem optimizer/profiles
→ Phase 7 productization/release hardening
→ 1.0
```

ADR 0004 allows later-phase **source implementation** to overlap unfinished physical validation after credible measurement evidence exists. Overlap never arms mutation early and never closes a physical exit gate.

---

## Phase 0 — Governance and safety contract

**State: CLOSED**

- [x] product purpose/non-goals;
- [x] source-available license/contribution/security rules;
- [x] `AGENTS.md` engineering contract;
- [x] architecture and privilege boundaries;
- [x] benchmark methodology;
- [x] GitHub templates/CODEOWNERS;
- [x] roadmap and live status ledger.

### Exit gate

A contributor can determine product intent, safety boundaries and completion criteria without chat history. **Satisfied.**

---

## Phase 1 — Build/comparison foundation

**State: CLOSED**

- [x] .NET 10 SDK and central package management;
- [x] Core/Benchmarking/Protocol/Platform.Windows/Persistence/Service/App boundaries;
- [x] focused permanent test project;
- [x] normal-user WinUI shell;
- [x] explicit no-mutation public state;
- [x] experiment lifecycle and legal transitions;
- [x] finite metric/sample contracts;
- [x] canonical percentile estimator;
- [x] minimum evidence/change policies;
- [x] Improved/Regressed/Tradeoff/NoMeasurableDifference/Inconclusive semantics;
- [x] guardrail regression cannot be hidden by local target improvement.

### Exit gate

Deterministic comparison and lifecycle core exists. **Satisfied.**

---

## Phase 2 — Trustworthy read-only Windows observation

**State: SOURCE COMPLETE; PHYSICAL CLOSURE OPEN**

Current contract:

```text
Protocol:             v6 (observation-only)
Evidence:             latencypilot-evidence-v9
Decision baseline:    baseline-quality-v2
Workload readiness:   workload-stability-v1
Optimizer eligibility: gpu-affinity-v1 (explicit serialized result)
Quick snapshot:       1 × 5 s diagnostic only
Decision sequence:    5 × 20 s + 5 s pre-settle + 750 ms inter-window settle
Per-window adequacy:  >=95% duration, >=1,000 DPC, >=1,000 ISR, clean integrity
p99.9:                >=10,000 samples/distribution
Mutation:             unavailable / unarmed
```

### Inventory, observation and evidence source

- [x] processor-group-aware package/core/logical/SMT topology;
- [x] present PnP inventory with stable instance IDs;
- [x] driver provider/version/INF metadata;
- [x] stored interrupt configuration with availability/error provenance;
- [x] Configuration Manager allocated IRQ/resource evidence with defensive descriptor parsing;
- [x] representative GPU/NIC/xHCI read-only evidence;
- [x] privileged Windows Service observation host;
- [x] typed/versioned local Named Pipe v6;
- [x] malformed/unknown/oversized protocol input fails closed;
- [x] active-console-session authorization and network identity rejection;
- [x] disconnect/protocol misuse cancels owned capture;
- [x] bounded ETW lifecycle, DPC/ISR collection and integrity accounting;
- [x] per-processor and authoritative image/module attribution;
- [x] unresolved/ambiguous image evidence remains unresolved;
- [x] p50/p95/p99/max and p99.9 adequacy rule;
- [x] quick diagnostic and repeated baseline are separate measurement products;
- [x] baseline-quality-v2 five-window quality analysis;
- [x] workload-stability-v1 activity drift analysis for optimizer eligibility, including complete per-window system CPU-busy evidence when available;
- [x] evidence-v9 provenance, RequestId, runtime context, serialized workload stability, explicit GPU optimizer eligibility/reason and SHA-256 export verification;
- [x] App baseline summary separates comparison validity from optimizer readiness in visible/accessibility text rather than color alone;
- [x] adaptive/accessibility source and keyboard accelerators;
- [x] read-only device inspector source;
- [x] consolidated owner-local read-only closure preflight for exact local/remote HEAD, exact-green Tests, Service/journal/stale-ETW/device/USB/RSS/baseline SHA and source-revision provenance;
- [x] owner-local WinApp CLI UIA/screenshot/SHA evidence capture for Light/Dark/High Contrast/TextScale/Narrow/Keyboard review states.

Canonical evidence boundary remains:

```text
stored interrupt configuration
≠ allocated resource assignment
≠ runtime DPC/ISR behavior
```

### Physical Phase 2 obligations

- [x] Real-world valid decision baseline previously captured on clean owner-local source;
- [ ] exact-closure-revision Real-world and Controlled-idle valid five-window baselines + green consolidated read-only audit;
- [ ] representative GPU/NIC/xHCI inspector sanity on current source;
- [ ] attribution plausibility against an independent observer where practical;
- [ ] active-session rejection and App-close/Service-restart/stale-ETW cleanup checks;
- [ ] JSON-visible-data/SHA/source-revision reconciliation recorded by the consolidated audit;
- [ ] Light/Dark/High Contrast, narrow/text scaling, keyboard and screen-reader/UIA pass using captured WinApp evidence plus Accessibility Insights/Narrator review;
- [ ] explicit proof read-only validation performs no unrelated mutation.

### Exit gate

Phase 2 closes only when the remaining owner-local read-only checks above are recorded against the exact source/package under test. **OPEN.**

---

## Phase 3 — Safe mutation platform + GPU experiment

**State: INTERNAL SOURCE IMPLEMENTED; PHYSICAL ARMING OPEN; PUBLIC MUTATION OFF**

Internal workflow:

```text
authoritative stable baseline
→ bounded measured candidates
→ exact original snapshot + journal
→ apply / verify / activate
→ synchronized ETW + raw PresentMon measurement
→ exact rollback between candidates
→ finalist only from screening
→ fixed ABBA + BAAB confirmation
→ Keep only after verified final state
   otherwise RestoreOriginal / RecoveryRequired
```

### Safety and execution source

- [x] concrete SQLite mutation journal with CAS revisions;
- [x] one unresolved experiment blocks unsafe follow-on mutation;
- [x] explicit Prepared/Applying/Applied/Measuring/AwaitingDecision/Reverting/Reverted/Kept/RecoveryRequired/AbortedBeforeApply states;
- [x] startup recovery re-reads actual machine state;
- [x] unknown/diverged/driver-changed state fails closed;
- [x] interruption-safe logical two-value GPU affinity write with compensation/recovery ownership;
- [x] exact-target device restart source and reboot-required detection;
- [x] owner-only validation harness;
- [x] topology/pressure-aware bounded GPU candidates;
- [x] synchronized ETW + raw PresentMon evidence under one interval;
- [x] raw DPC target metric and graphics guardrails;
- [x] attributed GPU ISR runtime-placement verification;
- [x] Gate A placement command fails closed unless the exact stored candidate remains verified before/after a clean capture and direct resolved GPU-driver ISR evidence is confined to the requested processor; ConfigMgr allocated resources remain independent provenance;
- [x] screening cannot Keep directly;
- [x] fixed eight-run ABBA+BAAB confirmation;
- [x] explicit five-way decision interpretation;
- [x] Keep/Restore journal transitions and verified rollback;
- [x] workload-stability-v1 required by shared optimizer readiness;
- [x] App candidate preparation uses actual per-window runtime CPU activity when available, refuses changing/inconclusive/misaligned workload evidence and reports Ready only after bounded candidates actually exist;
- [ ] mutation-specific typed product IPC — **blocked by Gate A**;
- [ ] user-facing one-click GPU mutation — **blocked by Gate C/D**.

### Arming gates

- [ ] **Gate A — internal physical substrate proof:** current exact App/Service build/install/launch; clean journal; controlled unresolved restart/reclassification; exact-target restart/reboot behavior; one candidate apply → stored verification → runtime ISR placement → exact rollback; deliberate supported failure → verified recovery; final exact original + zero unresolved.
- [ ] **Gate B — typed mutation IPC:** after Gate A only; mutation-specific typed/allowlisted commands and authorization. `MutationAvailable` remains false.
- [ ] **Gate C — physical IPC proof:** real App/client → Service mutation authorization, target identity, journal/recovery and exact rollback.
- [ ] **Gate D — product arming:** expose supported one-click mutation only after Gate C and credible target/guardrail UX.

### Exit gate

A supported physical GPU can be tuned through the product path and fully restored including forced-failure recovery. **OPEN.**

---

## Phase 4 — USB/xHCI and input-latency analysis

**State: READ-ONLY/READINESS SOURCE COMPLETE; MUTATION + PHYSICAL EVIDENCE GATED**

- [x] Raw Input identities resolve to stable PnP ancestry;
- [x] documented USB hub interface/IOCTL topology source;
- [x] exact unique driver-key correlation to hub/port with explicit ambiguity/unavailability;
- [x] exact xHCI controller identity retained;
- [x] bounded Raw Input host-report timing capture;
- [x] interval median/p95/p99, observed rate, jitter, gap and burst/coalescing analysis;
- [x] host-observable timing explicitly distinguished from physical click-to-photon latency;
- [x] xHCI DPC/ISR module attribution and readiness evidence;
- [x] target metrics and explicit network/audio/graphics guardrail requirements;
- [x] App read-only inspector surfaces USB route/port evidence;
- [x] App offers explicit on-demand five-second host Raw Input timing capture;
- [ ] supported reversible xHCI/controller-affinity mutation source — **deferred until Gate A proves the shared mutation substrate physically**;
- [ ] physical high-polling route/timing/benchmark/revert evidence.

### Exit gate

At least one high-polling path must be authoritatively identified, measured, experimentally compared and restored on supported hardware. **OPEN due physical/mutation gate.**

---

## Phase 5 — NIC/RSS latency optimization

**State: READ-ONLY/READINESS SOURCE COMPLETE; MUTATION + PHYSICAL EVIDENCE GATED**

- [x] authoritative `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData` reader;
- [x] RSS enabled/support/MSI/MSI-X, queue/message counts, profile, processor range, indirection and processor-array evidence;
- [x] conservative exact provider→PnP correlation with ambiguity preserved;
- [x] vendor-driver DPC/ISR attribution with generic NDIS kept distinct;
- [x] bounded local-network RTT/jitter/loss/throughput/CPU benchmark interpretation contract;
- [x] Internet scope is supplemental and cannot satisfy authoritative local evidence;
- [x] explicit target metrics and throughput/loss/CPU guardrails;
- [x] App read-only inspector surfaces RSS provider/PnP state;
- [ ] supported reversible RSS/affinity mutation source — **deferred until Gate A proves the shared mutation substrate physically**;
- [ ] physical local-network benchmark/apply/revert evidence.

### Exit gate

The physical workflow must distinguish local adapter improvement from path noise and safely restore every supported NIC change. **OPEN due physical/mutation gate.**

---

## Phase 6 — Cross-subsystem optimizer and workload profiles

**State: POLICY/RECOVERY SOURCE COMPLETE; ARMED MULTI-SUBSYSTEM EXECUTION GATED**

- [x] Competitive/Gaming, General and Audio-sensitive versioned profiles;
- [x] raw named metrics/guardrails remain visible;
- [x] no arbitrary weighted composite score;
- [x] Pareto Dominates/Dominated/Equivalent/Tradeoff/Inconclusive relation;
- [x] per-subsystem opt-out without deleting evidence;
- [x] bounded candidate policy inherited from subsystem engines;
- [x] workload-stability-v1 gates optimizer eligibility;
- [x] retained `Kept` changes discoverable newest-first;
- [x] global Restore Baseline planner is fail-closed on unresolved/unknown mutation kinds;
- [x] GPU retained changes reuse the existing verified subsystem rollback path;
- [ ] armed automatic multi-subsystem session — blocked until supported USB/NIC mutation paths have passed their physical prerequisites.

### Exit gate

Auto mode completes a bounded supported multi-subsystem session and every retained change has evidence, provenance and verified rollback. **OPEN due arming/physical gates.**

---

## Phase 7 — Productization and 1.0

**State: RELEASE/RECOVERY SOURCE HARDENED; OWNER-LOCAL RELEASE CLOSURE OPEN**

- [x] self-contained Windows 11 x64 App/Service publish source;
- [x] Inno Setup installer and portable distribution source;
- [x] upgrade checks retained/unresolved recovery state before replacing protected Service recovery tools;
- [x] uninstall checks retained/unresolved recovery state before removing recovery tools;
- [x] `Kept` is correctly unsafe for uninstall until Restore Baseline/recovery returns it to original;
- [x] release identity is exact-main/exact-green-tests/immutable-version gated;
- [x] SHA-256 package checksums and canonical payload manifest source;
- [x] Authenticode signing/verification hook with SHA-256 file digest and RFC 3161 timestamp digest;
- [x] final releases require signing configuration;
- [x] local redacted diagnostic bundle source with no automatic upload;
- [x] App launch-smoke source in owner-local publisher;
- [x] CI remains test-only;
- [x] reproducible WinApp CLI UIA/screenshot/SHA evidence capture source for accessibility review states;
- [ ] owner-local Release build/publish/launch-smoke on final source;
- [ ] real Authenticode signing + verification + timestamp evidence;
- [ ] installer/portable clean-machine install validation;
- [ ] upgrade/uninstall with clean and blocked-recovery cases;
- [ ] reboot/crash/recovery validation;
- [ ] accessibility/keyboard/UIA physical pass;
- [ ] representative NVIDIA/AMD/USB/NIC paths where hardware is available;
- [ ] final docs/package/source identity audit.

### Exit gate

1.0 installs cleanly, runs the supported workflows, survives interruption, restores managed state and uninstalls without unexplained residue. **OPEN pending owner-local closure.**

---

## Permanent-test policy

- Default/target permanent suite size: **10**.
- Current durable suite: **18 tests**.
- Owner-authorized maximum: **20**, only when needed to keep test files `<=1200` lines or when separate subsystem contracts materially improve durable failure isolation.
- Current USB, input, NIC/RSS, profile/Pareto, restore, workload-readiness, GPU runtime-placement and installed-Service source-provenance contracts are intentionally separate under that authorization; do not merge them merely to hit the target number.
- Temporary/obsolete tests must be removed rather than accumulated.
- Hardware validation, exploratory benchmark runs and release checklists are not automated tests.

## Definition of 100%

There are two deliberately separate completion claims:

### Repository/source-complete

All repository-verifiable 1.0 source is implemented and wired, or the remaining implementation is explicitly blocked by a documented physical safety prerequisite. Test-only CI is green on the exact source-complete HEAD. This does **not** mean the product is 1.0.

### True product 1.0 / 100%

Only after all remaining Phase 2 physical checks, Gate A→B→C→D, supported USB/NIC physical experiment evidence, App accessibility/runtime validation, signed package/upgrade/uninstall/recovery validation and representative supported-hardware audit are satisfied may LatencyPilot be tagged 1.0 or described as 100% complete.
