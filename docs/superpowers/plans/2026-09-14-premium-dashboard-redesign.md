# Premium Dashboard Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild LatencyPilot's main WinUI screen into the approved premium, chart-first Windows 11 dashboard without fabricating data or changing measurement/mutation semantics.

**Architecture:** Keep the existing service/measurement pipeline intact. Add four focused native WinUI visualization controls plus a `DashboardVisuals` adapter partial, then replace the current text-heavy `MainWindow.xaml` composition with a token-driven shell that consumes those controls. Dynamic readiness/device code is compacted but remains behaviorally equivalent.

**Tech Stack:** C# 14, .NET 10, WinUI 3 / Windows App SDK 2.4 Stable, existing MSTest/Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-09-14-premium-dashboard-redesign-design.md`

## Global Constraints

- No new UI/chart dependency.
- No fabricated time-series or heatmap data.
- Protocol remains v6 and mutation remains unavailable.
- Reuse `DesignTokens.xaml` and `ComponentStyles.xaml` as visual SSOT.
- Preserve existing accelerators, capture/export logic, baseline readiness gate, and accessibility semantics.
- Temporary UI verification may make the suite 10/10 only during implementation; final permanent suite returns to 9/9.
- Work is explicitly authorized directly on `main`.

---

### Task 1: Lock the dashboard structure with a temporary RED test

**Files:**
- Create temporarily: `tests/LatencyPilot.CriticalTests/TemporaryPremiumDashboardTests.cs`

**Interfaces:**
- Consumes: repository files only.
- Produces: a temporary structural invariant proving the new controls/SSOT composition exist.

- [ ] Add a temporary MSTest that expects the four visualization control files, `DashboardVisuals.cs`, and token keys such as `NavigationRailWidth`, `ChartGridBrush`, and `ChartAccentSecondaryBrush`.
- [ ] Run the normal critical test workflow and confirm exactly one new failure caused by the missing dashboard artifacts while the 9 existing tests still pass.

### Task 2: Expand visual SSOT

**Files:**
- Modify: `src/LatencyPilot.App/Design/DesignTokens.xaml`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`
- Modify: `docs/DESIGN_SYSTEM.md`

**Interfaces:**
- Produces reusable keys for navigation, chart surfaces, metric cards, chart strokes, and compact dashboard spacing.

- [ ] Add light/dark/high-contrast chart brushes and shell dimensions to `DesignTokens.xaml`.
- [ ] Add navigation, dashboard-card, chart-card, compact-metric, and icon-tile styles to `ComponentStyles.xaml`.
- [ ] Document the chart/data-truth rule and the new component keys in `DESIGN_SYSTEM.md`.

### Task 3: Add native chart controls

**Files:**
- Create: `src/LatencyPilot.App/Controls/LatencyProfileChart.cs`
- Create: `src/LatencyPilot.App/Controls/CpuDistributionChart.cs`
- Create: `src/LatencyPilot.App/Controls/ModuleContributionChart.cs`
- Create: `src/LatencyPilot.App/Controls/CpuInterruptMap.cs`
- Create: `src/LatencyPilot.App/DashboardChartModels.cs`

**Interfaces:**
- `LatencyProfileChart.SetSeries(IReadOnlyList<ChartPoint> primary, IReadOnlyList<ChartPoint> secondary, IReadOnlyList<string> labels)`
- `CpuDistributionChart.SetBars(IReadOnlyList<ChartBar> bars)`
- `ModuleContributionChart.SetBars(IReadOnlyList<ChartBar> bars)`
- `CpuInterruptMap.SetRows(IReadOnlyList<InterruptMapRow> rows)`

- [ ] Implement each control using native `Canvas`, `Polyline`, `Rectangle`, `Border`, and `TextBlock` primitives only.
- [ ] Render explicit empty states instead of placeholder numbers.
- [ ] Resolve all colors via theme resources; no inline hex colors.
- [ ] Add automation names/text summaries for each chart.

### Task 4: Adapt real evidence into dashboard visuals

**Files:**
- Create: `src/LatencyPilot.App/DashboardVisuals.cs`
- Modify: `src/LatencyPilot.App/MainWindow.xaml.cs`
- Modify: `src/LatencyPilot.App/MeasurementExperience.cs`

**Interfaces:**
- Consumes `KernelLatencyCaptureResponse` and the existing baseline window/capture data.
- Updates the four chart controls, DPC/ISR p99 summary, CPU concentration summary, and last-baseline summary.

- [ ] Add empty-state initialization in `InitializeDashboardVisuals()`.
- [ ] Update the dashboard after every successful quick snapshot using real distribution/processor/module evidence.
- [ ] Update the stability chart and baseline summary after repeated baseline completion from the actual five windows.
- [ ] Clear/stale visuals when scenario changes or capture fails.

### Task 5: Recompose MainWindow to match the approved premium target

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/WindowChrome.cs`

**Interfaces:**
- Retains all x:Name contracts referenced by existing partial classes.
- Adds named dashboard anchors and chart controls.

- [ ] Replace the tall hero/service layout with a compact shell and fixed section navigation rail.
- [ ] Add dashboard header/actions and four summary cards.
- [ ] Add 2×2 visualization grid using the new controls.
- [ ] Keep `This PC` and `Device evidence` compact and useful in the rail.
- [ ] Move baseline preparation and recent snapshot into compact bottom cards.
- [ ] Implement section-navigation buttons that scroll to the real dashboard anchors, with no fake pages.
- [ ] Preserve adaptive behavior and High Contrast semantics.

### Task 6: Compact existing dynamic readiness/device UI

**Files:**
- Modify: `src/LatencyPilot.App/MeasurementExperience.cs`
- Modify: `src/LatencyPilot.App/MeasurementReadinessExperience.cs`
- Modify: `src/LatencyPilot.App/DeviceEvidenceUi.cs`
- Modify: `src/LatencyPilot.App/PremiumObservationUi.cs`

**Interfaces:**
- Existing behavioral methods and fields remain callable by the rest of the app.

- [ ] Replace the large scenario/readiness prose card with a compact scenario row and two short checklist items; keep full rationale in automation help/tooltips.
- [ ] Render device evidence as concise status + one action; keep the detailed dialog unchanged.
- [ ] Remove the verbose dynamic snapshot evidence card from the primary layout and redirect its state updates into the compact recent-snapshot visual state.
- [ ] Preserve Mica, service/baseline semantic coloring, and all evidence calculations.

### Task 7: RED→GREEN verification and cleanup

**Files:**
- Modify temporarily: `.github/workflows/ci.yml`
- Delete: `tests/LatencyPilot.CriticalTests/TemporaryPremiumDashboardTests.cs`

**Interfaces:**
- Produces final clean repository state.

- [ ] Temporarily add `dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Release` after critical tests.
- [ ] Run CI and require 10/10 temporary tests plus a WinUI Release build with 0 errors.
- [ ] Fix compile/XAML failures based on exact logs, not guesses.
- [ ] Remove the temporary test and temporary build step.
- [ ] Run final CI on exact HEAD and require 9/9 permanent tests.
- [ ] Recheck `ObservationProtocol` v6 and `ServiceBoundary.MutationAvailable == false`.
- [ ] Review the final diff to ensure it is limited to the dashboard/design/docs and contains no mutation or protocol arming.
