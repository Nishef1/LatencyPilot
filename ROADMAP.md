# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-13

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

No later phase may bypass an earlier phase exit gate. Shared infrastructure implemented early may be reused later, but its existence does not close the later phase.

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

### After Phase 0

Proceed to Phase 1: buildable solution → comparison invariants → read-only desktop slice → validation/package evidence.

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

Historical Phase 1 release/CI evidence remains historical. Current hosted CI is intentionally test-only and does not prove WinUI or Service runtime behavior.

### Exit gate

Historical Phase 1 evidence established a buildable self-contained x64 foundation with deterministic tests and no device mutation.

### After Phase 1

Proceed to Phase 2: authoritative inventory/resource evidence → privileged read-only ETW observation → module/CPU attribution → decision-baseline methodology → evidence UX → physical closure.

---

## Phase 2 — Trustworthy read-only Windows observation

**State: IN PROGRESS**

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
Mutation:             unavailable
Permanent tests:      8/10
```

### 2.1 Inventory and evidence provenance

- [x] processor-group-aware package/core/logical-processor/SMT topology implementation;
- [ ] physical topology validation on target Windows 11 hardware;
- [x] present PnP inventory with stable instance IDs;
- [x] driver provider/version/INF metadata;
- [x] stored interrupt configuration with availability/error provenance;
- [x] allocated IRQ/resource capture through Configuration Manager;
- [x] optional per-device failures degrade to partial evidence rather than erasing the device;
- [x] representative GPU/display, network and actual `USBXHCI` evidence surfaces;
- [ ] physical representative GPU/NIC/xHCI validation;
- [ ] authoritative line-vs-message assigned-interrupt distinction only if Windows exposes it through a trustworthy assigned-resource source; do not infer it from stored MSI configuration or raw ConfigMgr flags.

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
- [x] unique capture `RequestId` retained through logs/protocol/evidence;
- [ ] physical Service → ETW → IPC validation on the final frozen candidate;
- [ ] active-session rejection validation with a second local session where practical;
- [ ] attribution plausibility against an independent observer where practical.

Microsoft's 100 µs DPC and 25 µs ISR values are driver-duration guidance references. LatencyPilot's >1 ms and >3 ms rows are local diagnostic buckets. None of those values is a universal system-health score.

### 2.3 Measurement products and baseline quality

#### Quick diagnostic snapshot

- [x] one five-second snapshot flow;
- [x] purpose explicitly `quick-diagnostic-snapshot` in evidence-v8;
- [x] used for integrity, attribution, concentration and hypothesis generation;
- [x] UI avoids presenting it as a health/stability/optimization verdict.

#### Repeated decision baseline

- [x] workload is expected to be warmed/repeatable before sequence start where applicable;
- [x] five-second LatencyPilot/service settle before window 1;
- [x] exactly five 20-second authoritative windows;
- [x] 750 ms inter-window settle;
- [x] low-observer-activity sequencing avoids heavy redraw/file export between windows;
- [x] `baseline-quality-v2` requires each requested window to be >=20,000 ms;
- [x] actual duration must be >=95% of requested duration;
- [x] capture integrity must be clean;
- [x] each metric requires >=1,000 events/window plus finite positive p99;
- [x] relative P10-P90 p99 spread <=30%;
- [x] early/late p99 drift <=20%;
- [x] no >50% extreme-window deviation;
- [x] no silent deletion of inconvenient windows;
- [x] contiguous `WindowNumber` is authoritative sequence; wall-clock timestamps are provenance;
- [x] best-effort runtime CPU/power context retained as provenance without silently changing the versioned formula;
- [x] partial/short/lossy/undersampled/noisy/drifted sequence cannot become Valid;
- [ ] physical Real-world valid decision baseline on final candidate;
- [ ] physical Controlled-idle valid decision baseline on final candidate;
- [ ] verify low-observer-activity sequencing on physical compositor/workload;
- [ ] thermal warning only if a trustworthy low-overhead source is identified and physical evidence shows it changes decisions.

`Valid` means repeatable enough for the current comparison method. It does not mean “the machine is healthy”.

### 2.4 Evidence and UX

- [x] evidence schema `latencypilot-evidence-v8`;
- [x] explicit evidence purpose separates quick snapshot from decision baseline;
- [x] product/protocol/source revision provenance;
- [x] scenario provenance and stale-evidence invalidation;
- [x] best-effort runtime CPU/power provenance;
- [x] bounded environment/topology provenance;
- [x] unique RequestIds;
- [x] full bounded processor/module/unresolved aggregates;
- [x] baseline captures/windows/runtime-windows alignment validation;
- [x] SHA-256 after save;
- [x] independent `scripts/Verify-Evidence.ps1` verification;
- [x] strict clean-capture and valid-baseline verifier gates;
- [x] keyboard accelerators Ctrl+R/O/B/E;
- [x] High Contrast/theme/accessibility metadata source;
- [x] adaptive narrow/wide layout source;
- [x] representative device-evidence inspector;
- [x] clean/dirty source provenance surfaced in header;
- [ ] physical JSON-vs-visible-evidence/SHA/source-revision audit;
- [ ] physical warning/sample-insufficient/scenario/readiness state validation;
- [ ] physical narrow-window/text-scaling/keyboard/screen-reader sanity;
- [ ] physical device-inspector sanity.

### Phase 2 exit gate

A user on a physical Windows 11 PC can:

1. run LatencyPilot non-elevated against the protected read-only Service;
2. capture a clean evidence-v8 quick snapshot;
3. produce valid Real-world and Controlled-idle five × 20-second baseline-v2 artifacts on the same exact clean source candidate;
4. identify CPU/module concentration without LatencyPilot changing system configuration;
5. verify evidence provenance/hash and exercise failure/cleanup/session/accessibility checks.

Phase 2 does **not** close from CI, VM evidence or historical five-second captures alone.

### After Phase 2

Proceed to Phase 3. Reuse the Service/IPC infrastructure; do not rebuild it as a second system.

---

## Phase 3 — Safe mutation platform + GPU interrupt experiment

**State: NOT STARTED — read-only infrastructure inherited from Phase 2**

### 3.1 Safety substrate

- [x] narrow privileged Service exists;
- [x] typed/versioned Named Pipe infrastructure exists;
- [x] generic privileged shell/registry/process execution is prohibited and absent;
- [ ] create concrete Persistence project with SQLite schema/migrations;
- [ ] durable exact-state snapshot and pending/closed journal records;
- [ ] mutation-specific command authorization/allowlist;
- [ ] Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
- [ ] recovery re-reads actual machine state before action;
- [ ] interrupted/pending experiments survive restart/reboot;
- [ ] verified rollback/recovery;
- [ ] explicit recovery-required/reboot-required states.

The checked infrastructure items are inherited prerequisites, not evidence that mutation work is complete.

### 3.2 First reversible GPU experiment

- [ ] GPU applicability detection;
- [ ] exact current/default state preserved as control;
- [ ] topology-aware physical-core candidate generation;
- [ ] bounded candidate screening;
- [ ] finalist confirmation using balanced/interleaved A/B ordering such as ABBA/BAAB;
- [ ] one candidate applied at a time;
- [ ] MSI/MSI-X mutation only where applicability, actual state verification and rollback are authoritative;
- [ ] ETW DPC/ISR target metrics;
- [ ] PresentMon frame-time / CPU-GPU busy-wait / GPU-display latency / dropped-frame metrics where applicable;
- [ ] relevant USB/network/audio/stability guardrails;
- [ ] explicit Improved/Regressed/Tradeoff/NoMeasurableDifference/Inconclusive decision;
- [ ] Keep/Revert with raw deltas and provenance;
- [ ] forced-failure rollback on supported physical hardware.

Do not assume CPU0 avoidance, Windows default affinity, a community tweak or another machine's winner is universally correct.

### Phase 3 exit gate

A supported physical GPU can be tuned through at least one narrow experiment and safely restored, including a forced-failure rollback exercise.

### After Phase 3

Proceed to Phase 4: map HID → hub/port → xHCI → runtime evidence before allowing reversible controller-affinity experiments.

---

## Phase 4 — USB/xHCI and input-latency analysis

**State: NOT STARTED**

- [ ] HID → port/hub → xHCI mapping;
- [ ] Raw Input report interval/jitter/missing/coalesced/burst analysis;
- [ ] USB/xHCI ETW correlation;
- [ ] controller DPC/ISR attribution;
- [ ] host-observable input timing clearly distinguished from physical end-to-end latency;
- [ ] supported reversible xHCI/controller-affinity experiments;
- [ ] target metrics plus collateral guardrails.

### Exit gate

At least one high-polling input/controller path can be analyzed and a reversible xHCI experiment compared without overstating measurement capability.

### After Phase 4

Proceed to NIC capability/RSS inventory → queue/processor distribution → NDIS attribution → controlled local-network benchmark → reversible RSS/affinity experiments.

---

## Phase 5 — NIC/RSS latency optimization

**State: NOT STARTED**

- [ ] NIC capabilities/RSS inventory;
- [ ] RSS processor/queue distribution;
- [ ] NDIS DPC/ISR attribution;
- [ ] controlled local-network latency/jitter benchmark;
- [ ] supported reversible RSS/affinity experiments;
- [ ] throughput/loss/CPU guardrails;
- [ ] Internet tests remain supplemental rather than authoritative local-network evidence.

### Exit gate

The tool can distinguish a local networking improvement from path noise and revert every supported NIC change.

### After Phase 5

Proceed to Phase 6: combine only already-supported experiments into a bounded search with profiles, repeated finalists, trade-off/Pareto handling and global restore semantics.

---

## Phase 6 — Cross-subsystem optimizer and workload profiles

**State: NOT STARTED**

- [ ] Competitive/Gaming, General and Audio-sensitive profiles;
- [ ] raw metrics always visible;
- [ ] bounded candidate search/pruning;
- [ ] repeated finalists;
- [ ] Pareto/trade-off representation;
- [ ] no overwrite of user-kept state without a new journaled experiment;
- [ ] global Restore Baseline;
- [ ] per-subsystem opt-out.

### Exit gate

Auto mode completes a bounded multi-subsystem session and every retained change has individual evidence, provenance and rollback state.

### After Phase 6

Proceed to Phase 7 productization/release hardening.

---

## Phase 7 — Productization and 1.0

**State: NOT STARTED**

- [ ] installer/uninstaller and Service lifecycle hardened;
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

1.0 installs cleanly, produces trustworthy baselines, executes supported GPU/USB/NIC workflows, survives interruption, restores managed state and uninstalls without unexplained configuration residue.

### After Phase 7

Run the final 1.0 audit against the full product definition, every phase exit gate, recovery state, documentation, license, signing/provenance and representative physical-hardware evidence. Only then tag 1.0.

---

## Permanent-test rule

Repository-wide permanent automated tests may not exceed **10** unless the owner explicitly approves an exception and an ADR explains why remaining at 10 would be more harmful.

Current count: **8**.

Test count is not a quality target. Consolidate scenario matrices inside durable high-value tests. Temporary implementation/debug tests may be created, run and deleted before finalization. Hardware validation is separate from this cap.
