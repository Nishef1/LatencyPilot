# Gate A Results Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render an evidence-first GPU Gate A result inside Overview and automatically create a shareable ZIP of each completed Gate A session.

**Architecture:** Keep `GpuAutoAffinityReport` as the sole decision authority. Add a small app-layer presentation model that converts report facts into UI-ready rows/series, a focused native WinUI candidate comparison control, and a session bundle exporter that creates a ZIP only after validated completion. `GateAValidationExperience` remains the orchestration owner and hands the validated result to Overview.

**Tech Stack:** C# 14 / .NET 10, WinUI 3, Windows App SDK, existing LatencyPilot chart/token system, `System.IO.Compression`, MSTest critical-contract suite.

**Spec:** `docs/superpowers/specs/2026-09-20-gate-a-results-experience-design.md`

## Global Constraints

- `GpuAutoAffinityReport` remains authoritative; UI must not create a new benchmark score or Keep/Restore rule.
- No new charting package or UI dependency.
- Preserve the session directory and create a sibling ZIP only after validated completion.
- ZIP failure must never change mutation/recovery/final-state semantics.
- Reuse existing WinUI design tokens, component styles and accessibility patterns.
- Do not create a new NavigationView destination; results live in Overview.
- Development-only evidence stays visually and textually distinct from closure-eligible evidence.
- Do not add a new permanent test project; extend current critical coverage only where behavior is stable and meaningful.

## Review Focus

- Gate A completes successfully but ZIP creation fails because the destination is locked: the result still renders and optimization state is unchanged.
- `RestoreOriginal` has ranked candidates but no kept CPU: the UI must label the compared candidate `Not kept` and never imply success.
- Report contains missing/null optional metrics: presentation stays conservative and renders `Not enough comparable evidence` instead of throwing.
- 16+ logical CPU candidates: chart labels remain readable and the Overview scroll surface grows vertically rather than compressing bars.
- Development-only report: hero clearly says development evidence and does not show closure-eligible language.

---

### Task 1: Add deterministic session ZIP packaging

**Files:**
- Create: `src/LatencyPilot.App/GateAEvidenceBundleExporter.cs`
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Test: `tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs`

**Interfaces:**
- Produces: `GateAEvidenceBundleExporter.TryCreateAsync(string sessionDirectory, CancellationToken cancellationToken) -> Task<GateAEvidenceBundleExportResult>`
- Produces: `GateAEvidenceBundleExportResult(string? ZipPath, string? Error)` with `Succeeded => ZipPath is not null`.
- Consumes: a completed, validated Gate A session directory.

- [ ] **Step 1: Add focused behavior tests in the existing critical suite**

Test a temporary session directory containing `gpu-auto-affinity-report.json` and a nested `benchmark` file. Assert that successful export creates `<session>.zip`, preserves the source folder, stores files beneath one top-level session directory entry, and returns no error. Add a second test where a deliberately invalid/unavailable destination scenario returns an error without deleting the session directory.

- [ ] **Step 2: Run the critical suite and confirm the new test fails because the exporter does not exist**

Run:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Expected: FAIL in the new exporter contract.

- [ ] **Step 3: Implement `GateAEvidenceBundleExporter`**

Implementation requirements:

```csharp
internal sealed record GateAEvidenceBundleExportResult(string? ZipPath, string? Error)
{
    internal bool Succeeded => !string.IsNullOrWhiteSpace(ZipPath);
}

internal static class GateAEvidenceBundleExporter
{
    internal static Task<GateAEvidenceBundleExportResult> TryCreateAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default);
}
```

Use `ZipArchive` so entries can be prefixed with the session directory name. Write a unique temporary sibling archive, then replace the stale destination only after the temporary archive closes successfully. On `IOException`, `UnauthorizedAccessException`, `InvalidDataException` or `ArgumentException`, delete only the temporary file and return a non-fatal error string. Never delete the source folder.

- [ ] **Step 4: Integrate packaging after report validation**

