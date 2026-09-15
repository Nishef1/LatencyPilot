# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-15

`ROADMAP.md` defines required outcomes. `PROJECT_STATUS.md` records current evidence and execution state. ADRs own accepted architecture decisions.

## 1. Product model

LatencyPilot is a Windows 11 latency experimentation platform built around:

```text
Measure → Experiment → Verify → Compare → Keep or Revert
```

A system-changing feature is incomplete unless it can follow:

```text
Detect applicability
→ Snapshot exact original state
→ Validate candidate
→ Journal pending experiment
→ Apply one narrow change
→ Verify actual applied state
→ Measure control/candidate
→ Compare target + guardrails
→ Keep or Revert
→ Verify final state
→ Close journal
```

Source implementation and physical authorization are separate. ADR 0004 permits later-phase source work while earlier physical closure remains open, but it never arms mutation or closes an exit gate.

## 2. Supported target

Initial target:

- Windows 11 x64;
- active local interactive desktop session;
- local machine only;
- non-elevated WinUI App;
- self-contained .NET / Windows App SDK deployment;
- privileged Windows Service only for kernel observation and later physically authorized narrow mutations.

Current observation authorization is intentionally active-console-session oriented. RDP/multi-session and ARM64 are not implied 1.0 support.

## 3. Technology baseline

- C# 14;
- .NET 10 LTS;
- WinUI 3 / Windows App SDK 2.4 Stable;
- unpackaged self-contained App;
- narrow Windows Service;
- typed/versioned local Named Pipes;
- ETW / `Microsoft.Diagnostics.Tracing.TraceEvent`;
- PresentMon for graphics/frame telemetry;
- SetupAPI + Configuration Manager;
- documented processor-topology / CPU-set APIs;
- documented USB hub interfaces/IOCTLs;
- Raw Input for host-observable input timing;
- `Root\StandardCimv2` RSS data through `System.Management`;
- concrete SQLite mutation journal through `Microsoft.Data.Sqlite`;
- MSTest + Microsoft.Testing.Platform;
- Inno Setup + PowerShell owner-local release tooling;
- bounded structured local diagnostics with Serilog.

NuGet versions are centrally owned by `Directory.Packages.props`. Native C++ is not a baseline dependency.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ WinUI 3 / normal user / non-elevated      │
│ evidence + measurement + read-only inspect│
└───────────────────┬────────────────────────┘
                    │ typed/versioned Named Pipe
                    ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service                       │
│ privileged boundary                       │
│ public IPC: observation-only v6            │
│ internal experiment/recovery source       │
│ product mutation remains unarmed          │
└──────────────┬─────────────────┬───────────┘
               │                 │
               ▼                 ▼
┌──────────────────────────┐  ┌──────────────────────────┐
│ Platform.Windows         │  │ Persistence              │
│ ETW / SetupAPI / CM      │  │ SQLite mutation journal │
│ USB / Raw Input / RSS    │  │ + recovery state        │
└──────────────┬───────────┘  └──────────────────────────┘
               ▼
         Windows 11 / hardware

