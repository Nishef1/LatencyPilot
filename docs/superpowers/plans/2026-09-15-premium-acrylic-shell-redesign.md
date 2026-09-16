# Premium Acrylic Shell Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish LatencyPilot as a Windows-native premium WinUI 3 utility: system-theme driven, Acrylic-backed, visually calmer, simpler for end users, and explicit about measurement/readiness without changing measurement or optimizer semantics.

**Architecture:** Keep the existing `MainWindow` partial C# files as the authoritative orchestration owner. Refine the existing four-view `NavigationView` shell rather than introducing a new page state/MVVM layer. Reuse all existing real-data chart controls and measurement/rendering logic; change presentation, visibility and routing only where needed.

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK 2.4.0, native `DesktopAcrylicBackdrop`, `NavigationView`, system theme resources, `SymbolIcon` / Segoe Fluent Icons.

**Spec:** `docs/superpowers/specs/2026-09-14-premium-dashboard-redesign-design.md`

## Global Constraints

- Work directly on `main`; user explicitly authorized this repository workflow.
- Do not take a local build. The existing GitHub Actions critical-test graph is the compile/test gate.
- Do not change protocol v6, evidence semantics, ETW capture, optimizer readiness rules, mutation availability, or physical gate requirements.
- Do not add a UI/chart package.
- Preserve all existing keyboard accelerators and evidence export behavior.
- Preserve Dark, High Contrast, text scaling and AutomationProperties support.
- Keep permanent test count within the owner-authorized ceiling; do not add brittle UI wording/markup tests.
- Preserve unrelated concurrent Gate A work.

---

### Task 1: Native Windows theme + Acrylic correctness

**Files:**
- Modify: `src/LatencyPilot.App/Design/DesignTokens.xaml`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`
- Modify: `src/LatencyPilot.App/PremiumObservationUi.cs`
- Modify: `src/LatencyPilot.App/MainWindow.xaml`

**Interfaces:**
- Consumes: current semantic resources and Window.SystemBackdrop.
- Produces: system-theme driven colors, true Desktop Acrylic, denser native component geometry.

- [ ] Default `RootGrid.RequestedTheme` to `Default`; saved Light/Dark still override it.
- [ ] Map primary text/card/stroke/accent semantics to current WinUI theme resources; keep analytical chart colors stable.
- [ ] Remove the runtime Mica override so the declared `DesktopAcrylicBackdrop` remains authoritative.
- [ ] Reduce title-bar/card/icon chrome to Windows 11 utility proportions.
- [ ] Push and require exact-commit green CI.

---

### Task 2: Overview information hierarchy and empty state

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/DashboardVisuals.cs`
- Modify: `src/LatencyPilot.App/DashboardShell.cs`

**Interfaces:**
- Consumes: current capture/baseline presence and existing chart controls.
- Produces: clean no-evidence onboarding state and evidence dashboard only when data exists.

- [ ] Keep **Quick snapshot** as the only Overview primary action.
- [ ] Move `Build baseline` ownership to Measure and `Export JSON` ownership to Evidence.
- [ ] Add a compact Overview no-evidence state; hide the four empty charts/metric grid until usable evidence exists.
- [ ] Keep system context visible but remove decorative wallpaper dependency; use real OS/GPU/CPU facts and a Fluent device icon.
- [ ] Shorten headings/subtitles that restate obvious UI.
- [ ] Push and require exact-commit green CI.

---

### Task 3: Service status and development-action placement

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/PremiumObservationUi.cs`
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`

**Interfaces:**
- Consumes: existing service badge/status strings and development Gate A availability.
- Produces: healthy service state that does not occupy a full-width banner and Gate A that no longer competes with consumer Overview actions.

- [ ] Make the title-bar mode badge neutral (`Read-only`) rather than a green health indicator.
- [ ] Collapse the full-width service row when connected; keep it visible for checking/degraded/disconnected states with Refresh.
- [ ] Move development-only `Run GPU Gate A` into the Measure view's developer-validation host and use quiet/secondary visual priority.
- [ ] Preserve all existing Gate A safety/authorization behavior.
- [ ] Push and require exact-commit green CI.

---

### Task 4: Fluent iconography and page polish

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`

**Interfaces:**
- Consumes: rebuilt shell/view hierarchy.
- Produces: consistent native iconography and lower visual noise.

- [ ] Use `SymbolIcon` for standard Home/Play/Devices/Document/Refresh/Save actions; use `FontIcon` only for specialized hardware/latency glyphs.
- [ ] Shrink metric icon containers from oversized circles to compact rounded tiles.
- [ ] Use native text hierarchy and reduce repeated explanatory prose.
- [ ] Keep every icon-only control accessible with AutomationProperties/tooltip.
- [ ] Ensure responsive reflow still works with the refined controls.
- [ ] Push and require exact-commit green CI.

---

### Task 5: Cleanup, documentation and final verification

**Files:**
- Modify: `docs/DESIGN_SYSTEM.md` if current guidance disagrees with final UI.
- Modify: `PROJECT_STATUS.md` / `ROADMAP.md` only when their current UI state is stale.
- Delete no source file unless every consumer is proven gone.

**Interfaces:**
- Consumes: completed final visual pass and hosted CI evidence.
- Produces: coherent repo state and owner render handoff.

- [ ] Search for stale Mica runtime assignment, decorative wallpaper loading, old Overview ownership of baseline/export, and Gate A insertion into Overview.
- [ ] Review the complete diff and confirm protocol/evidence/ETW/optimizer semantics did not change.
- [ ] Run the existing GitHub Actions workflow on the exact final `main` SHA; require workflow success and zero failed/skipped critical tests.
- [ ] Record that source/CI completion still requires owner visual review at 1664×936, approximately 1280×820, Dark, High Contrast, transparency-disabled, and increased text scale.
- [ ] Stop after exact-HEAD verification; do not add unrelated refactors or optimizer features in this tranche.
