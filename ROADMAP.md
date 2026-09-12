# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-12

LatencyPilot is complete only when it can safely measure a Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and let the user keep or revert changes with trustworthy recovery.

A phase is **closed only when every required checkbox in that phase is complete and its exit gate is satisfied**. Partial implementation does not count as phase completion. `PROJECT_STATUS.md` is the live execution ledger; this document defines the destination and gates.

## Final product definition — 100%

LatencyPilot 1.0 must provide all of the following:

- read-only hardware, CPU-topology, device, interrupt and driver inventory;
- ETW-based DPC/ISR measurement with per-CPU and per-module attribution;
- repeatable baseline capture with noise/drift detection;
- before/after comparison with raw metrics, tail percentiles and explicit uncertainty;
- safe, reversible interrupt-affinity/MSI experiments for supported devices;
- GPU experiment workflow with PresentMon guardrails;
- USB/xHCI and high-polling input analysis;
- NIC/RSS analysis and supported network experiments;
- cross-subsystem guardrails so a local win cannot silently become a system-wide loss;
- workload profiles without hiding raw metrics or trade-offs;
- durable experiment journal, crash/reboot recovery and verified rollback;
- non-elevated UI with a narrow privileged service boundary;
- history, diagnostics export and a clear Keep / Revert / Inconclusive decision model;
- self-contained signed Windows 11 x64 release with install/uninstall and upgrade safety.

Out of scope for 1.0 unless separately approved by ADR: generic debloating, registry-cleaner behavior, disabling Windows security, arbitrary service disabling, forced HPET/timer folklore, generic shell execution, or undocumented bulk tweak packs.

---

## Phase 0 — Charter, governance and safety contract

**Goal:** make product intent, contribution rules and architecture non-ambiguous before implementation.

### Required
- [x] README defines product purpose and non-goals.
- [x] custom personal-use/non-commercial license is present.
- [x] contribution/CLA/security policy is present.
- [x] `AGENTS.md` defines agent/developer constraints.
- [x] `SYSTEM_DESIGN.md` defines project and privilege boundaries.
- [x] benchmark methodology is documented.
- [x] GitHub PR/issue templates and CODEOWNERS exist.
- [x] roadmap and live status ledger exist.

### Exit gate
Closed when a new contributor can determine what the product is, what it must never become, how changes are accepted, and where each responsibility belongs without relying on chat history.

**State: CLOSED**

---

## Phase 1 — Buildable foundation + trustworthy comparison core

**Goal:** establish a buildable Windows application and the smallest scientifically useful comparison engine before any real system mutation exists.

### 1.1 Toolchain and repository
- [ ] pin .NET 10 SDK;
- [ ] add solution and shared build settings;
- [ ] add Core, Benchmarking, Protocol, Platform.Windows, Persistence, Service and App projects;
- [ ] add one focused test project;
- [ ] CI builds Release on Windows and runs the small critical test suite.

### 1.2 Domain foundation
- [ ] define experiment lifecycle states and legal transitions;
- [ ] define metric direction and sample-series contracts;
- [ ] define explicit verdicts: Improved, Regressed, Tradeoff, NoMeasurableDifference, Inconclusive;
- [ ] reject invalid/non-finite measurement input instead of silently normalizing it.

### 1.3 Comparison foundation
- [ ] percentile calculation is deterministic;
- [ ] comparison uses a configurable minimum sample count;
- [ ] changes inside configured noise threshold are not called improvements;
- [ ] guardrail regression converts a target improvement into Tradeoff;
- [ ] insufficient data returns Inconclusive.

### 1.4 Read-only app vertical slice
- [ ] WPF app launches without elevation;
- [ ] app displays real OS/process architecture and logical processor count;
- [ ] UI clearly states that mutation/auto-tune is not available yet;
- [ ] no fake score or synthetic optimization result is shown as real data.

### 1.5 Critical tests only
Maximum target for the entire Phase 1 suite: **8 tests**. Tests must cover behavior with high blast radius, not getters, UI labels or implementation details.

Required scenarios:
- [ ] invalid experiment transition is rejected;
- [ ] insufficient samples return Inconclusive;
- [ ] no measurable difference is not advertised as improvement;
- [ ] clear primary improvement is detected;
- [ ] primary improvement plus guardrail regression returns Tradeoff;
- [ ] clear primary regression is detected;
- [ ] invalid/non-finite measurement data is rejected.

### Exit gate
Phase 1 is closed only when GitHub Actions proves the complete solution builds in Release on Windows, all critical tests pass, and the produced WPF app artifact can be created. No device mutation is permitted in this phase.

**State: IN PROGRESS**

---

## Phase 2 — Observation engine: trustworthy Windows baseline

**Goal:** replace generic system information with real latency observation and attribution while remaining strictly read-only.

### 2.1 Inventory
- [ ] CPU package/core/logical-processor/SMT topology;
- [ ] PCI/PnP device inventory and stable identities;
- [ ] current interrupt policy and MSI/MSI-X observable state where supported;
- [ ] relevant driver/provider identity and version capture.

### 2.2 ETW capture
- [ ] controlled ETW session lifecycle;
- [ ] DPC and ISR collection;
- [ ] per-CPU attribution;
- [ ] module/driver attribution;
- [ ] duration distribution: p50/p95/p99/p99.9/max;
- [ ] capture cancellation and cleanup on failure.