Pure/shared:
Benchmarking → Core
Platform.Windows → Core
Protocol = standalone typed IPC contract
Persistence = concrete durable state boundary
```

There is intentionally no generic repository abstraction, generic tweak engine or generic privileged execution API.

## 5. Project responsibilities

### `LatencyPilot.Core`

Stable domain records/invariants only. No WinUI, ETW implementation, registry paths, P/Invoke, Service hosting or SQLite.

Current domains include topology/device evidence, input route/timing evidence, USB topology, RSS snapshots, metrics/results and system context.

### `LatencyPilot.Benchmarking`

Hardware-independent deterministic interpretation:

- canonical percentiles;
- `baseline-quality-v2`;
- `workload-stability-v1`;
- metric series/comparison/guardrail verdicts;
- GPU candidate/confirmation policy;
- USB/network readiness metric contracts;
- local-network benchmark interpretation;
- versioned workload profiles;
- Pareto relation without weighted score.

### `LatencyPilot.Protocol`

Typed/versioned IPC DTOs/errors only. Never a generic privileged execution surface.

Current public contract:

```text
ProtocolVersion.Current = 6
Pipe = LatencyPilot.Observation.v6
Commands = GetStatus, CaptureKernelLatency
MutationAvailable = false
```

### `LatencyPilot.Platform.Windows`

Windows-specific mechanisms:

- processor topology and CPU sets;
- PnP/driver/stored/allocated interrupt evidence;
- ETW capture and module/runtime attribution;
- GPU interrupt-affinity state/applicability and exact-target activation;
- PresentMon API/frame/workload evidence;
- USB hub topology + exact driver-key/port correlation;
- Raw Input route discovery and bounded timing capture;
- StandardCimv2 RSS provider reader/correlation;
- xHCI/network interrupt attribution.

Missing/ambiguous platform evidence stays explicit rather than being inferred from names or registry hints.

### `LatencyPilot.Persistence`

Concrete SQLite journal/recovery boundary:

- schema/version ownership;
- compare-and-swap revisions;
- unresolved-state blocking;
- retained `Kept` changes;
- read-only uninstall safety inspection;
- durable state required for exact recovery.

It is not a generic application repository layer.

### `LatencyPilot.Service`

Privileged boundary. Public v6 is observation-only. Internal source may prepare/apply/revert only narrowly supported operations behind durable journal/recovery semantics. The owner-only validation harness can exercise internals for Gate A but is not a product mutation surface.

The Service never becomes a generic scripting host.

### `LatencyPilot.App`

Normal-user WinUI experience. Local non-privileged inventory can call Platform.Windows directly. Privileged observation crosses Protocol to the Service. Deterministic interpretation belongs in Benchmarking.

Current read-only device inspector surfaces:

- representative GPU/NIC/xHCI interrupt evidence;
- exact USB input route/port evidence;
- RSS provider/PnP evidence;
- explicit on-demand five-second host Raw Input timing capture.

System-changing UI remains absent until Gate D.

## 6. Dependency direction

```text
Core                 ← no project dependency
Benchmarking         → Core
Protocol             ← standalone
Platform.Windows     → Core
Persistence          ← no LatencyPilot project dependency
Service              → Core + Benchmarking + Protocol + Platform.Windows + Persistence
App                  → Core + Benchmarking + Protocol + Platform.Windows
CriticalTests        → only projects required by durable critical contracts
```

No cyclic references and no speculative abstraction projects.

## 7. Privilege and deployment

The App is non-elevated. Installer deployment places App/Service under protected Program Files paths. A portable distribution may keep the App in a user-controlled extraction directory, but Service installation must copy the privileged payload to:

```text
%ProgramFiles%\LatencyPilot\Service
```

before LocalSystem registration. LocalSystem must never run the Service executable directly from a user-writable portable folder.

Install/upgrade/uninstall must not destroy recovery tools while LatencyPilot still owns a retained or unresolved system change.

## 8. Evidence semantics

Never collapse:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

Examples:

- registry `MSISupported` / affinity policy = stored configuration;
- ConfigMgr IRQ resources = allocated-resource evidence;
- ETW DPC/ISR events = runtime behavior.

Likewise:

```text
host-observable Raw Input report timing
≠ physical device latency
≠ click-to-photon latency
```

and:

```text
Internet RTT
≠ authoritative local adapter benchmark
```

Unavailable/ambiguous evidence stays unavailable/ambiguous.

## 9. Measurement products

### Quick diagnostic

```text
1 × 5 s
purpose = quick-diagnostic-snapshot
```

For integrity, attribution, concentration and hypothesis generation only.

### Decision baseline — `baseline-quality-v2`

```text
workload already warmed/repeatable where applicable
5 s LatencyPilot/service settle
5 × 20 s windows
750 ms inter-window settle
```

A window requires:

```text
requested duration >= 20,000 ms
actual/request >= 95%
clean capture integrity
DPC >= 1,000 events
ISR >= 1,000 events
finite positive DPC/ISR p99
```

Across eligible windows:

```text
relative noise floor = (P90 - P10) / |median| <= 30%
early/late relative drift <= 20%
extreme window relative deviation <= 50%
```

No inconvenient window is silently dropped.

### Optimizer readiness — `workload-stability-v1`

A valid latency baseline is necessary but not sufficient for optimizer experiments. The same five windows are additionally checked for workload activity consistency using:

- DPC event rate;
- ISR event rate;
- system CPU busy when complete runtime evidence exists;
- early/late activity drift <= 25%;
- maximum single-window relative deviation <= 50%.

Changing or spiky workload activity blocks candidate planning. This protects against a quiet early block, busy late block, or isolated workload spike masquerading as a tuning effect.

## 10. Canonical statistics

Percentiles use `LatencyPilot.Benchmarking.Statistics.Percentiles` and linear interpolation at:

```text
position = (sampleCount - 1) * percentile
```

DPC/ISR p99.9 is withheld below 10,000 samples for that individual distribution.

## 11. ETW architecture

```text
Service/session control
→ kernel provider configuration
→ capture
→ event decoding
→ bounded normalized events
→ processor attribution
→ image/module attribution
→ metric distributions/aggregates
→ evidence / baseline / experiment interpretation
```

Loss, invalid evidence and event-limit state are explicit. Already-loaded images require supported rundown/capture-state behavior. Raw event logging is not used in hot ETW callbacks.

## 12. IPC and authorization

Protocol v6 is local, typed, versioned and observation-only:

- no arbitrary command names;
- no shell/process execution primitive;
- no arbitrary registry path/value primitive;
- bounded frames/deadlines;
- unknown JSON members fail closed;
- network identities denied;
- pipe ACL narrowed to required local identities;
- client session must match active console session;
- disconnect/protocol misuse cancels owned capture.

Observation authorization is **not** mutation authorization.

```text
Gate A  owner-only physical mutation substrate proof; v6 stays read-only
Gate B  mutation-specific typed/allowlisted IPC + authorization source
Gate C  physical proof of real client/App → Service mutation boundary
Gate D  user-facing product arming
```

## 13. Evidence schema

Current export:

```text
schema = latencypilot-evidence-v9
protocolVersion = 6
purpose = quick-diagnostic-snapshot | repeated-decision-baseline
sourceRevisionId = exact clean source when available
```

Observation artifacts contain one bounded capture. Baseline artifacts retain aligned captures/windows/runtime context plus `baseline-quality-v2`, the serialized `workload-stability-v1` result, and the explicit `gpu-affinity-v1` optimizer-eligibility result/reason. The schema therefore preserves the distinction between `quality.isValidForComparison` and `optimizerEligibility.isEligible`; one must never be inferred from the other.

Serialization/file I/O occurs outside authoritative measurement windows. Saved JSON receives SHA-256 verification metadata.

## 14. Internal GPU experiment architecture

```text
valid baseline + stable workload
→ bounded physical-core candidates from measured pressure
→ exact original affinity snapshot
→ journal prepare
→ candidate apply + stored verify + exact-target activation
→ Measuring
→ synchronized ETW + raw PresentMon capture
→ verify expected stored state before/after
→ verify Candidate GPU ISR placement
→ exact rollback before next screening candidate
→ finalist only
→ ABBA + BAAB eight-run confirmation
→ AwaitingDecision only after final Candidate evidence
→ Keep after final state/driver re-read
   otherwise RestoreOriginal / RecoveryRequired
