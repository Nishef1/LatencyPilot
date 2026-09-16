# Premium Dashboard Redesign

## Goal

Rebuild LatencyPilot's current single-scroll engineering dashboard into a calm, premium, native Windows 11 application shell that a non-expert can understand immediately. Preserve the exact evidence and safety semantics already implemented while reducing cognitive load, improving navigation, standardizing iconography, and making the distinction between **measurement quality** and **optimizer readiness** obvious.

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
- Preserve the existing working `MainWindow` measurement orchestration. Do not create a parallel state/MVVM layer merely to support the visual rebuild.

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

`MainWindow.xaml` becomes a real shell visually, while the existing partial `MainWindow` C# files remain the orchestration owner for capture and evidence state.

The shell owns:

- window-level `DesktopAcrylicBackdrop`,
- custom title bar / product identity,
- top-level service/safety status,
- native `NavigationView`,
- four independent view hosts,
- appearance preference,
- routing.

This tranche deliberately does **not** introduce a new page-level state layer. Existing named controls are re-parented into the new view hosts so existing measurement code remains authoritative. Future extraction into separate Page classes is allowed only if a real maintenance need appears; it is not required for this redesign.

### Navigation

Use a real WinUI `NavigationView` rather than a custom button rail that scrolls one giant canvas.

Top-level destinations are deliberately reduced to four:

1. **Overview** — current health, last baseline readiness, top evidence and next action.
2. **Measure** — quick snapshot and repeated-baseline workflow, baseline preparation, progress and measurement guidance.
3. **Devices** — display/GPU, network/RSS and USB/xHCI evidence.
4. **Evidence** — exact counts, baseline windows, attribution, export/provenance.

`Baseline` is not a separate top-level destination because it is a measurement workflow, not a peer product area.

Appearance belongs in `NavigationView.PaneFooter`, not as a primary destination.

Navigation selection directly controls which view host is visible. The old scroll-position navigation model is removed, so stale selected navigation state cannot occur.

## Window material and surfaces

### Acrylic

Use the Windows App SDK system backdrop:

```xaml
<Window.SystemBackdrop>
    <DesktopAcrylicBackdrop />
</Window.SystemBackdrop>
```

The root visual must not paint an opaque/gradient full-window background over Acrylic.

Acrylic is environmental material, not decoration. Content surfaces use semantic translucent/opaque brushes from the design system for readability. Do not stack multiple acrylic effects inside every card.

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

The Overview is the primary consumer view and should fit the core decision flow in one desktop viewport at the default window size.

Header:

- `Latency health`,
- one-line explanatory subtitle,
- **Quick snapshot** as the only primary action on Overview.

`Build baseline` belongs to **Measure**. `Export JSON` belongs to **Evidence**. Development-only Gate A validation belongs to **Measure** and must never compete with the normal-user Overview action hierarchy.

Status summary must represent separately:

- `Baseline valid` / `Baseline inconclusive`,
- `Optimizer ready` / `Optimizer not ready`,
- optional `Transient tail observed`.

A valid baseline that is not optimization-ready must read as a trustworthy measurement with a blocked next step, not as a failed baseline.

Key metrics remain decision-useful only:

- DPC p99,
- ISR p99,
- busiest-CPU interrupt concentration,
- baseline/readiness summary.

Reuse the existing real-data charts:

- latency profile / five-window stability,
- CPU interrupt distribution,
- top modules by kernel time,
- CPU DPC/ISR map.

The no-evidence state must not render four large empty charts. Before the first usable capture or baseline, Overview shows one compact onboarding/empty-state surface with the next action. The metric/chart dashboard appears only once evidence exists.

Replace the large persistent `This PC` rail with a compact system context surface showing Windows build, CPU summary, primary GPU/driver, and observation mode. Decorative Windows wallpaper artwork is not required; prefer a Fluent system/device icon and real machine facts.

### Measure

Own both measurement workflows.

Quick snapshot:

- single clear CTA,
- five-second duration as supporting copy,
- recent snapshot summary,
- concise quality/integrity result.

Baseline:

- scenario selector,
- preparation gate,
- five-window progress,
- comparison-quality result,
- workload-stability result,
- optimizer-readiness result,
- specific retry guidance when workload changed.

