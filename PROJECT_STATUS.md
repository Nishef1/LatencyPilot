# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-15

## Overall

- Product version: **0.0.2 pre-alpha**.
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- Evidence schema: **`latencypilot-evidence-v8`**.
- Decision baseline: **`baseline-quality-v2`**.
- Optimizer workload readiness: **`workload-stability-v1`**.
- Permanent deterministic suite: **18 tests**; target 10, owner-authorized maximum 20 only for line-limit or materially safer durable subsystem separation.
- Hosted GitHub Actions: **test-only**. It does not prove App/Service launch, UI/accessibility, installer/package/signing or physical hardware behavior.

## Completion summary

### Repository-verifiable source

The remaining 1.0 source-completion tranche is now implemented across:

- internal GPU candidate execution, synchronized ETW/raw PresentMon evidence, runtime GPU ISR-placement verification and balanced confirmation;
- interruption-aware GPU affinity journal/recovery and global Restore Baseline semantics;
- documented USB hub/port route correlation and host-observable Raw Input timing;
- xHCI DPC/ISR attribution/readiness;
- authoritative StandardCimv2 NIC/RSS inventory, PnP correlation, network attribution and local benchmark interpretation;
- transparent workload profiles, subsystem opt-out and Pareto trade-off policy without a hidden score;
- workload-stability eligibility, including per-window system CPU activity when available, so changing/spiky repeated workloads cannot enter optimizer candidate planning;
- recovery-aware install/upgrade/uninstall source;
- deterministic release provenance/checksum/signing hooks and local redacted diagnostic bundle;
- read-only App inspector wiring for representative interrupt evidence, exact USB input routes, RSS state and on-demand host Raw Input timing, with optional evidence providers isolated so one unavailable layer no longer discards the rest of the inspector result;
- owner-local `LatencyPilot.ReadOnlyClosure` preflight that reconciles exact local/remote HEAD, exact-green Tests, the exact protected installed Service path **and the physical Service binary's embedded exact source revision**, clean journal, stale ETW, representative devices, USB/RSS and exact-revision baseline SHA/provenance without arming mutation;
- owner-local WinApp CLI evidence capture for Light/Dark/High Contrast/TextScale/Narrow/Keyboard UI states, including UIA trees, screenshots and SHA-256 manifests for later Accessibility Insights/Narrator review.

Supported USB/NIC mutation implementations remain deliberately **not armed/built as product mutation paths** until the shared GPU mutation substrate passes Gate A physically. This is a documented safety prerequisite, not permission to substitute source existence for physical proof.

### True product completion

**Not 100% yet.** Physical Phase 2 closure and mutation/release gates remain open. LatencyPilot must not be tagged 1.0 until those checks are recorded against exact source/package identities.

## Measurement authority

```text
Quick diagnostic snapshot
  1 × 5 s
  integrity / attribution / concentration / hypothesis generation
  never a health or optimizer verdict

Repeated decision baseline — baseline-quality-v2
  one steady workload/scene/action loop already warmed/repeatable when applicable
  phase-changing built-in benchmark != five equivalent steady windows
  5 s LatencyPilot/service settle
  5 × 20 s authoritative windows
  750 ms inter-window settle
  >=95% actual/request duration
  >=1,000 DPC and >=1,000 ISR events/window
  clean capture integrity
  bounded noise/drift/extreme-window checks

Optimizer workload readiness — workload-stability-v1
  exactly the same five-window steady sequence
  DPC event-rate stability
  ISR event-rate stability
  CPU-busy stability when available
  early/late drift gate
  isolated extreme-window activity gate

Phase-changing scripted benchmark
  diagnostic/useful only under the current five-window flow
  compare repeated whole runs or matched phases under fixed settings
  not a current Gate A candidate source

p99.9
  shown only with >=10,000 samples for that distribution
```

`Valid` means repeatable enough for the relevant method. It never means the machine is globally healthy or optimal.

## Phase 2 — read-only physical closure OPEN

Repository source includes processor-group-aware topology, present PnP/driver/interrupt inventory, stored-vs-allocated-vs-runtime evidence separation, protected Service/Named Pipe v6, ETW DPC/ISR capture, module/processor attribution, repeated baseline gates, evidence-v8 provenance/SHA verification and adaptive evidence UI.

