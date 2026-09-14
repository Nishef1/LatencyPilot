# Premium Dashboard Redesign

## Goal

Rebuild the current LatencyPilot main screen to match the approved premium visual direction: compact native Windows 11 shell, strong hierarchy, minimal prose, meaningful charts, and evidence-first interaction. The result must feel like a shippable first-party performance utility rather than an engineering dashboard.

## Non-negotiable constraints

- WinUI 3 / Windows App SDK 2.4 Stable; no additional UI/chart framework.
- Light-first, native Mica, with Dark and High Contrast preserved.
- Existing measurement semantics remain authoritative; no invented metrics or fabricated time-series.
- Protocol remains `LatencyPilot.Observation.v6`; public commands stay `GetStatus` and `CaptureKernelLatency` only.
- `ServiceBoundary.MutationAvailable` remains `false`.
- Existing keyboard accelerators, evidence export, readiness gating, service boundary, and baseline logic remain functional.
- Design values must come from the existing SSOT: `DesignTokens.xaml`, `ComponentStyles.xaml`, and this design-system contract. Shared styling must not be re-hardcoded in the page.
- Permanent automated test count remains at 9 unless a genuinely new durable invariant warrants the final slot. Red/green UI checks may be temporary and must be removed before final state.

## Truthful visualization contract

The approved concept image contains a continuous 60-second latency line and a time heatmap. The current protocol does not return raw event timestamps or time buckets, so shipping those visuals literally would fabricate data. The implementation therefore preserves the same visual hierarchy while mapping every visual to evidence that actually exists:

- **Tail profile chart:** plots DPC and ISR p50 / p95 / p99 / p99.9 / max from the latest real capture.
- **Baseline stability chart:** plots DPC p99 and ISR p99 across the five actual baseline windows when a baseline exists.
- **CPU interrupt distribution:** bars derived from each processor's real DPC+ISR event count.
- **CPU interrupt map:** a heatmap-like matrix derived from real per-processor DPC and ISR shares, not fake time buckets.
- **Top modules by kernel time:** horizontal bars derived from `TotalDurationMicroseconds` for real attributed modules.

When data is unavailable, charts show an intentional empty state rather than placeholder values.

## Information architecture

### App shell

A fixed 184–200 px left navigation rail plus a flexible dashboard canvas. Navigation is section navigation within the single existing screen, not fake multi-page navigation. The rail provides:

- Overview
- Measure
- Baseline
- Devices
- Appearance/status footer

Selecting a section scrolls the dashboard to the corresponding real section. No unimplemented Reports/Settings destinations are shown.

### Header

Compact title bar and dashboard header:

- LatencyPilot identity + version
- `Observation only` safety status
- `Latency health` heading and one-line purpose
- Quick snapshot (primary when baseline is locked/not prepared)
- Build baseline (primary when readiness is satisfied)
- Export JSON (secondary)

No large hero paragraph.

### Context rail

`This PC` becomes concise context, not four anonymous number tiles. It shows:

- Windows edition/build
- logical / physical / SMT CPU counts
- primary present display adapter name and driver version when available
- architecture summary

`Device evidence` becomes a compact read-only card with clear readiness/status and one action.

### Dashboard summary

Four metric cards:

1. DPC p99
2. ISR p99
3. CPU concentration (share handled by busiest observed CPU)
4. Last baseline verdict / status

Cards may include tiny sparklines derived only from available distribution/baseline evidence.

### Visual evidence grid

- Tail profile / baseline stability line chart
- CPU interrupt distribution bar chart
- Top modules horizontal bars
- CPU DPC/ISR intensity map

Charts use the same indigo/blue semantic palette, concise axes/labels, and strong empty states.

### Preparation and snapshot

Baseline preparation is compact and stays visible near the bottom of the overview. The two existing readiness acknowledgements remain the gate. Copy is shortened on-screen; full rationale remains accessible through tooltips/automation help.

Recent snapshot becomes a compact visual card with status and the most relevant evidence summary. The verbose current `Snapshot evidence` prose card is removed from the primary flow.

## SSOT expansion

`DesignTokens.xaml` owns:

- shell/nav dimensions
- chart colors and grid colors
- card/surface/background brushes
- text hierarchy
- shared spacing/radius/dimensions

`ComponentStyles.xaml` owns:

- nav item styles
- dashboard cards / metric cards
- chart card styles
- compact status pills
- action buttons

No screen-specific brush hex values are permitted in `MainWindow.xaml` or chart controls.

## Component boundaries

New focused WinUI controls:

- `Controls/LatencyProfileChart`: DPC/ISR percentile or baseline-window lines.
- `Controls/CpuDistributionChart`: processor share bars.
- `Controls/ModuleContributionChart`: module-duration bars.
- `Controls/CpuInterruptMap`: processor × DPC/ISR intensity matrix.

Controls receive already-computed display models; they do not call ETW, persistence, or service APIs.

`DashboardVisuals.cs` converts `KernelLatencyCaptureResponse` and baseline-window evidence into those display models and updates summary cards. Measurement/business logic stays in existing files.

## Responsive behavior

- >= 1400 px: fixed left rail + 2-column visualization grid.
- 1080–1399 px: compact left rail + responsive 2-column/1-column chart grid.
- < 1080 px: rail collapses to compact top/side mode and chart cards stack; no column is assigned width zero while still holding visible content.

## Accessibility

- High Contrast uses system colors and clear outlines.
- Charts include concise automation names and textual summaries for non-visual access.
- Color is never the sole signal; labels/values remain visible.
- Keyboard accelerators remain unchanged.

## Verification

- Temporary structural UI test verifies SSOT use and presence of real chart controls; remove before final state.
- Release-build `LatencyPilot.App` on Windows CI during implementation.
- Existing critical suite remains green.
- Final CI returns to normal test-only 9/9.
- Owner-local render must be visually reviewed at the real 1280×820 baseline and ultrawide viewport. Build success alone is not visual completion.
