# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Work is not complete from memory or chat context; evidence is recorded here.

Last updated: 2026-09-12

## Overall

- Product completion: **Phases 0–1 closed; Phase 2 in progress**
- Current release target: **Phase 2 read-only observation engine**
- Current mutation capability: **None by design**
- Supported target: **Windows 11 x64**
- Current desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Permanent automated tests: **9 / hard maximum 10**
- Latest green CI after UI migration: **run `34708293765`**, commit `99ac3f6e82a6567b1ed188d5d7280980b9d5dc0f`

## Phase 0 — CLOSED

Governance, licensing, contribution policy, security policy, architecture, benchmark methodology, roadmap/status tracking and GitHub templates are present on `main`.

## Phase 1 — CLOSED

Phase 1 established the buildable solution, domain/comparison foundation, non-mutating app vertical slice and focused critical suite.

Historical note: Phase 1 originally closed with WPF. ADR 0002 later superseded the UI choice with WinUI 3. The historical WPF evidence remains valid as Phase 1 evidence and is not rewritten.

## Architecture change — WinUI 3 migration — COMPLETE

Decision: `docs/adr/0002-winui3.md`.

Implemented:

- `LatencyPilot.App` migrated from WPF to WinUI 3;
- Windows App SDK pinned to Stable `2.4.0`;
- unpackaged deployment via `WindowsPackageType=None`;
- Windows App SDK self-contained deployment enabled;
- app pinned to `win-x64` because self-contained Windows App SDK requires a concrete supported architecture;
- normal-user/non-elevated architecture unchanged;
- no MVVM framework, DI container, second UI toolkit or MSIX identity added.

Evidence: GitHub Actions run `34708293765` passed Release build, all 9 permanent tests, self-contained `win-x64` publish and artifact upload.

Failure encountered and resolved: initial WinUI migration run `34708180338` failed because a self-contained Windows App SDK build was still architecture-agnostic. Root cause was fixed by setting `RuntimeIdentifier=win-x64`; no warning/error suppression was used.

## Phase 2 — IN PROGRESS

Goal: trustworthy, strictly read-only Windows observation before any system mutation.

### 2.1 Inventory — implemented/verified in CI

- [x] processor-group-aware package/core/logical processor topology implementation;
- [x] stable present PnP device instance IDs through SetupAPI;
- [x] driver provider/version/INF metadata through unified device properties;
- [x] stored interrupt configuration read-only inspection (`MSISupported`, message limit, affinity policy/mask where present);
- [x] interrupt-configuration availability provenance so an inaccessible hardware key is not silently treated as “no configuration”.

Evidence runs:

- CPU topology: `34705285359`;
- PnP inventory: `34705480141`;
- driver metadata: `34705791564`;
- interrupt configuration failure discovery: `34705881655`;
- hardware-key partial-data fix: `34706052779`.

### 2.1 Inventory — still required

- [ ] physical Windows 11 validation of topology and representative GPU/xHCI/NIC inventory;
- [ ] allocated IRQ/resource assignment from Configuration Manager;
- [ ] distinguish line/message interrupt evidence where authoritative source data permits it.

Important semantics: stored registry configuration is **not** active IRQ assignment and is **not** runtime DPC/ISR evidence.

### 2.2 ETW — remaining

- [ ] controlled ETW session lifecycle;
- [ ] DPC/ISR capture;
- [ ] per-CPU attribution;
- [ ] module/driver attribution;
- [ ] p50/p95/p99/p99.9/max distributions;
- [ ] cancellation/cleanup on failure.

### 2.3 Baseline quality — remaining

- [ ] repeated baseline windows;
- [ ] measured noise floor;
- [ ] drift detection;
- [ ] background/thermal quality warnings where observable;
- [ ] invalid baseline blocks optimization.

### 2.4 UX — remaining

- [ ] per-CPU concentration view;
- [ ] top DPC/ISR contributors;
- [ ] raw metric inspection;
- [ ] baseline quality verdict/reason;
- [ ] evidence-level labels for configuration vs assigned resource vs runtime behavior.

## Comparison-core correction discovered during step-back review

A prior comparator path returned `NoMeasurableDifference` before evaluating guardrails when the primary metric was inside its noise threshold. That could hide a collateral regression. It was corrected so a neutral primary plus materially regressed guardrail is `Regressed`.

Evidence: commit `83aa8b18ee17e3c9476ed735741812fc0bedab82`, run `34705597798`.

This justified one high-blast-radius permanent test; the suite now remains at 9/10.

## Current next action

1. read allocated IRQ resources from Configuration Manager (`ALLOC_LOG_CONF`) while preserving partial/unavailable status;
2. keep stored configuration, assigned resources and runtime behavior as separate models;
3. then build controlled read-only ETW DPC/ISR capture;
4. only after ETW attribution works, move to repeated baseline/noise/drift UX.

No affinity/MSI mutation is allowed yet. Phase 3 owns privileged mutation, durable journal and rollback.

## Hard test rule

Permanent automated tests may not exceed **10**. Temporary implementation/debug tests may be created and removed. Exceeding 10 requires explicit owner approval plus an ADR explaining why staying within 10 creates greater risk.

## Completion discipline

Before closing a subsection, perform the mandatory step-back review from `AGENTS.md`: re-check API semantics, evidence naming, partial errors, resource lifetime, privilege boundaries, YAGNI, scaling behavior, test cap, docs drift and current owner constraints.