The owner-local closure path is now consolidated in `tools/LatencyPilot.ReadOnlyClosure` and `docs/OWNER_CLOSURE.md`. It records exact local/remote revision, exact-green CI, protected Service state, exact protected executable path and embedded exact source revision from the installed Service binary, journal cleanliness, stale ETW absence, representative GPU/NIC/xHCI presence, USB topology, RSS provider state, and exact-revision steady Real-world/Controlled-idle baseline SHA/provenance in one read-only JSON audit. A stale Service binary at the correct directory no longer satisfies exact-revision closure. `scripts/Capture-UiAccessibilityEvidence.ps1` separately captures repeatable UIA/screenshot evidence for required display and keyboard states. These tools reduce manual closure work; they do not convert unexecuted owner-local checks into evidence.

Prior physical evidence includes a valid Real-world five-window decision baseline on clean historical revision `a4b4ff36c875982d5a263665860853462d0b055b`. That evidence authorized later source work under ADR 0004; it did not close Phase 2.

Remaining owner-local Phase 2 obligations:

1. valid steady Real-world and Controlled-idle five-window baselines on the exact closure revision, with the consolidated audit passing;
2. representative GPU/NIC/xHCI inspector sanity, including current USB/RSS read-only surfaces, recorded by the audit and visually sanity-checked;
3. attribution plausibility against an independent observer where practical;
4. App-close/Service-restart/stale-ETW cleanup and active-session rejection checks;
5. Light/Dark/High Contrast, narrow/text-scaling, keyboard and UI Automation/screen-reader sanity using the captured UI evidence plus Accessibility Insights/Narrator review;
6. JSON-visible-data/SHA/source-revision reconciliation recorded by the consolidated audit;
7. proof read-only validation performs zero unrelated system mutation.

## Phase 3 — GPU execution source implemented; physical arming OPEN

### Durable mutation/recovery substrate

Implemented:

- SQLite journal with compare-and-swap revisions;
- one unresolved experiment blocks unsafe follow-on mutation;
- explicit mutation lifecycle states;
- exact original/candidate GPU affinity payloads;
- fail-closed reclassification from actual state;
- unknown/diverged/driver-changed rollback refusal;
- interruption-safe logical two-value affinity write with compensation/recovery ownership;
- exact-target restart/reboot-required source;
- owner-only validation harness;
- retained `Kept` change discovery and ordered global Restore Baseline planning;
- uninstall/upgrade safety treats `Kept` as an active managed change, not a safe terminal state.

### GPU experiment source

Implemented:

```text
stable authoritative baseline
→ measured bounded candidates
→ journaled apply/activate
→ synchronized ETW + raw PresentMon capture
→ stored-state + runtime ISR placement verification
→ exact rollback between screening candidates
→ finalist nomination only
→ fixed ABBA + BAAB confirmation
→ verified Keep or exact RestoreOriginal/RecoveryRequired
```

The shared baseline-readiness contract now requires both `baseline-quality-v2` and `workload-stability-v1`. App candidate preparation uses the same readiness boundary and the actual per-window runtime CPU-busy evidence when available, so CPU drift, a changing interrupt workload, partial runtime activity evidence or an isolated spike does not proceed to candidate generation. The App now labels the `RealWorld` path as a steady real-world workload and explicitly warns that a phase-changing built-in benchmark must not be treated as five equivalent windows. The App also exposes the readiness outcome rather than silently withholding candidates.

The owner-only Gate A placement command now fails closed unless the exact stored candidate is verified immediately before and after a clean capture **and** runtime evidence contains at least one resolved GPU-driver ISR on the requested processor with zero resolved GPU-driver ISR events off target. ConfigMgr allocated resources remain independent provenance when readable; they cannot substitute for, or by themselves block/pass, the runtime placement proof. Missing/unavailable correlation is not a successful placement proof.

### Gate A — internal physical substrate proof — OPEN

The earlier read-only preflight found the owner NVIDIA GeForce RTX 3070 and a clean journal, but returned an allocated-resource tuple:

```text
irq=4294967270
group=1
affinity=0x0
flags=0x0002
```

That tuple was explicitly **not** accepted as proof of effective interrupt placement. Current source separately requires runtime GPU-driver ISR evidence for candidate placement.

Gate A requires on exact current clean `main`:

1. App + protected Service build/install/launch;
2. clean startup journal with zero unresolved entries;
3. a controlled unresolved entry survives Service restart/reclassification from actual machine state;
4. exact-target `DICS_PROPCHANGE` / reboot-required behavior is physically verified;
5. one bounded GPU candidate apply → stored verification → restart → runtime ISR evidence → exact rollback;
6. one deliberate supported failure proves rollback/recovery;
7. final machine state equals exact original and journal reports zero unresolved entries.

### Gate B — mutation-specific IPC — BLOCKED BY GATE A

After Gate A only: typed, mutation-specific, allowlisted commands and authorization. No arbitrary registry/shell/process primitive. Product mutation remains unavailable.