In `GateAValidationExperience.cs`, call the exporter only after benchmark exit, report existence, deserialization, schema/session validation, and development-source eligibility validation. Store the returned ZIP path/error for result rendering. Remove the automatic `TryRevealReport(reportPath)` success-path behavior; explicit evidence actions will replace it.

- [ ] **Step 5: Re-run critical tests and commit**

Expected: PASS.

Commit message:

```text
feat(app): package Gate A evidence bundles
```

### Task 2: Build a pure Gate A result presentation model

**Files:**
- Create: `src/LatencyPilot.App/GateAResultPresentation.cs`
- Modify: `tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs`

**Interfaces:**
- Produces: `GateAResultPresentation.Create(GpuAutoAffinityReport report, string sessionDirectory, string reportPath, GateAEvidenceBundleExportResult bundle) -> GateAResultViewModel`.
- Produces immutable app-layer records for hero copy, metric comparisons, candidate bars, trial points, decision rows and evidence paths.
- Consumes no WinUI types so the logic is unit-testable.

- [ ] **Step 1: Add presentation-model tests for Keep and Restore**

Create representative `GpuAutoAffinityReport` objects using existing core records. Assert:

```text
KeepCandidate + FinalProcessor -> hero title `CPU N kept`
RestoreOriginal -> hero title `Original kept`
RestoreOriginal compared candidate -> label contains `Not kept`
GateAClosureEligible false -> evidence label `Development evidence`
missing optional metrics -> conservative unavailable state rather than exception
```

Also assert metric direction: higher is better for FPS; lower is better for p99.

- [ ] **Step 2: Run the critical suite and confirm failure**

Run the same single critical test project. Expected: FAIL because presentation types do not exist.

- [ ] **Step 3: Implement presentation derivation**

Use candidate decision aggregates from `GpuAutoAffinityCandidateReport`. Build Original medians from scored, decision-grade Original/reference trials only; exclude warm-ups. Preserve report candidate order when choosing the diagnostic compared candidate after Restore. Use report `Reasons` and structured final-state fields for conservative explanation rows; never infer a Keep decision that the report did not make.

Suggested records:

```csharp
internal sealed record GateAResultViewModel(...);
internal sealed record GateAMetricComparison(...);
internal sealed record GateACandidateBar(...);
internal sealed record GateATrialPoint(...);
internal sealed record GateADecisionEvidenceRow(...);
```

- [ ] **Step 4: Re-run critical tests and commit**

Expected: PASS.

Commit message:

```text
feat(app): derive Gate A result presentation
```

### Task 3: Add a native candidate comparison chart

**Files:**
- Create: `src/LatencyPilot.App/Controls/GpuCandidateComparisonChart.cs`
- Modify: `src/LatencyPilot.App/DashboardChartModels.cs` only if a shared generic chart record materially reduces duplication; otherwise keep Gate A-specific model in its presentation file.
- Test: covered through presentation model and app build; do not add brittle visual-tree assertions.

**Interfaces:**
- Consumes: `IReadOnlyList<GateACandidateBar>` plus Original 1% low reference.
- Produces: a native `UserControl` with `SetData(...)` and `Clear(...)` matching current chart control conventions.

- [ ] **Step 1: Implement the chart using existing theme resources**

Use `Canvas`, `Rectangle`, `Line`, `TextBlock`, existing `DashboardThemeResources`, and existing spacing/font resources. Render one horizontal bar per candidate, a vertical Original reference line, labels for CPU and state, and tooltips with all available decision metrics. Use text/shape treatment in addition to color for `Kept`, `Not kept`, and `Inconclusive` states.

- [ ] **Step 2: Add accessibility summaries**

Set `AutomationProperties.Name` to `GPU candidate comparison chart` and `HelpText` to a generated summary naming Original, the compared/kept CPU, candidate count and decision state.

- [ ] **Step 3: Build the app**

Run:

```powershell
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Release
```

