# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-15

LatencyPilot is complete only when it can safely measure a Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and let the user keep or revert supported changes with trustworthy recovery.

A phase closes only when its exit gate is satisfied with current evidence. `PROJECT_STATUS.md` is the live execution ledger; this roadmap defines required direction and scope.

## Product definition — 1.0

LatencyPilot 1.0 must provide:

- read-only hardware, CPU-topology, device, interrupt and driver inventory;
- ETW-based DPC/ISR measurement with per-CPU and per-module attribution;
- repeatable decision baselines with capture-integrity, duration, sample, noise and drift gates;
- before/after comparison with raw metrics, tail percentiles and explicit uncertainty;
- safe reversible interrupt-affinity/MSI experiments for supported devices;
- GPU experiment workflow with PresentMon guardrails;
- USB/xHCI and high-polling input analysis;
- NIC/RSS analysis and supported network experiments;
- cross-subsystem guardrails;
- workload profiles without hiding raw metrics or trade-offs;
- durable experiment journal, crash/reboot recovery and verified rollback;
- non-elevated WinUI 3 UI with a narrow privileged Service boundary;
- history, diagnostics export and clear Keep/Revert/Inconclusive decisions;
- signed self-contained Windows 11 x64 release with install/uninstall and upgrade safety.

Out of scope for 1.0 unless separately approved by ADR: generic debloating, registry-cleaner behavior, disabling Windows security, arbitrary service disabling, timer/HPET folklore, generic shell execution or undocumented bulk tweak packs.

## Phase transition map

```text
Phase 0 governance
→ Phase 1 build/comparison foundation
→ Phase 2 trustworthy read-only observation + decision baseline
→ Phase 3 safe mutation substrate + GPU interrupt experiment
→ Phase 4 USB/xHCI + input analysis
→ Phase 5 NIC/RSS optimization
→ Phase 6 bounded cross-subsystem optimizer/profiles
→ Phase 7 productization/release hardening
→ 1.0
```

ADR 0004 allows later-phase **source implementation** to overlap remaining physical validation once a valid targeted Real-world baseline exists. Overlap never arms mutation early and never counts as closing an unfinished phase.

---

## Phase 0 — Charter, governance and safety contract

**State: CLOSED**

- [x] purpose/non-goals documented;
- [x] source-available personal-use/non-commercial license;
- [x] contribution/CLA/security policy;
- [x] `AGENTS.md` engineering contract;
- [x] architecture/privilege boundaries;
- [x] benchmark methodology;
- [x] GitHub templates/CODEOWNERS;
- [x] roadmap/live status ledger.

### Exit gate

A contributor can determine product intent, non-goals, architecture, contribution rules and completion criteria without chat history.

---

## Phase 1 — Buildable foundation + comparison core

**State: CLOSED**

### Foundation

- [x] .NET 10 SDK pinned;
- [x] solution/shared build settings;
- [x] Core, Benchmarking, Protocol, Platform.Windows, Service and App boundaries;
- [x] one focused permanent-test project;
- [x] first normal-user desktop slice;
- [x] explicit no-mutation state.

### Comparison/domain contracts

- [x] experiment lifecycle/legal transitions;
- [x] metric direction/sample contracts;
- [x] finite-input validation;
- [x] canonical deterministic percentile estimator;
- [x] minimum-sample/minimum-change policy;
- [x] explicit Improved/Regressed/Tradeoff/NoMeasurableDifference/Inconclusive verdicts;
- [x] guardrail regression cannot be hidden by local target improvement;
- [x] structurally insufficient evidence is Inconclusive.

Historical Phase 1 release/build evidence remains historical. Current hosted CI is intentionally test-only.

---

## Phase 2 — Trustworthy read-only Windows observation

**State: PHYSICAL CLOSURE IN PROGRESS; LATER SOURCE WORK ALLOWED BY ADR 0004**

Current contract:

```text
Protocol:             v6
Evidence:             latencypilot-evidence-v8
Quick snapshot:       1 × 5 s diagnostic only
Decision baseline:    baseline-quality-v2
                      5 × 20 s windows
                      >=95% actual/request duration
                      >=1,000 DPC and >=1,000 ISR events/window
p99.9:                >=10,000 samples/distribution
Mutation:             unavailable/unarmed
Permanent tests:      10 current; conditional owner-authorized max 20
Test source files:    <=1200 lines each
```

### 2.1 Inventory and evidence provenance