### 2.3 Baseline quality
- [ ] repeated baseline windows;
- [ ] noise-floor estimate;
- [ ] drift detection;
- [ ] thermal/background-load warning where observable;
- [ ] invalid baseline cannot unlock optimization.

### 2.4 UX
- [ ] per-CPU heat map/list;
- [ ] top DPC/ISR contributors;
- [ ] raw metric inspection;
- [ ] baseline quality verdict and reason.

### Exit gate
Closed when a user can run a repeatable read-only baseline on a real Windows 11 PC and identify CPU/module latency concentration without LatencyPilot changing system configuration.

**State: NOT STARTED**

---

## Phase 3 — Safe mutation platform + GPU interrupt optimization

**Goal:** implement the first complete Measure → Experiment → Verify → Compare → Keep/Revert optimization.

### 3.1 Safety substrate
- [ ] privileged Windows Service exists;
- [ ] versioned Named Pipe protocol;
- [ ] no generic registry/shell/process execution command;
- [ ] Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
- [ ] pending experiment survives app crash/service restart;
- [ ] verified rollback and recovery path;
- [ ] reboot-required state represented explicitly.

### 3.2 GPU experiment
- [ ] GPU device and interrupt-policy applicability detection;
- [ ] topology-aware candidate CPU selection;
- [ ] one-candidate-at-a-time affinity mutation;
- [ ] ETW target metrics;
- [ ] PresentMon frame-time/performance guardrails where available;
- [ ] repeat candidate runs;
- [ ] Keep/Revert user decision with raw deltas.

### Exit gate
Closed only after a real supported GPU can be tuned and safely restored on physical Windows 11 hardware, including a forced-failure rollback exercise.

**State: NOT STARTED**

---

## Phase 4 — USB/xHCI and input latency analysis

**Goal:** understand and safely optimize USB-controller placement without pretending software-only measurements equal click-to-photon hardware latency.

- [ ] map HID → port/hub → xHCI controller;
- [ ] Raw Input report interval/jitter measurement;
- [ ] USB/xHCI ETW correlation;
- [ ] controller DPC/ISR attribution;
- [ ] supported interrupt-affinity experiments for controller;
- [ ] input target metrics plus GPU/network/audio guardrails;
- [ ] clear distinction between host-side input timing and physical end-to-end latency.

### Exit gate
Closed when at least one high-polling mouse/controller path can be analyzed and a reversible xHCI experiment can be compared without overstating measurement capability.

**State: NOT STARTED**

---

## Phase 5 — NIC/RSS latency optimization

**Goal:** treat networking as IRQ + RSS + queue behavior rather than a single affinity mask.

- [ ] NIC capabilities and RSS inventory;
- [ ] RSS processor/queue distribution;
- [ ] NDIS DPC/ISR attribution;
- [ ] controlled local-network latency/jitter benchmark;
- [ ] supported RSS/affinity experiments;
- [ ] throughput/loss/CPU guardrails;
- [ ] Internet tests remain supplemental, not primary evidence.

### Exit gate
Closed when the tool can distinguish a real local networking improvement from Internet-path noise and revert all supported NIC changes.

**State: NOT STARTED**

---

## Phase 6 — Cross-subsystem optimizer and workload profiles

**Goal:** move from individual experiments to bounded automatic search without hiding trade-offs.

- [ ] Gaming / Competitive, General and Audio-sensitive profile definitions;
- [ ] raw metrics remain visible regardless of profile;
- [ ] candidate search prunes clearly inferior configurations;
- [ ] repeated finalists before recommendation;
- [ ] Pareto/trade-off representation;
- [ ] never overwrite user-kept configuration without a new journaled experiment;
- [ ] global Restore Baseline path;
- [ ] user can opt out of any subsystem.

### Exit gate
Closed when Auto mode can complete a bounded multi-subsystem session and every retained change has individual evidence, provenance and rollback state.

**State: NOT STARTED**

---

## Phase 7 — Productization and 1.0 release

**Goal:** ship a safe, understandable and supportable Windows utility rather than an engineering prototype.

- [ ] installer/uninstaller and service lifecycle;
- [ ] self-contained Windows 11 x64 package;
- [ ] code signing/release provenance;
- [ ] upgrade preserves journal/history safely;
- [ ] uninstall offers/executes restoration of active LatencyPilot-managed changes;
- [ ] diagnostics bundle redacts unnecessary personal information;
- [ ] accessibility and keyboard navigation pass;
- [ ] no unexplained admin prompts;
- [ ] clean-machine test;
- [ ] reboot/crash/recovery test;
- [ ] supported NVIDIA/AMD and common USB/NIC paths exercised where hardware is available;
- [ ] documentation matches actual capabilities and limitations.

### Exit gate
1.0 is reached only when the release can be installed on a clean Windows 11 x64 machine, produce a trustworthy baseline, execute at least the supported GPU/USB/NIC workflows, survive interruption, restore managed state, and uninstall without leaving unexplained configuration behind.

**State: NOT STARTED**

---

## Phase-closing rule

A checkbox may be marked complete only when the corresponding artifact/code exists on `main` and, where applicable, the relevant build or physical-hardware evidence exists. "Implemented but unverified" is not complete. `PROJECT_STATUS.md` must name the evidence and remaining blockers whenever a phase or subsection changes state.
