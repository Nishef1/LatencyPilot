# LatencyPilot Design System

Status: canonical visual single source of truth (SSOT) for the WinUI app.

## Purpose

LatencyPilot should look like a calm, premium, native Windows 11 performance tool rather than a generic dashboard or a gaming tweak utility. Visual changes must be made through the design system before individual screens are edited.

## Canonical resources

- `src/LatencyPilot.App/Design/DesignTokens.xaml` — semantic colors, chart palette, spacing, type scale, radii, dimensions, shell widths, and page/card padding.
- `src/LatencyPilot.App/Design/ComponentStyles.xaml` — reusable text, navigation, card, chart-card, metric, button, pill, and list recipes.
- `src/LatencyPilot.App/App.xaml` — composition only. It loads WinUI resources and the two LatencyPilot dictionaries; it must not become another token store.
- `src/LatencyPilot.App/Controls/*Chart.cs` — focused native WinUI visualizations. They render display models only and never call ETW/service/persistence directly.

## Rules

1. Do not hard-code product colors in screens. Add or reuse a semantic brush in `DesignTokens.xaml`.
2. Do not invent one-off card radii, page padding, control heights, shell widths, or shared spacing when a token already represents the intent.
3. Reusable visual recipes belong in `ComponentStyles.xaml`, not copied across screens.
4. Light, Dark, and High Contrast variants for a semantic color use the same resource key.
5. Semantic status colors mean something: accent = action/selection, success = verified/healthy, warning = caution/inconclusive, impact = elevated concern, danger = failure/severe signal. Do not use status colors decoratively.
6. Keep surfaces quiet. Prefer hierarchy, charts, spacing, and typography over gradients, excessive shadows, nested cards, or prose walls.
7. Dynamic C# UI must consume semantic resource keys (for example through the existing `ThemeBrush`/dashboard theme-resource paths) rather than introduce literal colors.
8. New screens should preserve Windows text scaling, keyboard access, AutomationProperties, High Contrast, and adaptive layout behavior.
9. Product safety state is information, not decoration. Observation-only/mutation-disabled claims must reflect the real service/protocol state and must never be changed only to improve appearance.
10. A successful build is not visual approval. Material UI changes require a real Windows render review at the target window sizes.
11. Charts must visualize evidence the current protocol actually provides. Never synthesize a continuous timeline, heatmap time bucket, performance delta, or health verdict merely to match a mockup.
12. Missing evidence renders an intentional empty state. Placeholder data that could be mistaken for a real measurement is prohibited.

## Truthful chart contract

The premium dashboard uses four native visual components:

- `LatencyProfileChart` — latest real DPC/ISR percentile shape (`p50`, `p95`, `p99`, `p99.9`, `max`), or the five real baseline-window p99 values after a repeated baseline.
- `CpuDistributionChart` — each logical processor's real share of observed DPC + ISR events.
- `ModuleContributionChart` — real attributed module duration normalized against total attributed kernel time.
- `CpuInterruptMap` — DPC share and ISR share for the busiest observed processors. It is heatmap-like visually but does **not** invent time buckets.

If future protocol versions add timestamped event buckets, a true time-series/temporal heatmap may replace these views only after that evidence is available end-to-end.

## Current visual direction

- Native Windows 11 / Fluent foundation.
- Mica-backed window chrome with a compact custom title area.
- Light-first appearance with dark and High Contrast parity.
- Blue interaction accent with indigo/blue chart semantics; semantic green/amber/orange/red only when warranted.
- Flat translucent surfaces, subtle borders, and very limited elevation.
- Chart-first evidence: visual pattern first, exact values available through progressive disclosure.
- Compact left section navigation with a real dashboard canvas rather than a long engineering document.
- Primary flow: system readiness -> quick snapshot or repeated baseline -> visual evidence -> exact evidence/export.

## Main workspace hierarchy

The observation dashboard intentionally prioritizes:

1. current latency health evidence and primary actions,
2. DPC/ISR p99, CPU concentration, and last-baseline state,
3. latency profile, CPU distribution, module contribution, and CPU interrupt-map visuals,
4. compact machine/device context,
5. baseline preparation and recent snapshot state,
6. exact counts, attribution detail, quality explanations, and baseline-window tables behind progressive disclosure.

Phase names, internal gate terminology, mutation implementation details, and long methodology explanations belong in engineering/status documentation or tooltips unless they materially affect a user decision.

## Shared dashboard keys

Use these instead of screen-local substitutes:

- dimensions: `NavigationRailWidth`, `ContextPanelWidth`, `ContentMaxWidth`, `ChartMinHeight`, `DashboardGap`;
- chart brushes: `ChartGridBrush`, `ChartAccentPrimaryBrush`, `ChartAccentSecondaryBrush`, `ChartAccentTertiaryBrush`, `ChartWarmBrush`, `ChartCellBrush`;
- navigation brushes: `NavigationBrush`, `NavigationSelectedBrush`, `NavigationIconBrush`;
- recipes: `DashboardCardStyle`, `ChartCardStyle`, `DashboardMetricStyle`, `IconTileStyle`, `NavigationRailStyle`, `NavigationItemStyle`, `NavigationItemSelectedStyle`.

## Change checklist

Before adding a new literal visual value, ask:

- Is this a semantic token already?
- Will another screen need the same value?
- Does it require Light/Dark/High Contrast variants?
- Is it a reusable component recipe rather than a screen-specific layout choice?
- Does the visualization directly map to evidence we actually collect?

If any answer points to reuse, update the SSOT first and consume the resource from the screen. If the evidence does not exist, change the visual rather than fabricate the data.
