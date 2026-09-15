# LatencyPilot Design System

Status: canonical visual single source of truth (SSOT) for the WinUI app.

## Purpose

LatencyPilot should look like a calm, premium, native Windows 11 performance tool rather than a generic dashboard or a gaming tweak utility. Visual polish must never make the evidence or safety model less truthful.

## Canonical resources

- `src/LatencyPilot.App/Design/DesignTokens.xaml` — semantic colors, chart palette, spacing, type scale, radii, dimensions, and shared padding.
- `src/LatencyPilot.App/Design/ComponentStyles.xaml` — reusable text, card, chart, metric, button, pill, and list recipes.
- `src/LatencyPilot.App/MainWindow.xaml` — Acrylic-backed application shell, native `NavigationView`, and the four product views.
- `src/LatencyPilot.App/DashboardShell.cs` — view routing, adaptive card reflow, appearance and shell coordination. It does not own measurement rules.
- `src/LatencyPilot.App/App.xaml` — resource composition only.
- `src/LatencyPilot.App/Controls/*Chart.cs` — focused native WinUI visualizations. They render display models only and never call ETW/service/persistence directly.

## Rules

1. Do not hard-code product colors in screens when a semantic design token can represent the intent.
2. Do not invent one-off radii, shared spacing, control heights or page padding when the design system already owns that value.
3. Reusable visual recipes belong in `ComponentStyles.xaml`, not copied across views.
4. Light, Dark and High Contrast variants for a semantic color use the same resource key.
5. Semantic status colors mean something: accent = action/selection, success = verified/healthy, warning = caution/inconclusive/not-ready, impact = elevated concern, danger = failure/severe signal. Never use them merely as decoration.
6. Keep surfaces quiet. Prefer hierarchy, whitespace, charts and typography over nested cards, decorative gradients, excessive shadows or prose walls.
7. Dynamic C# UI consumes semantic resource keys through the existing theme-resource paths instead of introducing literal product colors.
8. Preserve Windows text scaling, keyboard access, AutomationProperties, High Contrast and adaptive behavior.
9. Product safety state is information, not decoration. Observation-only/mutation-disabled claims must reflect the real service/protocol state.
10. Build success is not visual approval. Material UI changes require a real Windows render review at target window sizes and themes.
11. Charts visualize evidence the current protocol actually provides. Never synthesize a continuous timeline, time-bucket heatmap, performance delta or health score merely to match a mockup.
12. Missing evidence renders an intentional empty state. Placeholder values that could be mistaken for real measurements are prohibited.
13. Comparison quality and optimizer readiness are separate truths. A valid baseline may still be not ready for an optimization experiment.

## Shell and material

The active shell is native WinUI 3:

- `DesktopAcrylicBackdrop` is the window-level material.
- The root content is transparent so the system backdrop can remain visible.
- A compact custom title bar carries product identity and the real observation-only state.
- A native `NavigationView` owns adaptive primary navigation.
- Content cards use semantic surfaces and borders so the application remains readable when Windows transparency is disabled.
- Acrylic is environmental material, not a blur effect applied independently to every card.

High Contrast never depends on translucency and continues to use Windows system colors.

## Information architecture

There are four primary destinations:

1. **Overview** — current latency evidence, baseline/readiness summary, charts, and the next meaningful action.
2. **Measure** — quick snapshot and five-window baseline preparation/capture.
3. **Devices** — Graphics, Network/RSS, and USB/xHCI read-only evidence.
4. **Evidence** — exact distributions, attribution, baseline windows, measurement context, and export/provenance.

Baseline is a measurement workflow, not a fifth top-level destination. Appearance lives in the navigation footer rather than competing with product destinations.

The old custom button rail and scroll-to-section navigation are not part of the current design. Navigation selection always corresponds to the visible view.

## Iconography

Routine product icons use WinUI controls:

