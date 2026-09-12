# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. It is intentionally explicit so work is not declared complete from memory or chat context.

Last updated: 2026-09-12

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 not started**
- Current release target: **next milestone is Phase 2 read-only observation engine**
- Current mutation capability: **None by design**
- Supported OS target: **Windows 11 x64**
- Latest Phase 1 evidence run: **GitHub Actions `34704448960`**
- Evidence commit: **`d44ec29a0b87df0a818a19c1d576230f07b80b0d`**

## Phase 0 — CLOSED

Evidence on `main`:

- `README.md`
- `LICENSE`
- `CLA.md`
- `CONTRIBUTING.md`
- `SECURITY.md`
- `AGENTS.md`
- `SYSTEM_DESIGN.md`
- `docs/BENCHMARK_METHODOLOGY.md`
- `.github/CODEOWNERS`
- `.github/pull_request_template.md`
- `.github/ISSUE_TEMPLATE/*`
- `ROADMAP.md`
- `PROJECT_STATUS.md`

No Phase 0 blockers remain.

## Phase 1 — CLOSED

### 1.1 Toolchain and repository

- [x] .NET 10 SDK pinned at `10.0.401`
- [x] `.slnx` solution present
- [x] shared build settings present
- [x] all seven architecture projects present
- [x] one focused critical test project present
- [x] Windows Release CI green
- [x] self-contained WPF `win-x64` artifact produced by CI

Evidence: workflow run `34704448960` completed successfully on `main`.

### 1.2 Domain foundation

- [x] experiment lifecycle states
- [x] legal transition guard
- [x] metric direction contract
- [x] measurement-series finite-value validation
- [x] explicit comparison verdict model

Implemented under `LatencyPilot.Core`.

### 1.3 Comparison foundation

- [x] deterministic percentile implementation
- [x] configurable minimum sample count
- [x] configurable minimum relative-change/noise threshold
- [x] target improvement/regression classification
- [x] guardrail regression → `Tradeoff`
- [x] insufficient data → `Inconclusive`

Implemented under `LatencyPilot.Benchmarking`.

Important scope note: this is the Phase 1 comparison foundation only. Empirical baseline noise-floor estimation, drift detection, repeated A/B runs and richer uncertainty belong to Phase 2+ and are not falsely marked complete here.

### 1.4 Read-only vertical slice

- [x] WPF application project builds successfully
- [x] normal non-elevated desktop application contract
- [x] real OS architecture displayed
- [x] real process architecture displayed
- [x] real logical CPU count displayed
- [x] explicit observation-only / tuning-unavailable state
- [x] no fake optimization score or synthetic machine result

### 1.5 Critical tests

Active automated suite: **7 tests**. No coverage target and no per-file regression-test policy.

- [x] invalid experiment transition
- [x] insufficient samples
- [x] no measurable difference
- [x] clear improvement
- [x] tradeoff from guardrail regression
- [x] clear regression
- [x] invalid/non-finite data

All 7 passed in workflow run `34704448960`.

### Phase 1 artifact evidence

- Artifact: `LatencyPilot-win-x64`
- GitHub artifact ID: `10300772684`
- Size: `71,926,890` bytes (~68.6 MiB / 71.9 MB)
- SHA-256: `688143e85f4feb6708ef1f991e3bf19129b4fb2e476a5f628023281861f6af50`
- Produced from commit: `d44ec29a0b87df0a818a19c1d576230f07b80b0d`

### Phase 1 failures encountered and resolved

These are recorded so future work does not repeat them or confuse an earlier failed run with current status:

1. Windows projects initially used an insufficient target-platform version while declaring Windows 11 support; fixed by targeting `net10.0-windows10.0.26100.0` with supported minimum `10.0.22000.0`.
2. MSTest 4 removed the old `Assert.ThrowsException` API; critical tests use `Assert.ThrowsExactly`.
3. .NET 10 requires native Microsoft.Testing.Platform opt-in for MTP `dotnet test`; `global.json` now explicitly selects `Microsoft.Testing.Platform`.
4. analyzer `CA1822` caught a stateless instance method; the inventory reader was corrected instead of suppressing the warning.
5. publish is allowed to restore/build its `win-x64` runtime-specific graph instead of relying on a non-RID `--no-build` output.

No Phase 1 blockers remain.

## Phase 2 — NOT STARTED

Goal: trustworthy, strictly read-only Windows observation before any system mutation.

### 2.1 Inventory — remaining

- [ ] physical/logical CPU and SMT topology
- [ ] PCI/PnP device inventory with stable identities
- [ ] current interrupt policy and observable MSI/MSI-X state where supported
- [ ] relevant driver/provider versions

### 2.2 ETW capture — remaining

- [ ] controlled ETW session lifecycle
- [ ] DPC/ISR collection
- [ ] per-CPU attribution
- [ ] module/driver attribution
- [ ] p50/p95/p99/p99.9/max distributions
- [ ] cancellation and cleanup on failure

### 2.3 Baseline quality — remaining

- [ ] repeated baseline windows
- [ ] measured noise floor
- [ ] drift detection
- [ ] background/thermal-quality warnings where observable
- [ ] invalid baseline blocks optimization

### 2.4 UX — remaining

- [ ] per-CPU latency/load view
- [ ] top DPC/ISR contributors
- [ ] raw metric inspection
- [ ] baseline quality verdict with reason

## Next action

Begin Phase 2 with the read-only observation vertical slice in this order:

1. CPU topology + PnP/device inventory;
2. controlled ETW DPC/ISR capture;
3. normalized per-CPU/per-module evidence;
4. baseline repetition/noise/drift logic;
5. UI presentation of the real evidence.

Do **not** add affinity/MSI mutation yet. Phase 3 owns the privileged mutation substrate and rollback journal.

## Status update rule

When work changes this file:

1. mark only independently verifiable items complete;
2. name failed or deferred items explicitly;
3. never mark a whole phase closed while any required checkbox in `ROADMAP.md` remains open;
4. hardware-dependent work is not complete based on a GitHub-hosted VM;
5. a green build proves buildability, not latency improvement;
6. keep the active automated test suite within the focused 5–10-test policy unless the owner explicitly approves expansion.