```

Screening cannot Keep directly. Missing guardrails, dirty identity, insufficient duration/samples, failed runtime placement or incomparable evidence remain Inconclusive/Restore rather than being promoted.

## 15. USB/xHCI/input source architecture

Read-only source flow:

```text
Raw Input device interface
→ PnP instance/ancestry
→ xHCI ancestor
→ documented USB hub enumeration
→ IOCTL port driver-key evidence
→ unique exact driver-key correlation
→ hub/port/controller route
```

No VID/PID/name heuristic is accepted as exact port proof.

Input timing:

```text
selected Raw Input device
→ bounded message-only capture window
→ fail if registration would overwrite an existing process registration
→ raw report arrival timestamps
→ interval statistics / rate / jitter / gap / burst evidence
```

The read-only readiness layer combines exact route, host timing and xHCI module-attributed ETW evidence. A system-changing xHCI affinity experiment is deferred until Gate A physically validates the shared mutation substrate.

## 16. NIC/RSS source architecture

Read-only source flow:

```text
Root\StandardCimv2
→ MSFT_NetAdapterRssSettingData
→ bounded provider fields
→ exact/unique adapter identity correlation where available
→ PnP driver identity
→ vendor-driver + generic NDIS ETW attribution kept distinct
```

Local benchmark interpretation consumes caller-supplied bounded observations and exposes RTT, jitter, loss, throughput and CPU metrics. Internet observations remain supplemental.

A system-changing RSS/affinity experiment is deferred until Gate A physically validates the shared mutation substrate.

## 17. Profiles, Pareto and global restore

Profiles are immutable/versioned policy definitions. Per-subsystem opt-out changes execution eligibility, not evidence retention.

Pareto policy reports only:

```text
Dominates
Dominated
Equivalent
Tradeoff
Inconclusive
```

No property or path named `Score` combines unlike metrics.

Global Restore Baseline treats retained managed changes as active state. It plans newest-first unwind, fails closed on unresolved/unknown mutation kinds and delegates restoration to narrow subsystem-specific recovery executors. It never becomes a generic privileged writer.

## 18. Release and recovery architecture

Hosted CI remains test-only. Owner-local release flow:

```text
clean exact main
+ exact successful Tests workflow
→ Release restore/build/publish
→ WinUI resource + launch smoke
→ explicit payload assembly
→ canonical SHA-256 payload manifest
→ installer + portable package hashes
→ Authenticode sign/verify when configured/required
→ RFC 3161 timestamp
→ GitHub release
```

Final releases require signing configuration. SHA-256/RFC 3161 hooks are source capability; only an actual signed owner-local build is signing evidence.

Upgrade/uninstall performs read-only journal safety checks before stopping/replacing/removing protected recovery tools and checks again after Service shutdown. `Kept` remains unsafe for removal because it represents an active managed system change.

Diagnostics export is local/redacted and performs no automatic upload.

## 19. Permanent test strategy

- Default/target: **10** permanent tests.
- Current durable suite: **18**.
- Owner-authorized maximum: **20** only when needed for `<=1200` lines per test file or materially safer durable failure isolation.
- Current subsystem-specific USB, input, NIC/RSS, profile/Pareto, restore, workload-readiness, GPU runtime-placement and installed-Service source-provenance contracts intentionally use that separation.
- Temporary/obsolete tests must be removed rather than accumulated.

Hardware validation is separate from this budget.

## 20. Completion discipline

Hosted Tests prove deterministic/source contracts only. They do not prove App compilation/runtime, LocalSystem Service behavior, hardware placement, device restart, installer behavior, signing or accessibility.

Physical sequence remains:

1. close Phase 2 read-only physical checks;
2. Gate A internal GPU substrate proof;
3. Gate B typed mutation IPC;
4. Gate C physical IPC proof;
5. Gate D user-facing GPU arming;
6. supported USB/xHCI mutation implementation + physical proof;
7. supported NIC/RSS mutation implementation + physical proof;
8. bounded multi-subsystem profile/Restore validation;
9. UI/accessibility and signed package/install/upgrade/uninstall/recovery audit;
10. representative hardware + final 1.0 tag audit.

`PROJECT_STATUS.md` owns the exact current execution order and blockers.
