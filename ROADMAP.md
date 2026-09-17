# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-17

LatencyPilot is complete only when it can safely measure a supported Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and keep or restore supported changes with trustworthy recovery.

`PROJECT_STATUS.md` owns the live execution/evidence state. This roadmap owns the required product outcomes. A checked source item means the repository implementation exists; it does **not** substitute for a physical exit gate.

## Product definition — 1.0

LatencyPilot 1.0 must provide:

- read-only hardware, CPU-topology, device, interrupt and driver evidence;
- ETW DPC/ISR measurement with per-CPU and authoritative module attribution;
- repeatable decision baselines with integrity, duration, sample, noise, drift and workload-stability gates;
- before/after comparison with raw metrics, tail percentiles and explicit uncertainty;
- narrowly supported, reversible interrupt experiments rather than generic tweaking;
- GPU experiment execution with deterministic benchmark evidence, raw PresentMon and runtime-placement guardrails;
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

Current steady-evidence contract:

```text
Protocol:              v6 (observation-only)
Evidence:              latencypilot-evidence-v9
Decision baseline:     baseline-quality-v2
Workload readiness:    workload-stability-v1
Steady GPU eligibility: gpu-affinity-v1 (explicit serialized result)
Quick snapshot:        1 × 5 s diagnostic only
Decision sequence:     5 × 20 s + 5 s pre-settle + 750 ms inter-window settle
Per-window adequacy:   >=95% duration, >=1,000 DPC, >=1,000 ISR, clean integrity
p99.9:                 >=10,000 samples/distribution
Mutation:              unavailable / unarmed
```

The automatic GPU benchmark in Phase 3 is intentionally a different whole-run method and does not reinterpret its phases as `baseline-quality-v2` windows.

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
- [x] workload-stability-v1 activity drift analysis for steady optimizer eligibility, including complete per-window system CPU-busy evidence when available;
- [x] evidence-v9 provenance, RequestId, runtime context, serialized workload stability, explicit steady GPU optimizer eligibility/reason and SHA-256 export verification;
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

## Phase 3 — Safe mutation platform + benchmark-backed GPU experiment

**State: RANKED AUTOMATIC GPU SOURCE IMPLEMENTED; EXACT-HEAD CI + PHYSICAL ARMING OPEN; PUBLIC MUTATION OFF**

Automatic internal workflow:

```text
exact original/default affinity
→ normal-user deterministic D3D12 benchmark calibration
→ freeze worker map/workload/seed
→ 5 s non-scored original warm-up + two decision controls
→ screen every eligible physical core within v1 bound (max 16)
→ for each candidate: journaled apply / exact-target activation / stored verify
→ 5 s non-scored post-transition warm-up
→ two synchronized scored benchmark + ETW + raw PresentMon runs
→ resolved single-adapter target-only ISR placement proof
   (display KMD preferred; labelled dxgkrnl fallback only when unambiguous)
→ candidate run-level frame-p99 spread <=20% for rankability
→ exact rollback before the next candidate
→ rank decision-grade candidates by median run-level frame-p99
→ fresh re-screen of the best up-to-three physical-core candidates
→ SMT sibling refinement of the fresh physical-core winner
→ fixed ABBA + BAAB finalist confirmation, with 5 s non-scored warm-up after each state transition
→ final cancellation boundary
→ Keep the ranked finalist only when confirmation remains decision-grade/repeatable
   otherwise exact RestoreOriginal / RecoveryRequired
```

Original/default Windows affinity is the exact reference/recovery state, **not a minimum-improvement winner gate** for forced-CPU auto-affinity. A valid forced-CPU finalist does not have to beat Windows default by the generic 3% threshold; the default comparison remains visible context. `Inconclusive`/invalid/unstable candidates are never ranked. Passive processor pressure is ordering/context only; CPU0 remains eligible.

### Safety and execution source

