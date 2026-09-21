# Owner closure workflow

This runbook is the shortest supported path from source-complete to owner-local physical evidence. It does **not** weaken the safety contract and it does not arm public mutation.

## 1. Capture current-revision Phase 2 baselines

Use a clean `main` checkout whose exact HEAD already has a successful hosted `Tests` workflow. Install/run the observation Service from that same source revision and capture both:

- one five-window **Steady real-world workload** decision baseline;
- one five-window **Controlled idle** decision baseline.

For the steady Real-world baseline, hold one warmed scene, action loop or workload pattern through all five windows. A scripted benchmark that intentionally changes scenes/phases is not a substitute for this closure baseline; repeated whole benchmark runs are a separate experiment shape.

Both files must be closure-grade `latencypilot-evidence-v9` evidence from the exact 40-hex source revision. Baseline v9 serializes `baseline-quality-v2`, `workload-stability-v1`, and explicit optimizer-eligibility evidence so `Valid for comparison` cannot be confused with `Ready for optimization`. Do not reuse an older baseline after source changes.

## 2. Run the consolidated read-only audit

From the repository root in PowerShell:

```powershell
$head = (git rev-parse HEAD).Trim()
$out = Join-Path $env:USERPROFILE "Documents\LatencyPilot\validation\phase2-readonly-closure-$head.json"

dotnet run --project .\tools\LatencyPilot.ReadOnlyClosure\LatencyPilot.ReadOnlyClosure.csproj -- `
  --expected-commit $head `
  --realworld-baseline "C:\path\to\steady-realworld-baseline.json" `
  --controlled-idle-baseline "C:\path\to\controlled-idle-baseline.json" `
  --output $out
```

The command is deliberately read-only except for writing the requested audit JSON. It fails closed unless the required source/CI/Service/journal/ETW/device/USB/evidence checks pass, including:

- clean local `main` at the exact requested HEAD;
- GitHub `main` and a successful exact-SHA hosted `Tests` run;
- `ServiceBoundary.MutationAvailable=false`;
- exact protected observation-Service provenance;
- zero unresolved mutation journal entries;
- no stale `LatencyPilot-Kernel-*` ETW session;
- representative GPU and xHCI/device topology visibility required by the current audit;
- both baseline files match the exact revision/scenario and pass the canonical evidence verifier.

The Service provenance check reads version metadata from the executable configured in Windows Service Control Manager. A correctly named or correctly located stale binary is not accepted as exact-revision closure evidence.

`logman query -ets` and Windows service inspection are observation surfaces only; the audit does not start/stop a Service, ETW session, device, or mutation experiment.

## 3. Capture reproducible UI/accessibility evidence

LatencyPilot includes `scripts/Capture-UiAccessibilityEvidence.ps1`, which uses Windows app/UI Automation tooling. Install the required WinApp CLI package once, start the exact-revision App, place the UI in the state being reviewed, and capture each required scenario separately:

```powershell
$uiOut = Join-Path $env:USERPROFILE 'Documents\LatencyPilot\validation\ui'

.\scripts\Capture-UiAccessibilityEvidence.ps1 `
  -AppTarget 'LatencyPilot' `
  -Scenario Light `
  -OutputDirectory $uiOut

.\scripts\Capture-UiAccessibilityEvidence.ps1 `
  -AppTarget 'LatencyPilot' `
  -Scenario Dark `
  -OutputDirectory $uiOut

.\scripts\Capture-UiAccessibilityEvidence.ps1 `
  -AppTarget 'LatencyPilot' `
  -Scenario HighContrast `
  -OutputDirectory $uiOut
```

Repeat for `TextScale`, `Narrow`, and `Keyboard` after placing Windows/the App in the corresponding state. Each run records connection state, UIA trees, screenshot and SHA-256 manifest. Capture alone does **not** establish accessibility compliance.

Review names/roles/focus exposure, run Accessibility Insights FastPass, exercise the primary flow without pointer input, and perform a focused Narrator pass. Human review remains authoritative for clipping/overlap, reading order, announcement quality and whether the interaction is understandable.

## 4. Finish remaining manual Phase 2 evidence

A green read-only audit JSON plus UI evidence is **not** Phase 2 closure by itself. Record the remaining owner-observed checks from `docs/PHYSICAL_VALIDATION.md`:

1. compare DPC/ISR attribution against an independent observer where practical;
2. exercise active-console-session rejection plus App-close, Service-restart and interrupted/partial-baseline behavior;
3. review Light/Dark/High Contrast/narrow/text-scale/keyboard/UIA evidence with Accessibility Insights and Narrator;
4. compare before/after machine state and explicitly record that read-only validation caused no unrelated system mutation.

Only after those checks and the automated audit agree may Phase 2 be marked physically closed.

## 5. Run GPU Gate A

After the exact revision is green and the prerequisite read-only substrate is understood, follow `docs/PHASE3_PHYSICAL_VALIDATION.md`.

GPU Gate A is a **different measurement shape** from the five-window Real-world baseline. It uses the built-in deterministic D3D12 candidate search with time-local Original controls, normalized decision aggregates and strict final ETW ISR-placement proof. Do not feed the Real-world baseline into the GPU candidate ranking or treat it as a substitute for the Gate A Original controls.

Gate A still requires real owner-local apply/restart/runtime-placement/rollback/recovery evidence. ConfigMgr allocated interrupt resources are provenance only; final Keep requires attributable runtime ISR placement on the requested logical processor.

A successful Gate A session also needs the in-product result/evidence path inspected: validated report → evidence ZIP → Overview decision presentation. The UI must distinguish raw trials, decision aggregates and verified terminal state.

Do not expose or arm public mutation before Gate A is physically proven.

## 6. Continue the v1 dependency chain

After GPU Gate A closes:

1. introduce/verify the typed allowlisted product mutation boundary;
2. close the integrated primary-input → USB → xHCI recommendation/apply/runtime-verification path;
3. close combined reboot/resume/recovery behavior;
4. capture final comparable before/after evidence;
5. finish normal-user one-button UX, Restore original settings, accessibility/runtime checks and signed package/install/upgrade/uninstall evidence.

NIC/RSS automatic mutation, audio affinity, MSI-mode toggling, HAGS/BIOS/power changes and generic cross-subsystem optimization are **not** v1 closure dependencies.