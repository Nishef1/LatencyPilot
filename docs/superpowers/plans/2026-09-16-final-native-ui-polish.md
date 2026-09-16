# Final Native UI Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish LatencyPilot's Windows-native UI by removing visual/copy AI slop, making Mica the long-lived window material, reserving Acrylic for transient surfaces, improving responsive behavior, and reducing redundant actions without changing measurement or safety semantics.

**Architecture:** Keep the existing native WinUI `NavigationView` shell and four product views. Reuse the existing design-token/style SSOT; do not introduce a UI framework, MVVM layer, or new package. Keep measurement, evidence, Gate A, protocol v6, and mutation boundaries unchanged.

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK 2.4.0, XAML design resources, GitHub Actions hosted critical-tests gate.

**Spec:** `docs/DESIGN_SYSTEM.md`

## Global Constraints

- Work directly on `main`; no new branch.
- Do not local-build; hosted GitHub Actions is the compile/test gate.
- Preserve protocol v6, evidence semantics, ETW behavior, optimizer policy, and public `MutationAvailable=false` boundary.
- No new UI package or framework.
- Prefer Windows theme resources and Fluent icons over literal product decoration.
- Mica is the long-lived window material; Acrylic is reserved for transient flyouts/menus where it materially helps hierarchy.
- Do not add a health score or fabricated visualization.
- Avoid tests for incidental XAML wording or visual markup; use hosted compile coverage and existing durable behavior tests.

---

### Task 1: Reconcile current HEAD and unblock the hosted gate

**Files:**
- Review: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Review: `tests/LatencyPilot.CriticalTests/SourceRevisionIdentityTests.cs`

**Interfaces:**
- Consumes: current `main` and hosted `Tests` workflow.
- Produces: a current green/non-stale baseline before UI changes.

- [ ] **Step 1: Inspect the latest `main` commit and exact workflow result.**

Confirm any concurrent Gate A changes are present before writing UI files. Do not overwrite them.

- [ ] **Step 2: If CI is red, read the exact failing assertion/build error and identify the root cause.**

The expected prior failure was a source-contract test requiring `BuildGateATerminalSummary`; if current source already contains that method, treat the old failure as stale and wait for the exact current run instead of editing the test.

- [ ] **Step 3: Continue only from a reconciled current HEAD.**

Do not claim green unless the workflow head SHA equals the current branch SHA.

---

### Task 2: Make the window material native and restrained

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/DashboardShell.cs`
- Modify: `src/LatencyPilot.App/Design/DesignTokens.xaml`
- Modify: `docs/DESIGN_SYSTEM.md`

**Interfaces:**
- Consumes: WinUI `MicaBackdrop`, existing semantic surface brushes.
- Produces: one authoritative long-lived Mica backdrop and transparent page root.

- [ ] **Step 1: Replace window-level `DesktopAcrylicBackdrop` with `MicaBackdrop`.**

Use:

```xml
<Window.SystemBackdrop>
    <MicaBackdrop />
</Window.SystemBackdrop>
```

Keep `RootGrid` transparent so the system material remains visible.

- [ ] **Step 2: Remove the C# Acrylic-enforcement path.**

Delete `ApplyDesktopAcrylicBackdrop()` and its `Loaded`/appearance calls. Do not replace it with a competing runtime material setter; the XAML backdrop is authoritative.

- [ ] **Step 3: Remove material-specific dead tokens if they have no consumers.**

Delete legacy rail/context/artwork/navigation-breakpoint resources only after confirming no current XAML/C# consumer remains.

- [ ] **Step 4: Update `docs/DESIGN_SYSTEM.md`.**

Document Mica as the window material and Acrylic as optional transient material, while preserving High Contrast fallback requirements.

- [ ] **Step 5: Commit this material-only change.**

Commit message: `refactor(ui): use Mica for the app shell`

---

### Task 3: Remove copy and visual AI slop

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/PremiumObservationUi.cs`
- Modify: `src/LatencyPilot.App/Design/ComponentStyles.xaml`

**Interfaces:**
- Consumes: existing measurement/status TextBlocks and semantic brushes.
- Produces: shorter, literal copy and calmer visual hierarchy without changing underlying state.

- [ ] **Step 1: Tighten top-level copy.**

Use direct labels such as:

```text
Overview
Current interrupt latency and baseline quality.

Measure
Capture a snapshot or build a repeatable baseline.

Devices
Hardware paths behind interrupt activity.

Evidence
Raw measurements, attribution, and provenance.
```

