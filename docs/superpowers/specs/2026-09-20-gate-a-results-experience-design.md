# Gate A Results Experience Design

Date: 2026-09-20
Status: Approved by delegated product/design direction

## Intent

After GPU Gate A finishes, LatencyPilot should return the user to the main window and explain the result inside the product instead of making the raw JSON report the primary experience. A user should be able to understand what changed, which candidate performed best, why LatencyPilot kept or restored a state, how noisy/repeatable the evidence was, and whether final runtime placement was verified.

The same completed Gate A session directory must also be exported as a ZIP next to the directory so the owner can attach the complete evidence bundle to another conversation without manually compressing it.

## Product constraints

- The authoritative decision remains `GpuAutoAffinityReport`; the UI must not invent a second score, re-rank candidates, or reinterpret a rejected candidate as a winner.
- Keep/Restore semantics, provenance, closure eligibility, stored-state verification and ETW placement evidence remain owned by the existing benchmark/session code.
- Do not add a charting package. Reuse the existing WinUI chart primitives, design tokens, component styles and accessibility patterns.
- Keep the result inside Overview rather than creating another NavigationView destination.
- Preserve both the uncompressed session folder and a ZIP sibling.
- ZIP creation is evidence packaging only. Failure to package must not change the Gate A decision, rollback ownership or final-state verification.
- Development-only evidence must remain visibly distinct from closure-eligible evidence.
- Light, dark and high-contrast modes must remain readable without relying on color alone.

## Completion flow

1. The elevated helper finishes and the normal-user benchmark process exits.
2. The app reads `gpu-auto-affinity-report.json` and validates schema/session identity and source-state eligibility exactly as it does today.
3. The app packages the completed session directory to `<session-directory>.zip` through a temporary ZIP in the same parent directory, then atomically replaces any stale sibling ZIP.
4. Packaging errors are logged and surfaced as `Evidence bundle could not be packaged`; the Gate A result still renders.
5. The progress window receives its final outcome and can remain available as secondary history, but the main window restores and activates.
6. Overview renders a new `GPU optimization result` region using the validated report and the optional ZIP path.
7. Raw JSON is no longer opened automatically. Explicit actions are provided for `Open ZIP`, `Open session folder`, and `Open raw report`.

## Information hierarchy

### 1. Decision hero

The first card answers the human question before showing charts.

Keep example:

> CPU 7 kept
>
> The improvement survived repeat testing, cleared measured uncertainty, stayed inside performance guardrails, and final ETW evidence confirmed GPU interrupts on CPU 7.

Restore example:

> Original kept
>
> CPU 11 produced the strongest measured candidate result, but the gain did not clear the measured uncertainty and verification gates. LatencyPilot kept the exact original policy instead of turning benchmark noise into a system change.

The hero includes:

- final state badge: `CPU n kept`, `Original kept`, `Stopped safely`, or `Result needs attention`;
- evidence badge: `Closure eligible` or `Development evidence`;
- one concise explanatory paragraph derived only from `FinalRecommendation`, `FinalProcessor`, `FinalStateVerified`, `OriginalStateRestored`, `Reasons`, and final placement evidence;
- source revision and elapsed duration as quiet metadata.

### 2. Before / result metric strip

Four compact metric cards:

- 1% low FPS — primary metric;
- Average FPS;
- frame p99 — lower is better;
- 0.1% low FPS — tail context.

For a kept CPU, compare the accepted Original reference aggregate with the final kept candidate aggregate.

For RestoreOriginal, compare Original with the strongest rankable candidate only as diagnostic context and label it `Best tested · not kept`. The UI must not imply that candidate was safe to apply.

Each card shows Original, compared candidate, signed relative delta, and a text state such as `Improved`, `Within measured uncertainty`, `Guardrail regression`, or `Not enough comparable evidence`. Color may reinforce but never replace the text state.

### 3. Candidate comparison chart

A native WinUI horizontal comparison chart shows every candidate that has decision metrics.

- Y axis: logical CPU identity, grouped only visually by physical core metadata when useful.
- X axis: decision 1% low FPS.
- Original reference is a vertical rule or dedicated reference lane, not a fake CPU bar.
- Final kept CPU gets the strongest accent and a `Kept` label.
- Top tested but restored candidate gets an outlined `Not kept` treatment.
- Inconclusive candidates remain visible with muted treatment when a finite decision metric exists.
- Tooltips expose 1% low, AVG, p99, 0.1% low, trial count, verdict and local-control uncertainty.
- If more processors exist than comfortably fit, the chart grows vertically inside the Overview scroll surface rather than shrinking labels to unreadable sizes.

### 4. Repeatability / trial history

A second native chart plots scored trial 1% low FPS in run order.

