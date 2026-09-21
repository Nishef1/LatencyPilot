# Gate A Results Experience Design

Date: 2026-09-20  
Status: **Approved and implemented in source; paired-v2 semantics reconciled 2026-09-21**

This document owns the product-information hierarchy for the Gate A result experience. GPU measurement/search/ranking semantics are owned by ADR 0007 and current source. If an older example in this design conflicts with ADR 0007, ADR 0007 wins.

## Intent

After GPU Gate A finishes, LatencyPilot returns the user to the main window and explains the outcome inside the product instead of making raw JSON the primary experience.

The first questions the result surface must answer are:

1. What machine state is active now?
2. Was a CPU actually kept, was Original restored, or was the result a practical tie/inconclusive diagnostic?
3. What paired evidence drove the authority decision?
4. How stable were the local controls?
5. Was final runtime ISR placement verified?
6. Where is the complete evidence bundle?

The completed session directory is also exported as a ZIP sibling when packaging succeeds.

## Product constraints

- `GpuAutoAffinityReport` remains the authoritative decision. Presentation must not invent a second score or re-rank candidates.
- Paired-v2 presentation consumes persisted `DecisionRank`, finalist aggregates/decision floors and direct pair evidence.
- Raw execution order is not decision authority.
- Keep/Restore/practical-tie semantics, provenance, source eligibility, rollback ownership and final ETW placement remain owned by the benchmark/session/report code.
- Do not add a charting package. Reuse native WinUI primitives and existing design resources.
- Keep the result inside Overview rather than creating a separate navigation destination.
- Preserve both the uncompressed session folder and ZIP sibling.
- Packaging failure must never alter the benchmark decision or recovery ownership.
- Source/evidence eligibility must not be presented as physical Gate A closure.
- Color may reinforce state but never carry state alone.

## Completion flow

1. The elevated helper finishes and benchmark lifecycle reaches a terminal state.
2. The app reads `gpu-auto-affinity-report.json` and validates schema/session identity and source-state eligibility.
3. The app packages the completed session directory into a sibling ZIP through the existing safe temporary-file path.
4. Packaging failure is surfaced but does not alter the Gate A decision.
5. The main window restores/activates and Overview renders `GateAResultPresentation`.
6. Raw JSON is available only through explicit evidence actions.

## Information hierarchy

### 1. Decision hero

The hero states the terminal truth before charts.

Supported headline families include:

- `Winner · CPU n kept`
- `Practical tie · CPU n kept` when a tied improvement-capable target was selected operationally
- `No measured winner · Original restored`
- `Original kept`
- `Custom diagnostic result`
- stopped/failure-recovery states when applicable

The evidence badge uses **Evidence eligible** or **Development evidence**. It must never say `Closure eligible` because the stored compatibility field does not mean physical Gate A has already closed.

The hero summary is derived from report authority only and must not turn a positive diagnostic pair into a Keep claim.

### 2. Primary paired evidence

For the compared/kept candidate, show the direct local relationship whenever the report contains the required pair evidence:

```text
Original before → Candidate → Original after
```

Expose:

- 1% low primary values/effect;
- AVG paired effect;
- frame-p99 paired effect, lower-is-better but stored/displayed with improvement-positive sign convention;
- diagnostic 0.1%-low effect where available;
- control movement;
- drift budget;
- pair attempt/verdict;
- finalist decision floor when finalist authority exists.

Do not manufacture pseudo-normalized FPS. Raw values are observations; paired effect is a derived decision measure.

### 3. Candidate comparison chart

The candidate chart displays authority-ranked candidates in persisted rank order.

- X axis: paired 1%-low effect around a 0% Original reference.
- CPU labels include persisted decision rank where available.
- Kept candidate receives the strongest terminal-success treatment.
- Best measured but restored candidate is outlined/diagnostic and explicitly `Not kept`.
- Inconclusive candidates may remain visible when finite diagnostic evidence exists.
- Tooltips expose rank, processor/core, state, paired effect, local uncertainty/control movement, raw candidate context, trial count and verdict where available.

The chart must never infer ranking from raw candidate FPS.

### 4. Pair/finalist evidence history

The result surface should make the decision reconstructable without forcing the user into raw JSON.

Prefer compact evidence rows/cards for the relevant local pairs and finalist authority:

