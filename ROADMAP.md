# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-12

LatencyPilot is complete only when it can safely measure a Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and let the user keep or revert changes with trustworthy recovery.

A phase is **closed only when every required checkbox in that phase is complete and its exit gate is satisfied**. Partial implementation does not count. `PROJECT_STATUS.md` is the live execution ledger.

## Final product definition — 100%

LatencyPilot 1.0 must provide:

- read-only hardware, CPU-topology, device, interrupt and driver inventory;
- ETW-based DPC/ISR measurement with per-CPU and per-module attribution;
- repeatable baseline capture with noise/drift detection;
- before/after comparison with raw metrics, tail percentiles and explicit uncertainty;
- safe, reversible interrupt-affinity/MSI experiments for supported devices;
- GPU experiment workflow with PresentMon guardrails;
- USB/xHCI and high-polling input analysis;
- NIC/RSS analysis and supported network experiments;
- cross-subsystem guardrails;
- workload profiles without hiding raw metrics/trade-offs;
- durable experiment journal, crash/reboot recovery and verified rollback;
- non-elevated WinUI 3 UI with a narrow privileged service boundary;
- history, diagnostics export and clear Keep/Revert/Inconclusive decisions;
- signed, self-contained Windows 11 x64 release with install/uninstall and upgrade safety.

Out of scope for 1.0 unless separately approved by ADR: generic debloating, registry-cleaner behavior, disabling Windows security, arbitrary service disabling, forced HPET/timer folklore, generic shell execution, undocumented bulk tweak packs.

---

## Phase 0 — Charter, governance and safety contract

**State: CLOSED**

- [x] README defines purpose/non-goals.
- [x] custom personal-use/non-commercial license.
- [x] contribution/CLA/security policy.
- [x] `AGENTS.md` engineering contract.
- [x] `SYSTEM_DESIGN.md` architecture/privilege boundaries.
- [x] benchmark methodology.
- [x] GitHub PR/issue templates and CODEOWNERS.
- [x] roadmap and live status ledger.

### Exit gate
A contributor can determine product intent, non-goals, architecture, contribution rules and completion criteria without chat history.

---

## Phase 1 — Buildable foundation + trustworthy comparison core

**State: CLOSED**

### 1.1 Toolchain/repository
- [x] pin .NET 10 SDK;
- [x] solution/shared build settings;
- [x] Core, Benchmarking, Protocol, Platform.Windows, Persistence, Service and App projects;
- [x] one focused permanent-test project;
- [x] Windows Release CI.

### 1.2 Domain/comparison foundation
- [x] experiment lifecycle and legal transitions;
- [x] metric direction/sample contracts;
- [x] explicit verdicts;
- [x] reject non-finite input;
- [x] deterministic percentile calculation;
- [x] minimum-sample and minimum-change policy;
- [x] guardrail regression cannot be hidden by a local target result;
- [x] structurally insufficient evidence is `Inconclusive`.

### 1.3 Read-only app vertical slice
- [x] normal-user desktop app exists;
- [x] real OS/process inventory is shown;
- [x] mutation is explicitly unavailable;
- [x] no synthetic optimization result is shown as machine evidence.

Phase 1 originally closed with a WPF artifact. ADR 0002 later superseded the UI-framework choice with WinUI 3; historical Phase 1 evidence remains historical rather than being rewritten.

### Exit gate
Release build + permanent tests + self-contained x64 artifact succeed, with no device mutation.

---

## Phase 2 — Observation engine: trustworthy Windows baseline

**State: IN PROGRESS**

### 2.1 Inventory and evidence provenance
- [x] processor-group-aware CPU package/core/logical-processor/SMT topology implementation;
- [ ] validate CPU topology on physical Windows 11 hardware;
- [x] present PnP device inventory with stable instance IDs;
- [x] relevant driver provider/version/INF metadata;
- [x] stored interrupt configuration inspection with availability/error provenance;
- [ ] allocated IRQ/resource assignment capture from Configuration Manager;
- [ ] distinguish line/message interrupt evidence where Windows source data permits it;
- [ ] physical Windows 11 validation of representative GPU/xHCI/NIC device inventory.

Stored registry configuration, allocated resource assignment and runtime behavior are separate evidence levels and must remain separate in code/UI.

### 2.2 ETW capture
- [ ] controlled ETW session lifecycle;
- [ ] DPC collection;
- [ ] ISR collection;
- [ ] per-CPU attribution;
- [ ] module/driver attribution;
- [ ] p50/p95/p99/p99.9/max duration distributions;
- [ ] capture cancellation and cleanup on failure.