- Original scored trials and compared-candidate scored trials are separate series.
- Warm-ups are excluded from the plot but remain available in raw evidence.
- Retry/rejected/contaminated observations remain visible when represented in the report; they use a distinct marker/text treatment instead of being silently deleted.
- The chart subtitle states the count of scored Original and compared-candidate observations.
- Accessibility help text summarizes the run range and whether the result was repeatable enough for Keep.

### 5. Why this decision

A compact evidence checklist explains the decision using report facts:

- Primary improvement vs measured uncertainty.
- Repeatability / local control uncertainty.
- AVG and frame-p99 guardrails.
- Runtime ISR placement proof.
- Final stored-state / rollback verification.

Each row has `Passed`, `Blocked`, `Restored`, `Unavailable`, or `Diagnostic only` text plus a short sentence. No new decision logic is introduced; when the report does not provide enough structured data, the UI uses the existing `Reasons` text and labels the row conservatively.

### 6. Evidence actions

Actions are secondary to the explanation:

- `Open ZIP` — enabled only when packaging succeeded.
- `Copy ZIP path` — enabled only when packaging succeeded.
- `Open session folder`.
- `Open raw report`.

The raw JSON should never be opened automatically after a successful run.

## Data derivation

The renderer consumes only `GpuAutoAffinityReport` plus the session/report/ZIP paths.

### Original aggregate

Use scored `Trials` whose role is Original/reference and whose readiness is decision-grade. Prefer the same trial phases that feed the session decision (`screening-original` and accepted Original controls where appropriate). Aggregate with medians, matching the benchmark/session methodology. Do not mix warm-ups.

### Candidate aggregate

Use `GpuAutoAffinityCandidateReport.DecisionOnePercentLowFps`, `DecisionAvgFps`, `DecisionFrameP99Milliseconds`, and `DecisionLow01PctFps` for the candidate comparison surface because these are already the decision aggregates. Per-run history comes from `Trials`.

### Compared candidate when Original is restored

Choose the highest-ranked report candidate that has finite decision metrics and a rankable verdict, preserving report order. Label it diagnostic and not kept. This is a presentation choice, not a new Keep decision.

## Visual direction

The result region should feel like an evidence console rather than a benchmark leaderboard.

- Reuse Mica/glass surfaces, existing corner radii and typography.
- Use one confident accent for the final kept state, neutral surfaces for ordinary candidates, warning semantics for uncertainty, and failure semantics only for actual verification/export failure.
- Do not use gradients as data encoding; existing decorative gradient resources may remain for small brand/icon accents.
- Prefer whitespace and alignment over additional borders.
- Use compact metric cards and two larger evidence cards; avoid a wall of mini-panels.
- Keep titles literal: `GPU optimization result`, `Candidate comparison`, `Repeatability`, `Why this decision`.
- Copy should explain causality in plain English and avoid claims stronger than the report evidence.

## Responsive behavior

- Wide: four metric cards in one row; candidate chart and repeatability chart can share a two-column row only when each remains at least ~420 px wide.
- Medium: metrics wrap 2×2; charts stack.
- Narrow: metrics become a single column or compact 2-column layout; all actions wrap without horizontal scrolling.
- Candidate chart height is data-driven with a practical minimum row height so CPU labels remain readable.

## Accessibility

- Every chart receives `AutomationProperties.Name` and a complete `HelpText` summary.
- Every metric delta has textual direction; no red/green-only meaning.
- High Contrast uses system/theme resources and removes decorative soft fills where current design tokens already do so.
- Keyboard users can reach all evidence actions in reading order.

## ZIP packaging contract

- Source: the completed Gate A `sessionDirectory`.
- Destination: `sessionDirectory + ".zip"`.
- Create into a unique temporary sibling file first.
- Include the directory contents under one top-level folder named after the session directory so extracted bundles do not spill files into the destination root.
- Do not delete or mutate the source directory.
- Overwrite a stale ZIP only after a new ZIP has been created successfully.
- Packaging must run only after the benchmark/helper lifecycle is complete and the report has passed schema/session validation.
- Packaging failure is non-fatal to the optimization result but visible in the UI and logs.

## Testing and verification

Reuse the existing permanent test budget; do not add a new test project.

- Extend existing Gate A/source contract coverage for ZIP path/name and non-fatal packaging behavior using a temporary directory.
- Add pure presentation-model tests for Keep and Restore result derivation in an existing critical-test file rather than testing XAML wording or visual tree shape.
- Build the WinUI app and run `LatencyPilot.CriticalTests`.
- Inspect the rendered result on real Windows in light, dark, narrow and high-contrast states before calling visual quality complete.
- Physical Gate A remains required to validate the final experience with a real report and real ZIP evidence; source/CI success alone does not close physical Gate A.