Avoid marketing phrases such as “evidence you can trust”, “see what deserves attention”, or duplicated instructions that are already expressed by controls.

- [ ] **Step 2: Remove redundant visible explanations.**

Keep methodology/help text only where it changes user action or prevents misinterpretation. Move long diagnostic explanation to tooltips or Evidence when already represented elsewhere.

- [ ] **Step 3: Simplify decorative icon tiles.**

Keep Fluent icons for navigation/action/status semantics; remove large colored icon backgrounds where they do not add hierarchy. Preserve distinct chart colors only where they encode DPC/ISR/semantic status.

- [ ] **Step 4: Normalize typography through shared styles.**

Prefer existing shared WinUI-compatible styles and design tokens over per-screen `FontSize`/radius literals when a semantic style exists.

- [ ] **Step 5: Commit copy/hierarchy cleanup.**

Commit message: `refactor(ui): remove dashboard copy noise`

---

### Task 4: Improve page logic and responsive layout

**Files:**
- Modify: `src/LatencyPilot.App/MainWindow.xaml`
- Modify: `src/LatencyPilot.App/DashboardShell.cs`

**Interfaces:**
- Consumes: `ShowDashboardView`, `ReflowCards`, existing named grids/views.
- Produces: responsive Overview, Measure, Devices, and Evidence layouts with no zero-width parked controls.

- [ ] **Step 1: Keep Overview progressive disclosure.**

Before first capture, show one compact empty state and one primary action. Keep the data dashboard collapsed until evidence exists.

- [ ] **Step 2: Remove duplicate snapshot actions in Measure.**

Keep one snapshot action in the Measure header and one baseline action. The Recent snapshot card becomes status/readout only instead of another CTA.

- [ ] **Step 3: Reflow Devices cards.**

Give the Graphics/Network/USB grid an `x:Name` and use the existing `ReflowCards` helper to switch 3 → 1 columns at narrower widths. Ensure inventory fields also stack/condense instead of clipping.

- [ ] **Step 4: Reflow Evidence summary regions.**

Where Evidence uses side-by-side panels, name the grid and apply the same responsive helper. Do not create nested scroll regions.

- [ ] **Step 5: Make header/action layout explicit.**

Do not hide actions by placing them in zero-width columns. When width is insufficient, stack or relocate the action row while keeping controls reachable.

- [ ] **Step 6: Commit responsive logic.**

Commit message: `refactor(ui): finish responsive page flows`

---

### Task 5: Keep advanced/developer functionality out of consumer hierarchy

**Files:**
- Modify only if needed: `src/LatencyPilot.App/MainWindow.xaml`
- Modify only if needed: `src/LatencyPilot.App/GateAValidationExperience.cs`

**Interfaces:**
- Consumes: `DeveloperValidationCard`, `DeveloperValidationHost`.
- Produces: owner-only Gate A UI that remains available in development checkouts without competing with ordinary measurement actions.

- [ ] **Step 1: Keep Gate A under Measure → Developer validation only.**

Do not return it to Overview or header actions.

- [ ] **Step 2: Shorten the visible label if needed while preserving automation/help detail.**

Visible copy should be concise; the long safety explanation belongs in AutomationProperties/tooltip/status output.

- [ ] **Step 3: Do not modify Gate A execution, rollback, recovery, or terminal outcome logic.**

If current `main` has concurrent Gate A safety fixes, preserve them byte-for-byte unless a compile break requires a UI-only adaptation.

---

### Task 6: Hosted verification and scope review

**Files:**
- Review: `.github/workflows/ci.yml`
- Review: final diff since the pre-polish HEAD.

**Interfaces:**
- Consumes: current `main` after UI commits.
- Produces: exact-current-HEAD hosted proof and a freeze-worthy UI source state.

- [ ] **Step 1: Wait for the workflow triggered by the final UI commit.**

Expected command remains:

```text
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

- [ ] **Step 2: Require exact SHA match.**

Branch `main` SHA and workflow `head_sha` must match.

- [ ] **Step 3: Require all durable tests to pass.**

Do not weaken source-contract or safety tests to make UI work pass.

- [ ] **Step 4: Review the final diff for scope.**

Confirm there is no protocol/schema/ETW/optimizer/safety expansion and no unrelated package addition.

- [ ] **Step 5: Stop source work after exact-green.**

The next step is real Windows render review at 1664×936 plus dark theme, followed by the already-planned physical validation sequence; do not add speculative UI infrastructure.
