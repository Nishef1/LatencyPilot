# Premium Dashboard Redesign

## Goal

Rebuild LatencyPilot's current single-scroll engineering dashboard into a calm, premium, native Windows 11 application shell that a non-expert can understand immediately. The UI must preserve the exact evidence and safety semantics already implemented while reducing cognitive load, improving navigation, standardizing iconography, and making the important distinction between **measurement quality** and **optimizer readiness** obvious.

This is a visual/information-architecture rebuild, not a measurement or optimizer redesign.

## Non-negotiable constraints

- WinUI 3 on Windows App SDK 2.4.0; no additional UI/chart framework.
- Window-level `DesktopAcrylicBackdrop` is the primary material. No custom blur/composition implementation.
- The app must remain usable when Windows disables transparency; semantic surfaces and borders must still produce readable hierarchy.
- Dark and High Contrast remain first-class. High Contrast must use system colors and never depend on translucency.
- Existing measurement semantics remain authoritative; no invented score, fabricated timeline, synthetic health grade, or fake status.
- Protocol remains `LatencyPilot.Observation.v6`; public commands remain `GetStatus` and `CaptureKernelLatency` only.
- `ServiceBoundary.MutationAvailable` remains `false` until the existing physical arming gates say otherwise.
- Existing keyboard accelerators, evidence export, readiness gating, service boundary, baseline logic, physical-validation contracts, and schema semantics remain functional.
- Existing native chart controls remain the evidence visualization layer unless a real bug requires modification.
- Shared visual values belong in `DesignTokens.xaml` / `ComponentStyles.xaml`; screen-local color literals and duplicated component recipes are prohibited.
- CI remains the build/test gate. Do not add a second UI framework, UI test framework, or snapshot infrastructure just for this redesign.

## Product direction

LatencyPilot should feel closer to a first-party Windows performance utility than a gaming tweak dashboard:

- restrained Acrylic rather than gradients everywhere,
- one clear primary action at a time,
- strong spacing and typography instead of nested cards,
- Fluent iconography with consistent optical size,
- concise status language,
- charts only where a visual pattern helps a decision,
- exact technical evidence available without dominating the first screen.

The user should be able to answer these questions in a few seconds:

1. Is measurement available?
2. Is my latest baseline trustworthy?
3. Is the system ready for an optimization experiment?
4. What is dominating observed latency/interrupt work?
5. What should I do next?

## Shell architecture

### MainWindow

`MainWindow` becomes a shell instead of the entire product screen.

It owns:

- window-level `DesktopAcrylicBackdrop`,
- custom title bar / product identity,
- top-level service/safety status,
- native `NavigationView`,
- page host,
- appearance preference,
- routing only.

It must not own large evidence lists or page layout details.

### Navigation

Use a real WinUI `NavigationView` rather than a custom button rail that scrolls a single giant canvas.

Top-level destinations are deliberately reduced to four:

1. **Overview** — current health, last baseline readiness, top evidence and next action.
2. **Measure** — quick snapshot and repeated-baseline workflow, baseline preparation, progress and measurement guidance.
3. **Devices** — display/GPU, network/RSS and USB/xHCI evidence.
4. **Evidence** — exact counts, baseline windows, attribution, export/provenance.

`Baseline` is not a separate top-level destination because it is a measurement workflow, not a peer product area.

Appearance belongs in the NavigationView footer/settings affordance, not as another primary destination.

Navigation selection must correspond to the visible page. Manual scrolling must never leave a stale selected nav item because the new architecture does not use nav-as-scroll-position.

## Window material and surfaces

### Acrylic

Use:

```xaml
<Window.SystemBackdrop>
    <DesktopAcrylicBackdrop />
</Window.SystemBackdrop>
```

The root visual should not paint an opaque/gradient full-window background over Acrylic.

Acrylic remains environmental material, not decoration. Content surfaces use semantic translucent/opaque brushes from the design system for readability. Do not stack multiple acrylic effects inside every card.

### Surface hierarchy

Three levels only:

- shell/backdrop,
- page surface/group,
- metric/action tile where grouping materially helps.

Avoid card-inside-card patterns. Use dividers and whitespace before adding another rounded rectangle.

## Iconography

Use WinUI/Windows icon controls consistently:

- `SymbolIcon` where the standard `Symbol` enum covers the meaning,
- `FontIcon` with `SymbolThemeFontFamily` for Fluent glyphs not exposed by `Symbol`,
- existing custom waveform geometry only for product identity / latency-specific branding where no system icon communicates the concept.

Do not mix arbitrary path art with Fluent icons for routine actions.

Optical sizing:

- navigation: 16–18 px,
- compact action: 14–16 px,
- section/status icon: 18–20 px,
- large metric/hero icon: 20–24 px.

Icons never replace required text for ambiguous or safety-relevant actions.

## Page designs

### Overview

The Overview is the primary consumer page and should fit the core decision flow in one desktop viewport at the default window size.

#### Header

- `Latency health`
- one-line explanatory subtitle
- primary action chosen from current state:
  - `Quick snapshot` when no useful evidence exists,
  - `Build baseline` when preparation/readiness makes that the next meaningful action.
- secondary action for the other capture mode.
- export moves to an overflow/secondary action when evidence exists.

#### Status summary

Do not compress unrelated truths into one amber headline.

Represent separately:

- `Baseline valid` / `Baseline inconclusive`
- `Optimizer ready` / `Optimizer not ready`
- optional `Transient tail observed`

