# Premium Acrylic Shell Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current custom scroll-navigation dashboard with a premium Acrylic-backed WinUI 3 shell using native NavigationView, simpler information hierarchy, consistent Fluent icons, and four clear product views without changing measurement, evidence, protocol, or optimizer semantics.

**Architecture:** Keep the existing `MainWindow` partial C# files as the authoritative orchestration owner to avoid a parallel UI state layer. Re-parent the existing named controls into four independent view hosts inside a native `NavigationView`; `DashboardShell.cs` owns routing/adaptive shell behavior. Reuse all existing real-data chart controls and measurement/rendering logic.

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK 2.4.0, native `DesktopAcrylicBackdrop`, `NavigationView`, `SymbolIcon` / Segoe Fluent Icons.

**Spec:** `docs/superpowers/specs/2026-09-14-premium-dashboard-redesign-design.md`

## Global Constraints

- Work directly on `main`; user explicitly authorized this repository workflow.
- Do not take a local build. The existing GitHub Actions critical-test graph is the compile/test gate.
- Do not change protocol v6, evidence semantics, ETW capture, optimizer readiness rules, mutation availability, or physical gate requirements.
- Do not add a UI/chart package.
- Preserve all existing keyboard accelerators and evidence export behavior.
- Preserve Dark, High Contrast, text scaling and AutomationProperties support.
- Keep permanent test count within the owner-authorized ceiling; do not add brittle UI wording tests.

---

### Task 1: Acrylic-compatible design system

**Files:**
- Modify: `src/LatencyPilot.App/Design/DesignTokens.xaml`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`
- Modify: `docs/DESIGN_SYSTEM.md`

**Interfaces:**
- Consumes: existing semantic theme resources and component styles.
- Produces: shell/page/status/icon styles consumed by the rebuilt `MainWindow.xaml`.

- [ ] **Step 1: Remove the active dependency on the gradient shell background**

Keep `ShellBackdropBrush` only as a compatibility/fallback semantic brush if existing code still references it, but make the rebuilt shell transparent so `DesktopAcrylicBackdrop` is visible. Add semantic brushes for translucent navigation/page surfaces using the existing Light/Dark/HighContrast dictionaries rather than screen-local colors.

- [ ] **Step 2: Add reusable premium shell recipes**

Add styles/resources for `PageSurfaceStyle`, `StatusRowStyle`, `StatusBadgeStyle`, `CompactIconButtonStyle`, consistent icon sizes, and page section spacing. Reuse existing radius and typography resources rather than introducing one-off values.

- [ ] **Step 3: Reconcile design-system documentation**

Document Acrylic as the shell material, native NavigationView as primary navigation, and the four-view information architecture. Remove stale wording that describes the custom scroll rail as canonical.

- [ ] **Step 4: Commit**

Commit the design-system tranche before the structural XAML rewrite so a reviewer can independently reject/accept the visual foundation.

---

### Task 2: Native Acrylic + NavigationView shell

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/DashboardShell.cs`
- Modify: `src/LatencyPilot.App/WindowChrome.cs` only if title-bar hit testing requires adjustment.

**Interfaces:**
- Consumes: existing title-bar, appearance, service status and navigation handlers.
- Produces: `AppNavigationView`, `OverviewView`, `MeasureView`, `DevicesView`, `EvidenceView` named hosts and native navigation selection routing.

- [ ] **Step 1: Apply the supported window backdrop**

Use the Windows App SDK contract:

```xaml
<Window.SystemBackdrop>
    <DesktopAcrylicBackdrop />
</Window.SystemBackdrop>
```

Keep `RootGrid` transparent rather than painting the current gradient across the entire window.

- [ ] **Step 2: Replace the custom navigation rail with NavigationView**

Use exactly four primary `NavigationViewItem` destinations tagged `overview`, `measure`, `devices`, and `evidence`. Use `SymbolIcon` where the enum is sufficient and `FontIcon` with `SymbolThemeFontFamily` otherwise. Put the existing Appearance control in `NavigationView.PaneFooter`.

- [ ] **Step 3: Introduce four view hosts**

Re-parent existing named UI elements into four independent view containers. Only one host is visible at a time. Do not duplicate any evidence value or analyzer result to make the split work.

- [ ] **Step 4: Replace scroll-navigation code**

Change `DashboardShell.cs` routing from `ScrollDashboardTo(...)` and selected-button style mutation to `NavigationView.SelectionChanged` plus view-host visibility. Remove dead custom rail sizing/label hiding logic after native adaptive navigation owns it.

- [ ] **Step 5: Preserve appearance and service status behavior**

The service status remains truthful and mutation-disabled. Appearance still changes `RootGrid.RequestedTheme`; do not let Acrylic become a requirement for readability.

- [ ] **Step 6: Push and use hosted CI as the RED/GREEN compile gate**

Expected result: App compiles through the critical-test dependency graph with no missing named controls and the existing critical suite still passes.

---

