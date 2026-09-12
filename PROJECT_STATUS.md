# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. It is intentionally explicit so work is not declared complete from memory or chat context.

Last updated: 2026-09-12

## Overall

- Product completion: **Phase 0 closed; Phase 1 in progress**
- Current release target: **V0.1 foundation**
- Current mutation capability: **None by design**
- Supported OS target: **Windows 11 x64**

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

## Phase 1 — IN PROGRESS

### 1.1 Toolchain and repository

- [ ] .NET 10 SDK pinned
- [ ] `.slnx` solution present
- [ ] shared build settings present
- [ ] all seven architecture projects present
- [ ] focused critical test project present
- [ ] Windows Release CI green
- [ ] WPF artifact produced by CI

### 1.2 Domain foundation

- [ ] experiment lifecycle states
- [ ] legal transition guard
- [ ] metric direction contract
- [ ] measurement-series validation
- [ ] explicit comparison verdict model

### 1.3 Comparison foundation

- [ ] percentile implementation
- [ ] configurable minimum sample count
- [ ] configured noise threshold
- [ ] target improvement/regression classification
- [ ] guardrail regression → Tradeoff
- [ ] insufficient data → Inconclusive

### 1.4 Read-only vertical slice

- [ ] WPF application project
- [ ] non-elevated launch contract
- [ ] real OS architecture displayed
- [ ] real process architecture displayed
- [ ] real logical CPU count displayed
- [ ] explicit “observation only / tuning unavailable” state

### 1.5 Critical tests

Budget: **no more than 8 Phase 1 tests**.

- [ ] invalid experiment transition
- [ ] insufficient samples
- [ ] no measurable difference
- [ ] clear improvement
- [ ] tradeoff from guardrail regression
- [ ] clear regression
- [ ] invalid/non-finite data

### Phase 1 close blockers

1. Code and build infrastructure do not yet exist.
2. CI has not yet produced a verified Release build.
3. No test evidence exists yet.

## Next action

Implement Phase 1 code and CI, then update this file only after GitHub Actions evidence is available.

## Status update rule

When work changes this file:

1. mark only independently verifiable items complete;
2. name failed or deferred items explicitly;
3. never mark a whole phase closed while any required checkbox in `ROADMAP.md` remains open;
4. hardware-dependent work is not complete based on a GitHub-hosted VM;
5. a green build proves buildability, not latency improvement.