- [x] concrete SQLite mutation journal with CAS revisions;
- [x] one unresolved experiment blocks unsafe follow-on mutation;
- [x] explicit Prepared/Applying/Applied/Measuring/AwaitingDecision/Reverting/Reverted/Kept/RecoveryRequired/AbortedBeforeApply states;
- [x] startup recovery re-reads actual machine state;
- [x] unknown/diverged/driver-changed state fails closed;
- [x] interruption-safe logical two-value GPU affinity write with compensation/recovery ownership;
- [x] exact-target device restart source and reboot-required detection;
- [x] owner-only validation harness;
- [x] managed normal-user `LatencyPilot.GpuBenchmark` D3D12 host;
- [x] deterministic multicore worker mapping and one-time adaptive calibration followed by frozen workload;
- [x] D3D12 timestamp query evidence independent of PresentMon GPU-active telemetry;
- [x] pinned standalone PresentMon 2.5.1 console collector with official SHA-256 verification; Gate A does not require a separately installed PresentMon Service/API;
- [x] `Sylvan.Data.Csv` parsing of PresentMon CSV instead of a custom CSV parser;
- [x] raw PresentMon console-frame compatibility boundary and canonical raw-frame percentile interpretation;
- [x] dedicated `latencypilot-gpu-benchmark-v1` evidence and `latencypilot-gpu-auto-affinity-report-v1` report contracts;
- [x] `gpu-affinity-benchmark-v1` readiness/contamination policy with one bounded retry and CPU-busy drift kept as context;
- [x] every eligible physical core screened within v1 bound; >16 systems remain topology-stratified and bounded;
- [x] CPU0 remains eligible and passive pressure cannot pre-select the winner;
- [x] non-scored 5 s post-transition warm-up before candidate/finalist/refinement/confirmation evidence;
- [x] transparent median run-level frame-p99 ranking with no hidden weighted score;
- [x] best up-to-three rankable physical cores receive a fresh apply/warm-up/re-screen before finalist selection;
- [x] winning physical core receives eligible SMT sibling refinement;
- [x] synchronized ETW + standalone raw PresentMon + benchmark evidence per scored trial;
- [x] exact stored-state verification around candidate measurement;
- [x] resolved single-adapter target-only ISR runtime-placement verification with zero resolved off-target ISR; display KMD is preferred and `dxgkrnl` fallback is accepted only when conservatively attributable on a single-adapter system;
- [x] `Inconclusive` evidence cannot enter candidate ranking;
- [x] screening/finalist/refinement phases cannot Keep directly;
- [x] fixed eight-run ABBA+BAAB confirmation;
- [x] repeated-side frame-p99 spread above 20% fails closed as `Inconclusive`;
- [x] explicit named metric/guardrail interpretation without hidden weighted score;
- [x] exact rollback between screening/finalist/refinement candidates and final RestoreOriginal/RecoveryRequired path;
- [x] confirmation failure rollback must prove exact original state; failed verification is preserved with the original failure rather than discarded;
- [x] late cancellation after final comparison still prevents Keep while candidate state is owned;
- [x] development-only **Run GPU Gate A** App flow starts benchmark non-elevated, minimizes main window, shows real candidate/pass/progress/metric state and requests UAC only for the owner helper;
- [x] **Stop safely** source preserves rollback/recovery ownership until terminal verification;
- [x] progress/accessibility source exposes live candidate/phase/status semantics through visible text and UI Automation;
- [ ] exact-final-HEAD hosted **Tests** success for the ranked/standalone-collector revision — **CI evidence required**;
- [ ] owner-local render/taskbar/keyboard/screen-reader inspection of the progress experience — **physical evidence required**;
- [ ] mutation-specific typed product IPC — **blocked by Gate A**;
- [ ] normal-user `Auto-optimize GPU` product arming — **blocked by Gate C/D**.

### Arming gates

- [ ] **Gate A — internal physical benchmark + mutation proof:** exact-green clean revision; normal App/Service path; non-mutating D3D12 benchmark smoke; full bounded ranked candidate search with post-transition warm-ups and video-primary (AVG / 1% low / 0.1% low) ranking per ADR 0005; standalone pinned PresentMon capture as best-effort guardrail without separate service install; fresh top-candidate re-screen; current progress/taskbar/accessibility behavior; candidate stored-state verification with ISR placement proof when ETW is healthy (explicitly flagged when unavailable); exact rollback between candidates; balanced finalist Keep/Restore with repeatability/integrity gate (Regressed restores); Stop safely proof; repeated-search reproducibility/equivalence or explicit inconclusive result; one supported failure/recovery exercise; final known state + zero unresolved.
- [ ] **Gate B — typed mutation IPC:** after Gate A only; mutation-specific typed/allowlisted commands and authorization. `MutationAvailable` remains false during source implementation.
- [ ] **Gate C — physical IPC proof:** real App/client → Service mutation authorization, target identity, journal/recovery and exact rollback.
- [ ] **Gate D — product arming:** expose normal-user `Auto-optimize GPU` only after Gate C and credible target/guardrail UX.

### Exit gate

A supported physical GPU can be tuned through the product path and fully restored, including safe cancellation and supported forced-failure recovery. **OPEN.**

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
- [x] steady workload-stability contract remains available for steady/manual evidence;
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

- Default/target permanent suite size: **10** methods.
- Owner-authorized maximum: **20** methods, only when needed for file-size/maintainability or materially safer durable failure isolation.
- USB, input, NIC/RSS, profile/Pareto, restore, workload-readiness, GPU runtime-placement and installed-Service source-provenance contracts remain independently diagnosable where that separation is useful.
- Temporary/obsolete tests must be removed rather than accumulated; temporary bounded-all-core coverage has been folded into the canonical GPU session contract rather than left as a separate test.
- The exact current method count is established by the latest exact-head successful hosted **Tests** run, not by stale prose.
- Hardware validation, exploratory benchmark repetitions and release checklists are not automated tests.

## Definition of 100%

There are two deliberately separate completion claims:

### Repository/source-complete

All repository-verifiable 1.0 source is implemented and wired, or the remaining implementation is explicitly blocked by a documented physical safety prerequisite. Test-only CI is green on the exact source-complete HEAD. This does **not** mean the product is 1.0.

### True product 1.0 / 100%

Only after all remaining Phase 2 physical checks, Gate A→B→C→D, supported USB/NIC physical experiment evidence, App accessibility/runtime validation, signed package/upgrade/uninstall/recovery validation and representative supported-hardware audit are satisfied may LatencyPilot be tagged 1.0 or described as 100% complete.
