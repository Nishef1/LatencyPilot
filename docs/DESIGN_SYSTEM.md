# LatencyPilot Design System

Status: canonical visual single source of truth (SSOT) for the WinUI app.

## Purpose

LatencyPilot should look like a calm, premium, native Windows 11 performance tool rather than a generic dashboard or a gaming tweak utility. Visual changes must be made through the design system before individual screens are edited.

## Canonical resources

- `src/LatencyPilot.App/Design/DesignTokens.xaml` — semantic colors, spacing, type scale, radii, dimensions, and page/card padding.
- `src/LatencyPilot.App/Design/ComponentStyles.xaml` — reusable text, card, metric, button, pill, and list recipes.
- `src/LatencyPilot.App/App.xaml` — composition only. It loads WinUI resources and the two LatencyPilot dictionaries; it must not become another token store.

## Rules

1. Do not hard-code product colors in screens. Add or reuse a semantic brush in `DesignTokens.xaml`.
2. Do not invent one-off card radii, page padding, control heights, or spacing when a token already represents the intent.
3. Reusable visual recipes belong in `ComponentStyles.xaml`, not copied across screens.
4. Light, Dark, and High Contrast variants for a semantic color use the same resource key.
5. Semantic status colors mean something: accent = action/selection, success = verified/healthy, warning = caution/inconclusive, impact = elevated concern, danger = failure/severe signal. Do not use status colors decoratively.
6. Keep surfaces quiet. Prefer hierarchy, spacing, and typography over gradients, excessive shadows, or nested cards.
7. Dynamic C# UI must consume semantic resource keys (for example through the existing `ThemeBrush` path) rather than introduce literal colors.
8. New screens should preserve Windows text scaling, keyboard access, AutomationProperties, High Contrast, and adaptive layout behavior.
9. Product safety state is information, not decoration. Observation-only/mutation-disabled claims must reflect the real service/protocol state and must never be changed only to improve appearance.
10. A successful build is not visual approval. Material UI changes require a real Windows render review at the target window sizes.

## Current visual direction

- Native Windows 11 / Fluent foundation.
- Mica-backed window chrome with a compact custom title area.
- Light-first appearance with dark and High Contrast parity.
- Indigo interaction accent; semantic green/amber/orange/red only when warranted.
- Flat, translucent surfaces with subtle borders and very limited elevation.
- Large numbers for primary evidence, restrained labels, and progressive disclosure for engineering detail.
- Primary flow: system readiness -> quick snapshot or repeated baseline -> evidence -> export.

## Main workspace hierarchy

The current observation page intentionally prioritizes:

1. user goal and measurement actions,
2. service/read-only readiness,
3. primary DPC/ISR evidence,
4. attribution and CPU concentration,
5. repeated-baseline quality,
6. machine context and implementation detail.

Phase names, internal gate terminology, and mutation implementation details belong in engineering/status documentation unless they materially affect a user decision.

## Change checklist

Before adding a new literal visual value, ask:

- Is this a semantic token already?
- Will another screen need the same value?
- Does it require Light/Dark/High Contrast variants?
- Is it a reusable component recipe rather than a screen-specific layout choice?

If any answer points to reuse, update the SSOT first and consume the resource from the screen.