### 2.3 Baseline quality
- [ ] repeated baseline windows;
- [ ] measured noise floor;
- [ ] drift detection;
- [ ] background/thermal quality warning where observable;
- [ ] invalid baseline cannot unlock optimization.

### 2.4 UX
- [ ] per-CPU latency/interrupt concentration view;
- [ ] top DPC/ISR contributors;
- [ ] raw metric inspection;
- [ ] baseline quality verdict/reason;
- [ ] clearly label configuration vs assigned resource vs runtime evidence.

### Exit gate
A user can run a repeatable **read-only** baseline on a real Windows 11 PC and identify CPU/module latency concentration without LatencyPilot changing system configuration.

---

## Phase 3 — Safe mutation platform + GPU interrupt optimization

**State: NOT STARTED**

### 3.1 Safety substrate
- [ ] privileged Windows Service;
- [ ] versioned Named Pipe protocol;
- [ ] no generic registry/shell/process execution command;
- [ ] Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
- [ ] pending experiment survives interruption;
- [ ] verified rollback/recovery;
- [ ] reboot-required state represented explicitly.

### 3.2 GPU experiment
- [ ] GPU applicability detection;
- [ ] topology-aware CPU candidates;
- [ ] one-candidate-at-a-time affinity mutation;
- [ ] ETW target metrics;
- [ ] PresentMon guardrails where available;
- [ ] repeat candidate runs;
- [ ] Keep/Revert decision with raw deltas.

### Exit gate
A supported physical GPU can be tuned and safely restored on Windows 11, including a forced-failure rollback exercise.

---

## Phase 4 — USB/xHCI and input latency analysis

**State: NOT STARTED**

- [ ] HID → port/hub → xHCI mapping;
- [ ] Raw Input report interval/jitter measurement;
- [ ] USB/xHCI ETW correlation;
- [ ] controller DPC/ISR attribution;
- [ ] supported reversible controller-affinity experiments;
- [ ] target metrics plus collateral guardrails;
- [ ] host-side input timing clearly distinguished from physical end-to-end latency.

### Exit gate
At least one high-polling input/controller path can be analyzed and a reversible xHCI experiment compared without overstating measurement capability.

---

## Phase 5 — NIC/RSS latency optimization

**State: NOT STARTED**

- [ ] NIC capabilities/RSS inventory;
- [ ] RSS processor/queue distribution;
- [ ] NDIS DPC/ISR attribution;
- [ ] controlled local-network latency/jitter benchmark;
- [ ] supported RSS/affinity experiments;
- [ ] throughput/loss/CPU guardrails;
- [ ] Internet tests remain supplemental.

### Exit gate
The tool can distinguish a local networking improvement from path noise and revert all supported NIC changes.

---

## Phase 6 — Cross-subsystem optimizer and workload profiles

**State: NOT STARTED**

- [ ] Competitive/Gaming, General and Audio-sensitive profiles;
- [ ] raw metrics always visible;
- [ ] bounded candidate search/pruning;
- [ ] repeated finalists;
- [ ] Pareto/trade-off representation;
- [ ] never overwrite user-kept state without a new journaled experiment;
- [ ] global Restore Baseline;
- [ ] user can opt out by subsystem.

### Exit gate
Auto mode completes a bounded multi-subsystem session and every retained change has individual evidence/provenance/rollback state.

---

## Phase 7 — Productization and 1.0

**State: NOT STARTED**

- [ ] installer/uninstaller and service lifecycle;
- [ ] self-contained Windows 11 x64 package;
- [ ] signing/release provenance;
- [ ] safe upgrade of journal/history;
- [ ] uninstall restoration of active managed changes;
- [ ] redacted diagnostic bundle;
- [ ] accessibility/keyboard-navigation pass;
- [ ] no unexplained admin prompts;
- [ ] clean-machine validation;
- [ ] reboot/crash/recovery validation;
- [ ] representative NVIDIA/AMD/USB/NIC paths exercised where hardware is available;
- [ ] docs match actual capabilities/limitations.

### Exit gate
1.0 installs cleanly, produces a trustworthy baseline, executes supported GPU/USB/NIC workflows, survives interruption, restores managed state and uninstalls without unexplained configuration residue.

---

## Permanent-test rule

Repository-wide permanent automated tests may **never exceed 10** unless the owner explicitly approves the exception and an ADR explains why remaining at 10 would be more harmful. Temporary implementation/debug tests may be created and removed before finalization.

## Phase-closing rule

A checkbox is complete only when code/artifact exists on `main` and the required evidence exists. Build-dependent work requires green CI. Hardware-dependent work requires physical Windows 11 evidence. “Implemented but unverified” remains incomplete. Before closing a subsection, perform the mandatory step-back review defined in `AGENTS.md`.