- [x] processor-group-aware package/core/logical-processor/SMT topology implementation;
- [x] physical topology observed on the owner's Windows 11 8C/16T target during evidence capture;
- [x] present PnP inventory with stable instance IDs;
- [x] driver provider/version/INF metadata;
- [x] stored interrupt configuration with availability/error provenance;
- [x] allocated IRQ/resource capture through Configuration Manager;
- [x] defensive explicit allocated-IRQ descriptor parsing rather than blind struct reinterpretation;
- [x] optional per-device failures degrade to partial evidence rather than erasing the device;
- [x] representative GPU/display, network and actual `USBXHCI` evidence surfaces;
- [ ] physical representative GPU/NIC/xHCI inspector validation;
- [ ] authoritative line-vs-message assigned-interrupt distinction only if Windows exposes it through a trustworthy assigned-resource source.

Canonical semantic boundary:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

### 2.2 Privileged read-only observation

- [x] privileged Windows Service observation host;
- [x] typed/versioned local Named Pipe protocol v6;
- [x] fail-closed malformed/unknown/oversized framing;
- [x] no generic shell/process/registry execution primitive;
- [x] network identities denied;
- [x] ACL narrowed to required local identities;
- [x] connected client session must match active console session;
- [x] inability to establish client session identity rejects access;
- [x] disconnect/protocol misuse cancels active capture;
- [x] bounded operation deadlines;
- [x] controlled ETW session lifecycle/cleanup;
- [x] DPC + ISR collection;
- [x] per-processor aggregation;
- [x] authoritative image-lifetime/rundown-aware module attribution;
- [x] ambiguous/unresolved addresses remain unresolved;
- [x] bounded module/unresolved contributor lists;
- [x] p50/p95/p99/max using the canonical estimator;
- [x] p99.9 only at >=10,000 samples for that distribution;
- [x] capture integrity includes ETW loss, invalid latency/image events and event-limit state;
- [x] unique capture RequestId retained through logs/protocol/evidence;
- [x] physical Service → ETW → IPC path produced clean evidence on the owner's target;
- [ ] active-session rejection validation with a second local session where practical;
- [ ] attribution plausibility against an independent observer where practical.

### 2.3 Measurement products and baseline quality

#### Quick diagnostic snapshot

- [x] one five-second snapshot flow;
- [x] purpose explicitly `quick-diagnostic-snapshot` in evidence-v8;
- [x] used for integrity, attribution, concentration and hypothesis generation;
- [x] UI avoids presenting it as a health/stability/optimization verdict.

#### Repeated decision baseline

- [x] workload warmed/repeatable before sequence start where applicable;
- [x] five-second settle before window 1;
- [x] exactly five 20-second authoritative windows;
- [x] 750 ms inter-window settle;
- [x] low-observer-activity sequencing;
- [x] each requested window >=20,000 ms and actual duration >=95%;
- [x] clean capture integrity;
- [x] each DPC/ISR metric >=1,000 events/window;
- [x] bounded noise, drift and extreme-window gates;
- [x] contiguous WindowNumber is the sequence authority;
- [x] best-effort runtime CPU/power provenance;
- [x] partial/short/lossy/undersampled/noisy/drifted sequence cannot become Valid;
- [x] physical Real-world valid decision baseline on clean revision `a4b4ff36c875982d5a263665860853462d0b055b`;
- [ ] physical Controlled-idle valid decision baseline;
- [ ] thermal warning only if a trustworthy low-overhead source is found and evidence shows it changes decisions.

### 2.4 Evidence and UX

- [x] evidence-v8 with purpose/product/protocol/source/scenario/runtime/topology provenance;
- [x] full bounded processor/module/unresolved aggregates;
- [x] baseline capture/window alignment validation;
- [x] SHA-256 after save and independent `scripts/Verify-Evidence.ps1`;
- [x] strict clean-capture and valid-baseline verifier gates;
- [x] keyboard accelerators Ctrl+R/O/B/E;
- [x] High Contrast/theme/accessibility metadata source;
- [x] adaptive narrow/wide layout source;
- [x] representative device-evidence inspector;
- [x] clean/dirty source provenance surfaced in header;
- [x] low-frequency baseline progress/ETA;
- [ ] physical JSON-vs-visible-evidence/SHA/source-revision audit;
- [ ] physical warning/sample-insufficient/scenario/readiness state validation;
- [ ] physical narrow-window/text-scaling/keyboard/screen-reader sanity;
- [ ] physical device-inspector sanity.

### Phase 2 exit gate

Phase 2 closes only after the remaining read-only physical checks are reconciled. ADR 0004 changes development sequencing, not the meaning of completion.

---

## Phase 3 — Safe mutation platform + one-click GPU interrupt experiment

**State: GPU EXECUTION SOURCE IMPLEMENTED; PHYSICAL ARMING IN PROGRESS; PRODUCT MUTATION NOT ARMED**