### Gate C — physical IPC proof — BLOCKED BY GATE B

Validate the real App/client → Service mutation path, authorization, target identity, journal ownership and exact rollback.

### Gate D — product arming — BLOCKED BY GATE C

Only then may the supported one-click mutation workflow become user reachable.

## Phase 4 — USB/xHCI/input source frontier

Implemented read-only/readiness source:

- Raw Input → stable PnP route;
- documented USB hub interface/IOCTL enumeration;
- unique driver-key → exact hub/port correlation with explicit ambiguity/unavailability;
- exact xHCI controller identity;
- bounded Raw Input report timestamp capture;
- median/p95/p99 interval, observed report rate, tail jitter, long-gap and burst/coalescing analysis;
- explicit `host-observable-raw-input-dispatch-timing` scope rather than click-to-photon claims;
- xHCI DPC/ISR module attribution and readiness metrics/guardrails;
- App inspector surface for route/port evidence and explicit on-demand five-second host timing capture.

Still open:

- xHCI/controller-affinity mutation source after Gate A authorizes reuse of the physical mutation substrate;
- physical high-polling route/timing/experiment/rollback evidence.

## Phase 5 — NIC/RSS source frontier

Implemented read-only/readiness source:

- `Root\StandardCimv2` `MSFT_NetAdapterRssSettingData` reader;
- enabled/MSI/MSI-X/queue/message/profile/processor/indirection evidence;
- conservative provider→PnP correlation;
- vendor driver vs generic NDIS attribution;
- local RTT/jitter/loss/throughput/CPU metric contract;
- Internet observations explicitly supplemental;
- readiness target/guardrail contract;
- App inspector RSS provider/PnP surface.

Still open:

- supported RSS/affinity mutation source after Gate A;
- physical local-network benchmark/apply/revert evidence.

## Phase 6 — profile/Pareto/restore source

Implemented:

- `CompetitiveGaming`, `General`, `AudioSensitive` versioned profiles;
- explicit subsystem opt-out;
- named raw metrics and guardrails;
- Pareto Dominates/Dominated/Equivalent/Tradeoff/Inconclusive policy;
- no arbitrary weighted score;
- workload-stability optimizer eligibility;
- newest-first retained-change planning;
- fail-closed global Restore Baseline planner;
- GPU restore execution reuses the existing subsystem transaction/restart/verification path.

An armed multi-subsystem auto-optimizer remains physically gated by the unsupported/unarmed USB/NIC mutation paths.

## Phase 7 — release/recovery source hardening

Implemented source:

- exact-main/clean-tree/exact-green-tests release gate;
- owner-local Release restore/build/publish and WinUI launch-smoke path;
- self-contained App/Service payload and Inno Setup/portable sources;
- pre-replacement journal safety checks for upgrade/manual Service replacement;
- pre-removal safety checks for uninstall;
- recovery tools are retained when state is unreadable, unresolved or `Kept`;
- canonical payload SHA-256 manifest and package checksums;
- Authenticode signing/verification hook using SHA-256 digests and RFC 3161 timestamps;
- final release requires signing configuration;
- redacted local diagnostics ZIP with no automatic upload.

Owner-local package closure remains open: actual Release build, signing credential use, installer/portable smoke, clean-machine install, blocked/safe upgrade/uninstall, reboot/crash recovery and representative hardware validation.

## Current verification discipline

The permanent suite is intentionally split into 18 durable tests so USB, input timing, NIC/RSS, profile/Pareto, restore, workload-readiness, GPU runtime-placement and installed-Service source-provenance failures remain independently diagnosable. The current owner rule permits up to 20 only for materially safer separation or the 1200-line file limit.

Every final source-completion claim requires a successful **Tests** workflow on the exact final HEAD. Hosted success proves deterministic/source contracts only.

## Exact owner-local closure sequence

```text
run exact-revision steady Phase 2 Real-world + Controlled-idle baselines + consolidated read-only audit
→ capture/review WinApp UIA evidence + Accessibility Insights/Narrator/manual read-only checks
→ build/install/launch exact current main
→ Gate A internal GPU substrate proof
→ implement Gate B typed mutation IPC
→ Gate C physical App/client → Service proof
→ Gate D user-facing GPU arming
→ implement/physically validate supported USB/xHCI mutation experiment
→ implement/physically validate supported NIC/RSS mutation experiment
→ validate bounded multi-subsystem profile flow + Restore Baseline
→ signed package + clean install/upgrade/uninstall/reboot/crash recovery audit
→ representative supported-hardware audit
→ final 1.0 tag audit
```

Never mark a physical/build/package/signing requirement complete from hosted CI or source existence alone.