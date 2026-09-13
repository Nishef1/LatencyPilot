# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-13

LatencyPilot is complete only when it can safely measure a Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and let the user keep or revert supported changes with trustworthy recovery.

A phase closes only when its exit gate is satisfied with current evidence. `PROJECT_STATUS.md` is the live execution ledger; this roadmap defines required direction and scope.

## Product definition — 1.0

LatencyPilot 1.0 must provide:

- read-only hardware/topology/device/interrupt/driver inventory;
- ETW-based DPC/ISR measurement with per-CPU and per-module attribution;
- quick diagnostic evidence that is clearly distinguished from decision-grade measurement;
- repeatable baseline capture with sample adequacy, noise and drift checks;
- before/after comparison with raw metrics, tails and explicit uncertainty;
- safe, reversible interrupt-affinity/MSI experiments for supported devices;
- GPU workflows with PresentMon target/guardrail evidence where applicable;
- USB/xHCI and high-polling input analysis;
- NIC/RSS analysis and supported network experiments;
- cross-subsystem guardrails;
- workload profiles without hiding raw metrics/trade-offs;
- durable experiment journal, crash/reboot recovery and verified rollback;
- non-elevated WinUI 3 App with a narrow privileged Service boundary;
- history, diagnostics and auditable Keep/Revert/Inconclusive decisions;
- signed/self-contained Windows 11 x64 productization with safe install/upgrade/uninstall.

Out of scope unless separately approved: generic debloating, registry cleaning, disabling Windows security, arbitrary service disabling, undocumented timer/HPET folklore, generic shell execution, or bulk tweak packs without measurement.

## Phase map

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

Later phases may reuse earlier infrastructure but cannot bypass earlier exit gates.

---

## Phase 0 — Charter, governance and safety

**State: CLOSED**

- [x] purpose/non-goals;
- [x] license/contribution/CLA/security policy;
- [x] engineering contract in `AGENTS.md`;
- [x] architecture/privilege boundary;
- [x] benchmark methodology;
- [x] roadmap/status tracking and GitHub templates.

### Exit gate
A contributor can recover product intent, architecture, non-goals, safety boundaries and completion criteria without chat history.

---

## Phase 1 — Buildable foundation + comparison core

**State: CLOSED**

- [x] .NET 10/C# 14 solution and shared build settings;
- [x] Core, Benchmarking, Protocol, Platform.Windows, Service and App boundaries;
- [x] experiment lifecycle and legal transitions;
- [x] metric direction/sample contracts;
- [x] deterministic canonical percentile calculation;
- [x] explicit `Improved`, `Regressed`, `Tradeoff`, `NoMeasurableDifference`, `Inconclusive` verdicts;
- [x] guardrail regression cannot be hidden by a local target win;
- [x] first non-mutating desktop vertical slice;
- [x] no synthetic machine optimization result presented as real evidence.

The original Phase 1 UI artifact was historical WPF; ADR 0002 superseded the framework with WinUI 3 without rewriting history.

---

## Phase 2 — Trustworthy read-only observation and baseline

**State: IN PROGRESS**

Phase 2 must establish a measurement substrate strong enough that later mutation decisions are not built on one-shot noise.

### 2.1 Inventory/provenance

- [x] processor-group-aware package/core/logical/SMT topology;
- [x] present PnP inventory with stable instance IDs;
- [x] driver provider/version/INF metadata;
- [x] stored interrupt-configuration inspection with unavailable/error provenance;
- [x] allocated ConfigMgr IRQ/resource evidence;
- [x] optional property/resource failures degrade to partial evidence;
- [x] representative GPU/display, NIC and actual `USBXHCI` device selection;
- [x] clean/dirty source revision provenance in owner-local builds;
- [ ] physical topology/device/resource plausibility on target Windows 11 hardware;
- [ ] authoritative assigned line/message distinction only if Windows evidence actually proves it.

Required semantic boundary:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

### 2.2 Privileged read-only observation Service

- [x] Windows Service hosts privileged kernel observation;
- [x] typed/versioned Named Pipe protocol;
- [x] current observation protocol **v6**;
- [x] status + kernel-capture commands only;
- [x] no mutation command or generic shell/registry/process primitive;
- [x] fail-closed framing/unknown-member behavior;
- [x] local ACL + active-console-session authorization;
- [x] disconnect cancellation and bounded operation deadlines;
- [x] capture `RequestId` correlation;
- [ ] physical final-candidate Service/ETW/IPC/cleanup/session validation.

Current observation authorization is deliberately not accepted as future mutation authorization.

### 2.3 ETW DPC/ISR evidence

