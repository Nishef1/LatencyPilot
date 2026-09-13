# LatencyPilot Product Roadmap

Status: **Authoritative completion plan**  
Last updated: 2026-09-13

LatencyPilot is complete only when it can safely measure a Windows 11 system, identify latency pressure, run narrowly scoped experiments, quantify target and collateral effects, and let the user keep or revert changes with trustworthy recovery.

A phase is **closed only when every required checkbox in that phase is complete and its exit gate is satisfied**. Partial implementation does not count. `PROJECT_STATUS.md` is the live execution ledger and owns the detailed current execution ladder.

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

## Phase transition map

This is the required direction after each phase closes. The detailed substeps for the active phase live in `PROJECT_STATUS.md`.

```text
Phase 0 governance
→ Phase 1 build/comparison foundation
→ Phase 2 trustworthy read-only observation
→ Phase 3 safe mutation substrate + GPU interrupt experiment
→ Phase 4 USB/xHCI + input analysis
→ Phase 5 NIC/RSS optimization
→ Phase 6 bounded cross-subsystem optimizer/profiles
→ Phase 7 productization/release hardening
→ 1.0
```

No later phase may be used to bypass an earlier phase exit gate. Shared infrastructure implemented early may be reused by later phases, but its existence does not close the later phase.

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

### After Phase 0 closes
Proceed to Phase 1: buildable solution → comparison-domain invariants → read-only desktop vertical slice → validation/package evidence.

---

## Phase 1 — Buildable foundation + trustworthy comparison core

**State: CLOSED**

### 1.1 Toolchain/repository
- [x] pin .NET 10 SDK;
- [x] solution/shared build settings;
- [x] Core, Benchmarking, Protocol, Platform.Windows, Persistence, Service and App projects;
- [x] one focused permanent-test project;
- [x] historical Windows Release CI at Phase 1 closure. Current hosted validation runs the critical suite plus Release compile gates for App/Service; publish/package/release remains owner-local.

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
Historical Phase 1 evidence showed a successful release build + permanent tests + self-contained x64 artifact, with no device mutation.

### After Phase 1 closes
Proceed to Phase 2 in this order: authoritative inventory/resource evidence → privileged read-only ETW observation → module/CPU attribution → physical validation → repeated baseline/noise/drift → evidence UX.

---

## Phase 2 — Observation engine: trustworthy Windows baseline

**State: IN PROGRESS**

### 2.1 Inventory and evidence provenance
- [x] processor-group-aware CPU package/core/logical-processor/SMT topology implementation;
- [ ] validate CPU topology on physical Windows 11 hardware;
- [x] present PnP device inventory with stable instance IDs;
- [x] relevant driver provider/version/INF metadata;
- [x] stored interrupt configuration inspection with availability/error provenance;
- [x] allocated IRQ/resource assignment capture from Configuration Manager with partial/unavailable provenance;
- [x] optional device-property/resource failures degrade to partial evidence instead of invalidating the entire inventory;
- [ ] distinguish line/message interrupt evidence where Windows source data permits it;
- [ ] physical Windows 11 validation of representative GPU/xHCI/NIC device inventory and allocated resources.

Stored registry configuration, allocated resource assignment and runtime behavior are separate evidence levels and must remain separate in code/UI.

### 2.2 ETW observation
- [x] controlled privileged ETW observation host in `LatencyPilot.Service`;
- [x] versioned typed local Named Pipe protocol for Phase 2 observation;
- [x] fail-closed protocol framing rejects unknown JSON members and malformed/oversized frames;
- [x] local observation pipe denies network identities, narrows its ACL to interactive/service identities and fail-closes unless the connected client session matches the active console session;
- [x] abandoned clients cancel active capture work instead of leaving the single observation host occupied;
- [x] no generic registry/shell/process execution command;
- [x] controlled ETW session lifecycle;
- [x] DPC collection;
- [x] ISR collection;
- [x] ETW processor-number attribution and per-processor aggregation;
- [x] authoritative module/driver attribution;
- [x] p50/p95/p99/max duration distributions using one documented percentile estimator;
- [x] p99.9 exposed only when the corresponding distribution has at least 1,000 samples;
- [x] capture cancellation/timeout/cleanup on failure;
- [x] bounded aggregate IPC response rather than raw-event transfer;
- [x] protocol-v5 capture evidence preserves `RequestId` for direct log/evidence correlation;
- [x] bounded structured App/Service diagnostics, including expected capture-unavailable and session-rejection provenance, without per-event ETW logging;
- [ ] physical Windows 11 validation of current Service → ETW → IPC behavior, active-session authorization and cleanup.

Native routine addresses must remain unresolved unless authoritative kernel image evidence maps them. Already-loaded images require the appropriate kernel image rundown/CAPTURE_STATE behavior rather than future ImageLoad events alone.

The current Phase 2 observation ACL/session rule is not future mutation authorization. Phase 3 must add mutation-specific authorization before any privileged write command exists. RDP/multi-session observation is not implied by the current active-console rule.