- pair number/stage/attempt;
- Original-before capture/value;
- Candidate capture/value;
- Original-after capture/value;
- effect;
- control movement versus drift budget;
- Valid / Inconclusive state;
- finalist median effect and decision floor when applicable.

Long execution history may remain secondary. Warm-ups remain evidence but are not decision observations.

### 5. Why this decision

Rows explain the authoritative decision without creating new logic:

- Primary paired improvement.
- Local-control stability / decision floor.
- AVG and frame-p99 guardrails.
- Supported GPU-driver interrupt-tail guardrails where enough evidence exists.
- Runtime ISR placement proof.
- Final stored-state / rollback verification.

Use states such as `Passed`, `Blocked`, `Restored`, `Unavailable`, `Practical tie`, or `Diagnostic only`.

For a RestoreOriginal outcome, an empty regression list is not enough to claim a final guardrail pass. Non-Keep evidence stays diagnostic.

### 6. Evidence actions

Secondary actions:

- `Open ZIP` — when packaging succeeded;
- `Copy ZIP path` — when packaging succeeded;
- `Open session folder`;
- `Open raw report`.

Raw JSON must not open automatically after a successful run.

## Compared candidate selection

Presentation follows report authority:

1. if a candidate was actually kept, compare/render that candidate;
2. otherwise choose the strongest persisted authority candidate for diagnostic context, preferring finalist authority when it exists for the same processor;
3. never reconstruct a new winner from metric decimals or execution order.

For non-Keep outcomes the label must clearly include comparison/diagnostic semantics such as `best measured · comparison only · not kept`.

## Practical-tie presentation

When report authority says `PracticalTie=true`, the result must say that the finalists were practically equivalent under the method's one-percentage-point margin. If one target was kept through passive deterministic selection, the UI may explain that it was the operational target but must not claim it proved faster than its tied peers.

## Diagnostic scopes

### Selected CPUs / Custom

Show the best measured CPU inside the selected subset, direct pair evidence and why Original was restored. Never present the subset result as a machine-wide winner or closure evidence.

### Original only

Show Original repeatability/variability only. The UI must explicitly state that no system changes were made and candidate benefit/restart stability remain untested.

## Visual direction

The result should feel like an evidence console, not a benchmark leaderboard.

- Strong hierarchy: terminal truth first, evidence second, detail third.
- Use one confident accent for a verified kept state.
- Use neutral/outlined treatment for a best-measured-but-not-kept candidate.
- Warning semantics communicate uncertainty/inconclusive evidence; failure semantics are reserved for actual verification/recovery/export failure.
- Prefer whitespace/alignment over extra borders and decorative panels.
- Avoid gradients as data encoding.
- Keep copy literal and evidence-bounded.

## Responsive behavior

- Wide: summary + compact metric/evidence region may use columns only when content remains readable.
- Medium: wrap compact cards and stack larger evidence surfaces.
- Narrow: stack content; no horizontal scrolling for core actions/evidence.
- Candidate chart height is data-driven with readable row labels.
- Long explanations wrap rather than truncating the decision reason.

## Accessibility

- Charts and evidence surfaces expose meaningful `AutomationProperties.Name`/help summaries.
- Signed effects also have textual direction/state.
- High Contrast relies on system/theme resources and text/outline state rather than color-only semantics.
- Keyboard users can reach Stop/evidence actions in reading order.
- Real Windows inspection remains required for clipping, focus order, text scaling and Narrator/UIA meaning.

## ZIP packaging contract

- Source: completed Gate A session directory.
- Destination: `sessionDirectory + ".zip"`.
- Create into a unique temporary sibling first.
- Include evidence under one top-level session folder.
- Do not delete or mutate the authoritative source directory.
- Replace a stale ZIP only after a new ZIP is created successfully.
- Packaging runs only after benchmark/helper lifecycle completion and report identity validation.
- Packaging failure is visible but non-fatal to the optimization decision.

## Verification boundary

Reuse the existing permanent critical-test budget. Contract tests may assert stable authority/data-flow invariants, but they do not prove visual quality.

Before calling this result experience physically complete, inspect it on real Windows with a real paired-v2 report in Light, Dark, High Contrast, increased text scale, narrow/wide layouts, keyboard-only navigation and UI Automation/Narrator states.

Physical Gate A remains separate from source/CI success.