- [x] controlled kernel ETW lifecycle;
- [x] DPC collection;
- [x] ISR collection;
- [x] per-processor aggregation;
- [x] image load/unload/rundown-aware module attribution;
- [x] ambiguous/missing mappings remain unresolved;
- [x] bounded module/unresolved contributor lists;
- [x] p50/p95/p99/max using one canonical estimator;
- [x] p99.9 withheld until **10,000 samples** under protocol v6;
- [x] ETW loss/invalid event/image/event-limit provenance;
- [x] post-capture aggregate logging without raw per-event log I/O;
- [ ] physical attribution plausibility against an independent observer where practical.

Interpretation contract:

- DPC `>100 µs` / ISR `>25 µs` are Microsoft driver-duration reference lines;
- `>1 ms` / `>3 ms` are LatencyPilot local diagnostic buckets;
- none of these alone is a health/impact verdict;
- CPU0 concentration is a hypothesis signal, not an automatic fault.

### 2.4 Measurement modes

#### Quick diagnostic snapshot

- [x] one five-second snapshot;
- [x] used for integrity, attribution, concentration, obvious tail events and hypothesis generation;
- [x] evidence purpose `quick-diagnostic-snapshot`;
- [x] must not unlock an optimizer verdict;
- [ ] physical final-candidate quick-snapshot UX/evidence audit.

#### Repeated decision baseline — `baseline-quality-v2`

- [x] exactly five windows;
- [x] **20 seconds requested per authoritative window**;
- [x] actual duration must be >=95% of requested duration;
- [x] >=1,000 DPC events/window and >=1,000 ISR events/window for current p99 stability eligibility;
- [x] clean capture required;
- [x] <=30% relative P10–P90 spread;
- [x] <=20% early/late drift;
- [x] no >50% extreme-window deviation;
- [x] no silent window deletion;
- [x] `WindowNumber` is authoritative sequence, wall-clock UTC is provenance;
- [x] five-second LatencyPilot/service pre-sequence settle is distinct from workload warm-up;
- [x] evidence purpose `repeated-decision-baseline`;
- [x] evidence schema **`latencypilot-evidence-v8`**;
- [ ] physical Real-world v8/v2 baseline on exact final source;
- [ ] physical Controlled-idle v8/v2 baseline on exact final source.

Real-world/before-after workloads must already be warmed and repeatable before the decision baseline starts unless startup/loading is intentionally what is being measured.

### 2.5 Evidence/runtime context

- [x] exact source revision when clean build metadata supports it;
- [x] dirty source refuses to claim exact clean commit provenance;
- [x] JSON evidence with scenario/environment/topology and bounded aggregates;
- [x] best-effort system CPU busy context;
- [x] AC/DC, Battery Saver, active scheme and configured Windows power-mode provenance;
- [x] SHA-256 after JSON save;
- [x] strict `scripts/Verify-Evidence.ps1` v8/v2 closure checks;
- [ ] final physical JSON-visible/hash/source-revision reconciliation.

### 2.6 UX/accessibility

- [x] Service/safety state;
- [x] quick snapshot and decision baseline are visibly distinct;
- [x] scenario-specific preparation gate for decision baseline only;
- [x] Real-world mode keeps issue-reproducing applications open;
- [x] quick-snapshot interpretation uses diagnostic/reference language rather than global health verdicts;
- [x] CPU/module contributor and tail-rate views;
- [x] representative device evidence inspector;
- [x] keyboard shortcuts `Ctrl+R/O/B/E`;
- [x] adaptive/high-contrast source resources;
- [ ] physical Light/Dark/High Contrast, narrow/wide, text scaling, keyboard/focus and screen-reader sanity.

### 2.7 Failure/security closure

- [ ] App close during capture;
- [ ] Service stop/restart/reconnect;
- [ ] no stale ETW session after interruption;
- [ ] partial baseline cannot become Valid;
- [ ] second-session rejection where practical;
- [ ] zero-mutation verification.

### Exit gate
On one exact clean final Phase 2 source revision:

- hosted eight-test contract is green;
- owner-local App/Service builds and launches;
- protocol v6 works physically;
- one clean v8 quick snapshot proves diagnostic/provenance path;
- one Real-world and one Controlled-idle v8/v2 `5 × 20 s` decision baseline pass strict verification;
- representative device/attribution/failure/session/accessibility evidence passes;
- zero mutation is confirmed.

### After Phase 2
Proceed to Phase 3. Do not add mutation before this gate is physically closed.

---

## Phase 3 — Safe mutation platform + GPU interrupt experiment

**State: NOT STARTED**

### 3.1 Durable safety substrate

- [ ] introduce the real SQLite persistence/journal project and migrations;
- [ ] mutation-specific authorization and typed allowlist;
- [ ] Detect → Snapshot → Validate → Journal → Apply → Verify;
- [ ] interruption/reboot recovery state;
- [ ] exact original-state rollback and final-state verification;
- [ ] no generic registry/shell/process primitive.