### Task 3: Premium Overview hierarchy

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/PremiumObservationUi.cs` only where output copy/visibility must match the new hierarchy.
- Modify: `src/LatencyPilot.App/DashboardVisuals.cs` only for presentation synchronization, not metric semantics.

**Interfaces:**
- Consumes: existing summary text fields, readiness state, four chart controls and system inventory values.
- Produces: one-viewport consumer overview with clear next action and separate baseline/readiness truths.

- [ ] **Step 1: Build the Overview header and action hierarchy**

Keep Quick Snapshot and Build Baseline as the two visible capture actions. Make Export JSON lower priority. Preserve Ctrl+O, Ctrl+B and Ctrl+E.

- [ ] **Step 2: Separate baseline quality from optimizer readiness**

Render status as separate rows/badges so `Baseline valid` can coexist with `Optimizer not ready`. Transient-tail evidence is an additional status, not the headline verdict.

- [ ] **Step 3: Simplify system context**

Replace the large persistent `This PC` rail with a compact surface containing Windows/build, CPU topology summary, primary GPU/driver and observation-service status. Move secondary architecture/group counts away from the primary viewport while keeping their named values available in technical views if existing code updates them.

- [ ] **Step 4: Keep real charts, improve visual balance**

Reuse `LatencyProfileChart`, `CpuDistributionChart`, `ModuleContributionChart`, and `CpuInterruptMap`; change only layout, spacing and hierarchy. Do not invent new timeline data.

- [ ] **Step 5: CI checkpoint**

Push this tranche and require the exact commit's GitHub Actions test run to complete successfully before continuing.

---

### Task 4: Measure, Devices and Evidence views

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/MeasurementExperience.cs` only if control location/visibility synchronization requires it.
- Modify: `src/LatencyPilot.App/MeasurementReadinessExperience.cs` only if control location/visibility synchronization requires it.
- Modify: `src/LatencyPilot.App/DeviceEvidenceUi.cs` only if the new Devices host needs presentation synchronization.

**Interfaces:**
- Consumes: existing baseline-preparation, recent snapshot, device evidence and exact-evidence controls.
- Produces: focused workflow views with no duplicated measurement logic.

- [ ] **Step 1: Build Measure view**

Place quick snapshot, baseline scenario/preparation, progress and latest readiness results on one focused measurement page. Keep workload-change retry guidance visible without exposing unnecessary internal gate jargon.

- [ ] **Step 2: Build Devices view**

Group display/GPU, network/RSS and USB/xHCI evidence into three domain surfaces with Fluent icons and progressive disclosure. Device detection must not imply tunability.

- [ ] **Step 3: Build Evidence view**

Move exact metrics, attribution, top modules, CPU concentration, baseline windows, export and measurement context into the technical view.

- [ ] **Step 4: Remove nested scrolling**

Top-N module/CPU lists are already bounded; size them to content or otherwise rely on the page scroll. Do not leave ListView scrollbars nested inside the main ScrollViewer.

- [ ] **Step 5: CI checkpoint**

Push and require exact-commit green CI.

---

### Task 5: Fluent iconography, accessibility and responsive polish

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`
- Modify: `src/LatencyPilot.App/DashboardShell.cs`

**Interfaces:**
- Consumes: rebuilt shell/view hierarchy.
- Produces: consistent native iconography and adaptive behavior.

- [ ] **Step 1: Normalize icon controls**

Replace routine custom path/icon mixtures with `SymbolIcon` or `FontIcon FontFamily="{StaticResource SymbolThemeFontFamily}"`. Keep custom waveform geometry only for LatencyPilot identity/latency-specific branding.

- [ ] **Step 2: Verify automation and tooltips**

Every icon-only or compact control gets a meaningful `AutomationProperties.Name`; ambiguous actions keep visible text.

- [ ] **Step 3: Use NavigationView adaptive pane behavior**

Remove the old manual compact-rail implementation. Page grids may still reflow cards, but no visible child may live in a zero-width column.

- [ ] **Step 4: Check theme semantics**

Ensure Dark and High Contrast resources exist for any new semantic brush. Do not use transparency as the only separation mechanism.

- [ ] **Step 5: CI checkpoint**

Require exact-commit green hosted CI.

---

### Task 6: Cleanup, reconciliation and final verification

**Files:**
- Modify: `docs/DESIGN_SYSTEM.md`
- Modify: `PROJECT_STATUS.md` only if it currently describes the old shell as current product behavior.
- Modify: `ROADMAP.md` only if a checked UI item becomes stale/incorrect.
- Delete no source file unless every consumer is proven gone.

**Interfaces:**
- Consumes: completed redesign and hosted CI evidence.
- Produces: coherent repo state and a visual-validation handoff.

- [ ] **Step 1: Remove dead custom-navigation code/resources**

Delete old scroll-anchor navigation handlers/styles/tokens only after code search and compile proof show they are no longer consumed.

- [ ] **Step 2: Review the complete diff**

Verify that no protocol, evidence-schema, ETW, optimizer, service-boundary or mutation-domain file changed as part of UI work unless an independently discovered bug required it.

- [ ] **Step 3: Run final hosted verification**

Use the existing GitHub Actions workflow on the exact final `main` SHA. Required: workflow `success`, critical suite passes with zero failed/skipped tests, and App compiles through the test project graph.

- [ ] **Step 4: Record the visual-review boundary**

Source/CI completion does not claim visual completion until the owner renders the actual app on Windows at 1664×936, approximately 1280×820, Dark, High Contrast, transparency-disabled, and increased text scale.

- [ ] **Step 5: Commit final reconciliation**

Stop after the final exact-HEAD green run; do not add unrelated refactors or new optimizer features in this tranche.