- prefer `SymbolIcon` when the `Symbol` enum communicates the action,
- otherwise use `FontIcon` with `SymbolThemeFontFamily`, which maps to Segoe Fluent Icons on Windows 11,
- custom geometry is reserved for LatencyPilot-specific product/latency identity where a standard Fluent icon does not express the concept.

Optical targets:

- navigation: 16–18 px,
- compact actions: 14–16 px,
- section/status icons: 18–20 px,
- metric/hero icons: 20–24 px.

Ambiguous or safety-relevant actions keep visible text; color or icon alone is never the only meaning.

## Truthful chart contract

The Overview uses four native visual components:

- `LatencyProfileChart` — latest real DPC/ISR percentile shape (`p50`, `p95`, `p99`, `p99.9`, `max`), or the five real baseline-window p99 values after a repeated baseline.
- `CpuDistributionChart` — each logical processor's real share of observed DPC + ISR events.
- `ModuleContributionChart` — real attributed module duration normalized against total attributed kernel time.
- `CpuInterruptMap` — DPC share and ISR share for the busiest observed processors. It is heatmap-like visually but does **not** invent time buckets.

If a future protocol adds timestamped event buckets, a temporal chart may use them only after that evidence exists end-to-end.

## Overview hierarchy

The default consumer view prioritizes:

1. measurement availability and capture actions,
2. DPC p99, ISR p99, CPU concentration, and baseline/readiness state,
3. latency profile and CPU distribution,
4. module contribution and CPU interrupt map,
5. compact machine context.

Exact counts, p99.9/max detail, long quality explanations, provenance and baseline windows live in Evidence. Baseline preparation belongs in Measure. Detailed device/provider state belongs in Devices.

Phase names, mutation implementation details and engineering gate terminology remain in status/engineering documentation unless they materially affect a user decision.

## Surface hierarchy

Use three visual levels at most:

1. system Acrylic shell,
2. semantic page/section surface,
3. metric/action tile where grouping genuinely helps.

Prefer whitespace and dividers before adding another rounded rectangle. Bounded top-N evidence lists rely on the page scroll rather than nested scrolling regions.

## Shared keys

Continue using the semantic resources in `DesignTokens.xaml` rather than screen-local substitutes. Important active keys include:

- layout: `ContentMaxWidth`, `DashboardGap`, `PagePadding`, `ChartMinHeight`;
- surfaces: `PremiumSurfaceBrush`, `SurfaceAltBrush`, `SurfaceStrongBrush`, `BorderBrush`, `DividerBrush`;
- chart brushes: `ChartGridBrush`, `ChartAccentPrimaryBrush`, `ChartAccentSecondaryBrush`, `ChartAccentTertiaryBrush`, `ChartWarmBrush`, `ChartCellBrush`;
- status brushes: `SuccessBrush`, `WarningBrush`, `ImpactBrush`, `DangerBrush` and their soft counterparts;
- recipes: `DashboardCardStyle`, `ChartCardStyle`, `DashboardMetricStyle`, `MetricTileStyle`, `IconTileStyle`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `QuietButtonStyle`, `PillBorderStyle`.

Legacy rail-specific resources may remain only while another consumer still exists; remove them once code search and hosted compile proof confirm they are dead.

## Responsive behavior

`NavigationView` owns expanded/compact/overlay navigation behavior. Product grids may reflow their cards, but should not implement a second competing navigation breakpoint system.

Desktop intent:

- wide: expanded navigation plus multi-column cards/charts,
- medium: compact navigation plus one/two-column content,
- narrow: overlay/compact navigation and stacked content.

No visible control should be parked in a zero-width column as a substitute for responsive layout.

## Change checklist

Before adding or changing visual behavior, ask:

- Is this a semantic token or reusable component recipe?
- Does it preserve Light/Dark/High Contrast and transparency-disabled readability?
- Does a Fluent system icon already communicate this action?
- Does the visualization map directly to evidence we collect?
- Is this information important enough for Overview, or should it live in Measure/Devices/Evidence?
- Does the change preserve measurement/safety semantics?

If evidence does not exist, change the visual rather than fabricate the data.