### 3.2 GPU candidate experiment

- [ ] detect supported GPU and current interrupt policy/resources;
- [ ] preserve default/current state as the control;
- [ ] topology-aware, physical-core-aware candidate generation;
- [ ] treat CPU0 concentration as candidate-prior evidence, never a hard exclusion rule;
- [ ] bounded screening of plausible candidates;
- [ ] confirm a small finalist set with balanced/interleaved A/B sequences such as ABBA/BAAB;
- [ ] use decision-grade candidate/control run durations (initial design around 30 seconds/run, versioned when implemented);
- [ ] verify applied affinity state before measurement;
- [ ] MSI/MSI-X mutation only where applicability and rollback are authoritative;
- [ ] DPC/ISR target metrics;
- [ ] PresentMon frame-time/CPU/GPU/latency metrics where workload supports them;
- [ ] USB/network/audio/stability guardrails as applicable;
- [ ] Keep/Revert based on measured target + guardrail vector;
- [ ] forced-failure rollback exercise.

### Exit gate
A supported physical GPU can be tested against its control, a candidate can be kept only on reproducible evidence, and forced failure restores the exact original managed state.

---

## Phase 4 — USB/xHCI and input analysis

**State: NOT STARTED**

- [ ] HID → port/hub → xHCI mapping;
- [ ] Raw Input report interval/jitter measurement;
- [ ] USB/xHCI ETW correlation;
- [ ] controller DPC/ISR attribution;
- [ ] supported reversible controller-affinity experiments;
- [ ] target metrics + collateral guardrails;
- [ ] host-side report timing kept distinct from physical switch-to-photon claims.

### Exit gate
At least one high-polling input/controller path can be analyzed and a reversible xHCI experiment compared without overstating measurement capability.

---

## Phase 5 — NIC/RSS optimization

**State: NOT STARTED**

- [ ] NIC capability/RSS inventory;
- [ ] RSS processor/queue distribution;
- [ ] NDIS DPC/ISR attribution;
- [ ] controlled local-network latency/jitter benchmark;
- [ ] supported RSS/affinity experiments;
- [ ] throughput/loss/CPU guardrails;
- [ ] Internet tests remain supplemental rather than the sole primary truth.

### Exit gate
A local networking improvement can be separated from path noise and every supported NIC change can be restored.

---

## Phase 6 — Cross-subsystem optimizer and profiles

**State: NOT STARTED**

- [ ] Competitive/Gaming, General and Audio-sensitive profiles;
- [ ] bounded candidate search/pruning;
- [ ] repeated finalists;
- [ ] Pareto/trade-off representation;
- [ ] raw metrics always visible;
- [ ] user opt-out by subsystem;
- [ ] no overwrite of user-kept state without a new journaled experiment;
- [ ] global Restore Baseline.

### Exit gate
Auto mode completes a bounded multi-subsystem session and every retained change has individual evidence, provenance and rollback state.

---

## Phase 7 — Productization and 1.0

**State: NOT STARTED**

- [ ] installer/uninstaller and Service lifecycle;
- [ ] self-contained Windows 11 x64 package;
- [ ] signing/release provenance;
- [ ] safe upgrade of journal/history;
- [ ] uninstall restoration of active managed changes;
- [ ] redacted diagnostic bundle;
- [ ] accessibility/keyboard-navigation pass;
- [ ] no unexplained admin prompts;
- [ ] clean-machine validation;
- [ ] reboot/crash/recovery validation;
- [ ] representative NVIDIA/AMD/USB/NIC paths where hardware is available;
- [ ] documentation matches actual capabilities and limitations.

### Exit gate
1.0 installs cleanly, produces trustworthy baselines, executes supported GPU/USB/NIC workflows, survives interruption, restores managed state and uninstalls without unexplained residue.

---

## Permanent-test rule

Permanent automated tests may not exceed **10** without explicit owner approval plus an ADR explaining why staying at 10 would be more harmful. The current suite uses **8** durable test methods.

New high-blast-radius mutation/recovery invariants may reuse or replace lower-value slots. Temporary investigative tests may be created, executed and deleted during implementation.

Physical hardware validation is separate from the permanent-test count.

## Completion rule

A checked box requires evidence for its exact claim. Source complete does not mean physically validated; a green deterministic test run does not imply WinUI compilation or hardware correctness; local build does not imply release readiness; a diagnostic snapshot does not imply a benchmark verdict.

When all Phase 7 gates pass, perform one final 1.0 audit across product definition, all exit gates, active recovery state, documentation, licensing, signing/provenance and representative physical evidence before tagging 1.0.