Expected: PASS.

- [ ] **Step 4: Commit**

```text
feat(app): add GPU candidate comparison chart
```

### Task 4: Add the Overview result surface

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Create: `src/LatencyPilot.App/GateAResultExperience.cs`
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Modify: `src/LatencyPilot.App/DashboardShell.cs` only if existing responsive breakpoints need one additional result-surface hook.
- Modify: `src/LatencyPilot.App/Design/PremiumOverviewTokens.xaml` only when an existing token cannot express the layout; prefer current resources.

**Interfaces:**
- Consumes: `GateAResultViewModel` from Task 2.
- Produces: `RenderGateAResult(GateAResultViewModel result)` on `MainWindow` and explicit evidence action handlers.

- [ ] **Step 1: Add the XAML result region to Overview**

Structure:

```text
GPU optimization result
├─ decision hero + eligibility/source metadata + evidence actions
├─ 4 metric cards
├─ Candidate comparison
├─ Repeatability
└─ Why this decision
```

Default visibility is collapsed until a validated result exists. Reuse `ChartCardStyle`, dashboard metric/card styles, semantic brushes, typography and radii. No new navigation item.

- [ ] **Step 2: Implement result rendering in a focused partial class**

`GateAResultExperience.cs` owns text, metric formatting, chart binding and evidence action state. Use the presentation model only; no benchmark re-ranking in this class.

For repeatability, reuse/extend the existing `LatencyProfileChart` only if its two-series semantics and labels fit honestly. Otherwise create one small Gate A trial-history control following the same native pattern rather than distorting an unrelated chart.

- [ ] **Step 3: Add evidence actions**

- `Open ZIP`: `Process.Start` with `UseShellExecute=true` on the ZIP file.
- `Copy ZIP path`: Windows clipboard API; show a short confirmation in the result metadata/status area.
- `Open session folder`: shell-open the directory.
- `Open raw report`: shell-open JSON explicitly.

Disable ZIP actions when packaging failed and show the packaging error as a non-fatal warning.

- [ ] **Step 4: Restore and activate the main window after terminal completion**

After the validated report/presentation is ready, restore `AppWindow` and activate the main window. Keep the progress window secondary. Do not automatically open Explorer or the JSON report on success.

- [ ] **Step 5: Implement responsive layout**

Use existing dashboard responsive infrastructure. Wide: metrics 4-up; medium: 2×2; narrow: one/two columns as space permits. Stack evidence charts when either would fall below roughly 420 px usable width. Candidate chart height grows with candidate count.

- [ ] **Step 6: Build and run critical tests**

Run:

```powershell
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Release
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Expected: both PASS.

- [ ] **Step 7: Commit**

```text
feat(app): surface Gate A results in Overview
```

### Task 5: Visual and workflow verification

**Files:**
- Modify only files implicated by concrete render/build defects found during verification.

**Interfaces:**
- Consumes the completed implementation.
- Produces verified source/CI state and a concise physical-validation handoff.

- [ ] **Step 1: Review the complete diff against the spec**

Check there is no second ranking implementation, no automatic report opening, no new chart dependency, and no ZIP operation before final report validation.

- [ ] **Step 2: Run final source verification**

```powershell
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Release
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Expected: PASS.

- [ ] **Step 3: Verify CI on the exact pushed `main` revision**

Confirm the GitHub Actions `Tests` workflow succeeds on the final commit.

- [ ] **Step 4: Physical visual check on Windows**

Run one Gate A development/evidence session and inspect: light, dark, narrow width, high contrast, 16-candidate readability, Keep/Restore copy, explicit ZIP actions, and the actual generated ZIP contents. If a real Keep result is unavailable, use a real Restore report for layout verification and keep Keep-specific physical behavior pending rather than fabricating completion.

- [ ] **Step 5: Final handoff**

Report exact final commit, CI result, generated ZIP convention, and any remaining hardware-only validation. Physical Gate A evidence remains separate from source completion.