Development-only physical Gate A controls may appear here when the existing repository-development precondition is satisfied. They use secondary/quiet visual priority and do not appear in packaged end-user builds.

No internal gate terminology in primary copy unless it materially helps troubleshooting.

### Devices

Group by device domain:

- Graphics,
- Network,
- USB / xHCI.

Each domain gets a Fluent icon, detected identity, key evidence/status, and progressive disclosure for technical details. Detection must not imply mutation availability.

### Evidence

Technical view with:

- latest capture integrity,
- exact DPC/ISR counts and tails,
- attribution coverage,
- top modules,
- CPU concentration,
- repeated baseline windows,
- measurement context,
- export/provenance.

Bounded top-N lists must size to their content instead of creating nested scroll bars.

## Status and feedback model

Use native non-modal patterns.

- Healthy connected service state does **not** consume a full-width banner on every page. It is represented compactly in shell state.
- Service checking/degraded/disconnected states may surface a compact inline status row or `InfoBar` with Refresh.
- Actionable warning/error: `InfoBar` on the relevant view.
- Long operation: inline progress with duplicate actions disabled.
- No fake toast-like success message for ordinary local capture completion.

Status colors retain existing semantics:

- accent = action/selection,
- success = verified/healthy,
- warning = caution/inconclusive/not-ready,
- impact = elevated concern,
- danger = failed/severe signal.

Text/labels always accompany status color.

## Native Windows visual language

The final visual pass follows Windows theme semantics rather than a fixed branded light palette:

- Default appearance follows the Windows setting (`ElementTheme.Default`) unless the user explicitly saved Light or Dark.
- The active accent comes from the Windows system accent resources for buttons, selection, and compact emphasis.
- Primary/secondary text, card fills, strokes and subtle fills should map to WinUI theme resources such as `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `CardBackgroundFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`, and system accent resources.
- DPC/ISR chart colors may remain stable analytical colors so comparisons are consistent across Windows accent choices.
- Metric icon containers are compact rounded tiles rather than oversized circles.
- Corners, card padding and title-bar height follow the denser Windows 11 utility aesthetic; premium means calm precision rather than oversized web-dashboard chrome.
- The runtime must not replace the declared Acrylic backdrop with Mica.

## Code boundaries

Do not move ETW/service behavior into view controls.

Existing partial `MainWindow` files remain the source of truth for capture, readiness and rendering behavior. The redesign changes visual ownership and routing only. `DashboardShell.cs` becomes the shell/navigation/layout coordinator. `DashboardVisuals.cs`, `PremiumObservationUi.cs`, measurement experience files, device evidence code, and evidence export continue to compute/update the same named UI outputs unless a visual simplification removes a redundant output.

Do not duplicate analyzer or readiness rules in visual code.

## Design system update

`DesignTokens.xaml` remains the visual SSOT; the current full-window gradient direction is retired from the active shell.

Add/revise semantic resources for:

- Acrylic-compatible shell/page surfaces,
- native system accent and Windows theme text/card brushes,
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

Use native `NavigationView` adaptive pane behavior rather than manually shrinking a custom rail.

Desktop targets:

- wide: expanded left pane, 2-column evidence layouts where useful,
- medium: compact left pane, responsive 1–2 column cards,
- narrow: overlay/compact navigation and stacked content.

No visible child remains inside a zero-width layout column.

## Accessibility

- Preserve keyboard access and existing accelerators.
- Every icon-only control has `AutomationProperties.Name` and a tooltip where appropriate.
- Views expose meaningful headings and automation labels.
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

If a UI change appears to require changing one of those contracts, stop and resolve it separately rather than hiding it in visual work.

## Verification

Repository verification:

- App remains compiled by the existing critical-tests project graph.
- Existing permanent critical suite remains green; no permanent UI wording tests are added.
- If a stable structural invariant warrants a test, keep the durable suite within the owner-authorized ceiling.
- No new UI package is introduced.
- Old custom navigation/scroll-position routing is removed rather than left as dead code.

Visual verification on real Windows remains mandatory after source/CI completion:

- default 1664×936 desktop viewport,
- approximately 1280×820 compact desktop viewport,
- dark mode,
- High Contrast,
- Windows transparency disabled,
- text scaling above 100%.

Build/test success proves compilation and behavioral contracts; it does not by itself prove visual quality.