Primary target:

```text
Optimize GPU
→ authoritative preflight/baseline
→ bounded candidates
→ journaled apply/verify/measure/revert
→ balanced finalist confirmation
→ Keep best or restore exact original state
```

### 3.1 Safety substrate

- [x] narrow privileged Service and typed/versioned Named Pipe infrastructure;
- [x] generic privileged shell/registry/process execution prohibited and absent;
- [x] concrete SQLite mutation journal with atomic transactions and compare-and-swap revisions;
- [x] unresolved journal blocks another mutation experiment;
- [x] explicit Prepared/Applying/Applied/Measuring/AwaitingDecision/Reverting/Reverted/Kept/RecoveryRequired/AbortedBeforeApply states;
- [x] RecoveryRequired can proceed only toward rollback in journal v1;
- [x] Service startup re-reads/classifies actual stored state for unresolved known GPU experiments;
- [x] fail-closed original/candidate/diverged/unknown recovery planner;
- [x] shared recovery assessment re-reads actual state immediately before recovery decisions;
- [x] rollback-biased recovery executor refuses unknown/diverged/driver-changed state;
- [x] owner-only physical-validation harness exists without product mutation IPC;
- [x] interruption-safe two-value GPU affinity write/restore semantics with exact compensation or unresolved recovery ownership;
- [ ] mutation-specific typed protocol commands + authorization/allowlist — blocked by Gate A;
- [ ] interrupted/pending experiments survive Service restart/reboot end to end on physical hardware;
- [ ] verified forced rollback/recovery on physical hardware;
- [x] source-level reboot-required detection keeps an experiment unresolved rather than claiming activation;
- [ ] physical reboot-required recovery proof.

### 3.2 First reversible GPU experiment

- [x] exact stored-state snapshot for DevicePolicy/AssignmentSetOverride including missing values and original kinds/bytes;
- [x] apply/restore restricted to present SetupAPI display adapters and documented affinity-policy values;
- [x] stored-state verification after apply and restore;
- [x] one-processor-group v1 KAFFINITY boundary;
- [x] topology-aware physical-core candidate generation from measured per-window DPC+ISR pressure;
- [x] CPU0 is not hard-excluded;
- [x] bounded candidate count defaults to four;
- [x] exact-target `DIF_PROPERTYCHANGE` / `DICS_PROPCHANGE` source with restart/reboot-required handling;
- [ ] physical exact-target restart/reboot-required validation;
- [x] runtime GPU ISR-placement evidence analyzer keeps stored policy distinct from effective placement;
- [x] runtime placement evidence is integrated into internal candidate evidence: attributed GPU ISR must appear on target CPU and none may appear off-target;
- [ ] physical runtime/effective interrupt placement validation;
- [x] journaled transaction prepares before apply and keeps incomplete rollback unresolved;
- [x] mutation remains unarmed until physical safety passes;
- [x] bounded candidate screening loop;
- [x] deterministic screening nominates only a finalist;
- [x] synchronized ETW + raw PresentMon capture under one requested interval/deadline;
- [x] raw DPC duration target samples integrated into the mutation experiment;
- [x] raw PresentMon frame-time / CPU-GPU busy-wait / GPU-display latency / dropped-frame guardrails integrated where exposed;
- [x] fixed eight-run ABBA + BAAB finalist confirmation with identity/duration/sample/integrity/state/noise/drift gates;
- [x] explicit Improved/Regressed/Tradeoff/NoMeasurableDifference/Inconclusive interpretation;
- [x] source-level Keep/Restore with raw deltas/provenance and verified journal transitions;
- [ ] relevant USB/network/audio/stability guardrails beyond currently available graphics/ETW evidence;
- [ ] forced-failure rollback on supported physical hardware.

MSI/MSI-X is deliberately not bundled into the first GPU affinity mutation. It is a separate future supported experiment only after applicability, actual state and rollback are authoritative.

### 3.3 Mutation arming gates

```text
Gate A — internal physical substrate proof
  owner-only harness; protocol v6 remains read-only
  prove journal/recovery, exact-target restart/reboot handling,
  candidate apply/runtime evidence/exact rollback and forced-failure recovery

Gate B — typed mutation IPC implementation
  after Gate A only; mutation-specific typed/allowlisted commands and authorization
  MutationAvailable remains false

Gate C — physical IPC boundary proof
  validate real App/client → Service mutation path and recovery/rollback

Gate D — product arming
  expose supported one-click mutation only after Gate C and credible evidence UX
```

### Phase 3 exit gate