### 2.3 Baseline quality
- [ ] repeated baseline windows in the WinUI flow — source implemented and hosted Release compile passes; owner-local runtime/physical evidence pending;
- [x] stable repeated-window policy/minimum valid windows in deterministic `baseline-quality-v1`;
- [x] contiguous window-number sequence is authoritative; UTC timestamps remain provenance rather than a monotonic-order requirement;
- [x] empirical window-level p99 noise-floor calculation;
- [x] early/late inter-window drift detection;
- [x] invalid/extreme-window handling with explicit reasons and no silent deletion;
- [ ] background/thermal quality warning where observable and reliable;
- [ ] invalid/inconclusive baseline cannot unlock a future optimizer — quality gate exists, optimizer integration does not yet exist.

Current `baseline-quality-v1` requires five windows, at least 20 events per metric/window, clean capture integrity, <=30% relative P10-P90 spread, <=20% early/late drift and no >50% extreme-window deviation. These are versioned conservative policy values, not statistical-significance claims.

### 2.4 UX
- [x] observation-service health/safety status;
- [x] short read-only DPC/ISR observation action;
- [x] DPC/ISR counts and p99 summary plus sample-gated p99.9;
- [x] ETW loss/invalid/event-limit status;
- [x] integrity-warning capture cannot be presented as healthy merely because threshold counts are low;
- [x] documented 100 µs DPC / 25 µs ISR driver-guidance rows are separated from local 1 ms / 3 ms diagnostic buckets;
- [x] short capture is labeled observation rather than trustworthy baseline;
- [x] per-CPU latency/interrupt concentration view;
- [x] bounded top DPC/ISR contributors after module attribution exists;
- [x] manual JSON evidence export source preserves bounded aggregate evidence, correlation ID and bounded environment provenance;
- [ ] evidence-export/runtime behavior validation on physical WinUI;
- [ ] repeated-baseline quality verdict/reasons UI — source implemented and hosted compile passes; owner-local runtime evidence pending;
- [ ] validate configuration vs assigned resource vs runtime evidence labels on physical UI;
- [ ] finish narrow-window/text-scaling/focus/screen-reader sanity pass on physical WinUI.

### Exit gate
A user can run a repeatable **read-only** baseline on a real Windows 11 PC and identify CPU/module latency concentration without LatencyPilot changing system configuration.

### After Phase 2 closes
Proceed to Phase 3. Do **not** recreate the Service/IPC: Phase 2 already owns that infrastructure. Phase 3 starts with durable journal/recovery and mutation-specific authorization, then implements the first reversible GPU interrupt experiment.

---

## Phase 3 — Safe mutation platform + GPU interrupt optimization

**State: NOT STARTED — observation infrastructure already inherited from Phase 2**

### 3.1 Safety substrate
- [x] privileged Windows Service exists as the narrow boundary;
- [x] versioned Named Pipe protocol exists;
- [x] generic registry/shell/process execution is prohibited by contract and absent from Phase 2 protocol;
- [ ] SQLite durable experiment journal/recovery state;
- [ ] mutation-specific command authorization/allowlist extension;
- [ ] Detect → Snapshot → Validate → Journal → Apply → Verify lifecycle;
- [ ] pending experiment survives interruption;
- [ ] verified rollback/recovery;
- [ ] reboot-required/recovery-required states represented explicitly.

The three checked infrastructure items are inherited prerequisites only; they do not mean Phase 3 has begun mutation work.

### 3.2 GPU experiment
- [ ] GPU applicability detection;
- [ ] topology-aware CPU candidates;
- [ ] one-candidate-at-a-time affinity mutation;
- [ ] MSI/MSI-X mutation only where applicability and rollback are authoritative;
- [ ] ETW target metrics;
- [ ] PresentMon guardrails where available;
- [ ] repeat candidate runs;
- [ ] Keep/Revert decision with raw deltas;
- [ ] forced-failure rollback exercise on supported physical hardware.

### Exit gate
A supported physical GPU can be tuned and safely restored on Windows 11, including a forced-failure rollback exercise.

### After Phase 3 closes
Proceed to Phase 4: first map HID → hub/port → xHCI, then measure host-side input timing and controller runtime behavior, and only then permit reversible xHCI/controller-affinity experiments.

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

### After Phase 4 closes
Proceed to Phase 5: NIC capability/RSS inventory → queue/processor distribution → NDIS attribution → controlled local-network benchmark → supported reversible RSS/affinity experiments.

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

### After Phase 5 closes
Proceed to Phase 6: combine only already-supported subsystem experiments into a bounded candidate search with workload profiles, repeated finalists, trade-off/Pareto handling and global restore semantics.

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

### After Phase 6 closes
Proceed to Phase 7 productization: installer/service lifecycle, signing/provenance, upgrade/uninstall recovery safety, diagnostics, accessibility and representative clean-machine/hardware validation.

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

### After Phase 7 closes
Run the final 1.0 release audit against the complete product definition, every phase exit gate, active recovery state, documentation, licensing, signing/provenance and representative physical-hardware evidence. Only then tag 1.0.

---