A valid baseline that is not optimization-ready must visually read as a trustworthy measurement with a blocked next step, not as a failed baseline.

Use short status rows/badges plus one concise explanation. Detailed reasons belong in Measure/Evidence.

#### Key metrics

Keep only decision-useful metrics:

- DPC p99,
- ISR p99,
- busiest-CPU interrupt concentration,
- dominant module / baseline-readiness summary.

p99.9/max remain available in expanded evidence rather than becoming equal-weight headline metrics.

#### Visual evidence

Reuse the existing real-data charts:

- latency profile / five-window stability,
- CPU interrupt distribution,
- top modules by kernel time,
- CPU DPC/ISR map.

Overview may show the two most useful charts prominently and the other two in a secondary row, but all data remains real and evidence-backed.

#### System context

Replace the large persistent `This PC` rail with a compact system context strip/card:

- Windows build,
- physical/logical CPU summary,
- primary GPU and driver,
- observation service state.

Architecture/package/group details move to Evidence/Devices unless they affect a decision.

### Measure

This page owns both measurement workflows.

#### Quick snapshot

- single clear CTA,
- five-second duration shown as supporting text,
- recent snapshot summary,
- concise quality/integrity result.

#### Baseline

- scenario selector,
- preparation checklist/gate,
- five-window progress,
- comparison-quality result,
- workload-stability result,
- optimizer-readiness result,
- specific retry guidance when workload changed.

No engineering gate names in the primary copy unless they materially help troubleshooting.

### Devices

Group by device domain rather than one long technical dump:

- Graphics
- Network
- USB / xHCI

Each domain gets:

- Fluent icon,
- detected device/driver identity,
- key evidence/status,
- disclosure for technical details.

Do not imply tunability or mutation availability just because a device is detected.

### Evidence

This is the technical page and can be denser, but still avoids nested scrolling.

Sections:

- latest capture integrity,
- exact DPC/ISR counts and tails,
- attribution coverage,
- top modules,
- CPU concentration,
- repeated baseline windows,
- measurement context,
- export/provenance.

Lists showing only a bounded top-N must size to content rather than creating independent scroll bars inside the main page scroll.

## Status and feedback model

Use native non-modal patterns.

- Persistent normal state: compact status in shell/header.
- Actionable warning/error: `InfoBar` on the relevant page.
- Long operation: inline progress with disabled duplicate action.
- No toast-like fake success messages for ordinary local capture completion.

Status colors retain existing semantics:

- accent = action/selection,
- success = verified/healthy,
- warning = caution/inconclusive/not-ready,
- impact = elevated concern,
- danger = failed/severe signal.

Text/labels always accompany status color.

## State and code boundaries

Do not move ETW/service behavior into page controls.

Introduce the smallest shared UI state needed to keep pages synchronized with the existing capture pipeline. The preferred boundary is a focused `DashboardSessionState` owned by the window/application layer that stores the latest already-computed display/evidence state and notifies pages. It must not duplicate analyzer/business rules.

Measurement/business logic remains in existing measurement/evidence services and analyzers.

Page code-behind may orchestrate presentation and route commands, but page controls must not query the privileged service independently when the existing window/session layer already owns that workflow.

## Design system update

`DesignTokens.xaml` remains the visual SSOT, but the current full-window gradient direction is retired for the shell.

Add/revise semantic resources for:

- Acrylic-compatible shell/page surfaces,
- navigation selection,
- compact status backgrounds,
- hero/metric spacing,
- page max width,
- standard icon sizes,
- page section spacing.

`ComponentStyles.xaml` owns reusable recipes for:

- page header,
- status badge/row,
- metric tile,
- section surface,
- primary/secondary/quiet buttons,
- icon tile,
- compact evidence row.

## Responsive behavior

Use native `NavigationView` adaptive pane behavior instead of manually shrinking a custom rail.

Desktop targets:

- wide: expanded left pane, 2-column evidence layouts where useful,
- medium: compact left pane, responsive 1–2 column cards,
- narrow: overlay/compact navigation and stacked content.

No visible child should remain inside a zero-width layout column.

## Accessibility

- Preserve keyboard access and existing accelerators.
- Every icon-only control has `AutomationProperties.Name` and tooltip where appropriate.
- Pages expose meaningful headings and automation labels.
- No status relies only on color.
- Text scaling must not clip metric labels or action text.
- High Contrast uses system colors and readable borders; Acrylic is never required for content legibility.
- Reduced/disabled Windows transparency must still leave a coherent surface hierarchy.

## What does not change

This redesign must not change:

- protocol version,
- evidence schema semantics,
- baseline-quality thresholds,
- workload-stability thresholds,
- optimizer readiness rules,
- mutation availability,
- physical Gate A/B/C/D requirements,
- device mutation domains,
- ETW capture behavior.

If a UI change appears to require changing one of those contracts, stop and resolve that separately rather than hiding it in visual work.

## Verification

Repository verification:

- App remains compiled by the existing critical-tests project graph.
- Existing permanent critical suite remains green; no permanent UI wording tests are added.
- If a stable structural invariant requires a test, keep the durable suite within the owner-authorized ceiling.
- No new UI package is introduced.
- `MainWindow` becomes materially smaller and shell-focused.

Visual verification on real Windows remains mandatory after source/CI completion:

- default 1664×936 desktop viewport,
- approximately 1280×820 compact desktop viewport,
- dark mode,
- High Contrast,
- Windows transparency disabled,
- text scaling above 100%.

Build/test success proves compilation and behavioral contracts; it does not by itself prove visual quality.