A supported physical GPU can be tuned through one one-click bounded journaled experiment and safely restored, including forced-failure rollback, with target metrics and relevant guardrails. This remains **open** until the physical Gate A→B→C→D sequence is satisfied.

---

## Phase 4 — USB/xHCI and input-latency analysis

**State: SOURCE DISCOVERY STARTED; AUTHORITATIVE ROUTE/TIMING/EXPERIMENT READINESS IN PROGRESS**

- [x] Raw Input device identities resolve to stable PnP ancestry/active input routes;
- [ ] authoritative HID → exact hub/port → xHCI mapping using documented USB hub interfaces/IOCTLs;
- [ ] host-observable Raw Input report interval/jitter/long-gap/coalescing/burst analysis;
- [ ] USB/xHCI ETW correlation;
- [ ] exact controller DPC/ISR attribution;
- [ ] host-observable input timing explicitly distinguished from physical end-to-end latency;
- [ ] supported reversible xHCI/controller-affinity experiment source using the existing journal/recovery discipline;
- [ ] target metrics plus collateral guardrails;
- [ ] physical high-polling route/benchmark/revert evidence.

### Exit gate

At least one high-polling input/controller path is authoritatively identified, measured and compared through a reversible xHCI experiment without overstating host-observable timing as physical click-to-photon latency.

---

## Phase 5 — NIC/RSS latency optimization

**State: SOURCE IMPLEMENTATION NOT YET COMPLETE**

- [ ] authoritative StandardCimv2 NIC/RSS capability/current-state inventory;
- [ ] RSS processor/queue/indirection distribution;
- [ ] exact NDIS/vendor-driver DPC/ISR attribution;
- [ ] controlled local-network latency/jitter/loss benchmark contract;
- [ ] supported reversible RSS/affinity experiment source using journal/recovery discipline;
- [ ] throughput/loss/CPU guardrails;
- [ ] Internet tests remain supplemental rather than authoritative local-network evidence;
- [ ] physical local-network benchmark/apply/revert evidence.

### Exit gate

The tool can distinguish a local networking improvement from path noise and revert every supported NIC change.

---

## Phase 6 — Cross-subsystem optimizer and workload profiles

**State: NOT YET SOURCE-COMPLETE**

- [ ] Competitive/Gaming, General and Audio-sensitive profiles;
- [ ] raw metrics always visible;
- [ ] bounded candidate search/pruning;
- [ ] repeated finalists;
- [ ] Pareto/trade-off representation without arbitrary weighted score;
- [ ] no overwrite of user-kept state without a new journaled experiment;
- [ ] global Restore Baseline;
- [ ] per-subsystem opt-out.

### Exit gate

Auto mode completes a bounded multi-subsystem session and every retained change has individual evidence, provenance and rollback state.

---

## Phase 7 — Productization and 1.0

**State: FOUNDATION EXISTS; HARDENING NOT YET CLOSED**

- [ ] installer/uninstaller and Service lifecycle hardened against unresolved recovery state;
- [ ] self-contained Windows 11 x64 package source reconciled to intended payload;
- [ ] deterministic signing/release provenance/checksum hooks;
- [ ] safe upgrade of journal/history;
- [ ] uninstall restoration of active managed changes;
- [ ] redacted diagnostic bundle;
- [ ] accessibility/keyboard-navigation physical pass;
- [ ] no unexplained admin prompts;
- [ ] clean-machine validation;
- [ ] reboot/crash/recovery validation;
- [ ] representative NVIDIA/AMD/USB/NIC paths exercised where hardware is available;
- [ ] docs match actual capabilities/limitations.

### Exit gate

1.0 installs cleanly, produces trustworthy baselines, executes supported GPU/USB/NIC workflows, survives interruption, restores managed state and uninstalls without unexplained configuration residue.

---

## Permanent-test policy

- Default/target permanent suite size: **10**.
- Current count: **10**.
- Every test source file must be **<=1200 lines**.
- The repository owner explicitly authorizes growth up to **20** permanent tests only when genuinely necessary to keep files below 1200 lines or to preserve a materially safer durable separation.
- This authorization is not a target. Remove temporary/obsolete tests and consolidate low-value duplication before increasing the count.
- Hardware validation, exploratory benchmark runs and release checklists do not count as automated tests.

## Definition of 100%

Repository/source completion is not the same as true product 1.0. A source item may be checked when its deterministic implementation exists and has the evidence required by that item. Hardware-, App/runtime-, package-, accessibility- and signing-dependent requirements remain open until owner-local evidence exists.

Only after every phase exit gate, Gate A→B→C→D, representative supported hardware validation, release/signing/provenance and recovery/uninstall validation are satisfied may the product be tagged 1.0.
