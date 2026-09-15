# Owner closure workflow

This runbook is the shortest supported path from source-complete to owner-local physical evidence. It does **not** weaken the Gate A/B/C/D safety contract and it does not arm public mutation.

## 1. Capture current-revision baselines

Use a clean `main` checkout whose exact HEAD already has a successful `Tests` workflow. Install/run the observation Service from that same source revision and capture both of these from the App:

- one five-window **Steady real-world workload** decision baseline;
- one five-window **Controlled idle** decision baseline.

For the steady Real-world baseline, hold one warmed scene, action loop or workload pattern through all five windows. A scripted benchmark that intentionally changes scenes/phases is not a substitute for this closure baseline; repeated whole benchmark runs are a separate experiment shape and are not currently Gate A candidate evidence.

Both files must be closure-grade `latencypilot-evidence-v8` evidence from the exact 40-hex source revision. Do not reuse an older baseline after source changes.

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

The command is deliberately read-only except for writing the requested audit JSON. It fails closed unless all automated checks pass:

- clean local `main` at the exact requested HEAD;
- GitHub `main` matches that SHA;
- an exact-SHA successful `Tests` workflow exists;
- `ServiceBoundary.MutationAvailable=false`;
- the installed observation Service is running from the exact protected LatencyPilot Service path **and its physical binary ProductVersion embeds the exact expected 40-hex source revision**;
- the mutation journal has zero unresolved entries;
- no stale `LatencyPilot-Kernel-*` ETW session is active;
- representative GPU, NIC and xHCI devices are present;
- USB topology is readable with connected/xHCI-linked ports;
- the Windows RSS provider can be read;
- both baseline files match the exact source revision and expected scenario;
- both baseline files pass the canonical `scripts/Verify-Evidence.ps1` clean-capture/valid-baseline check and SHA-256 reconciliation.

The Service provenance check reads version metadata from the executable actually configured in Windows Service Control Manager. A correctly named or correctly located stale Service binary is therefore not accepted as exact-revision closure evidence.

`logman query -ets` and Windows service inspection are used only as observation surfaces; the audit does not start/stop a Service, ETW session, device, or mutation experiment.

## 3. Capture reproducible UI/accessibility evidence

LatencyPilot includes `scripts/Capture-UiAccessibilityEvidence.ps1`, which uses Microsoft's WinApp CLI/UI Automation surface. Install `Microsoft.winappcli` with WinGet once, start the exact-revision App, put the UI into the state being reviewed, and capture each required scenario separately:

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

Repeat for `TextScale`, `Narrow`, and `Keyboard` after placing Windows/the App in the corresponding state. Each run records WinApp connection state, a deep UIA tree, an interactive-only tree, a screenshot and a SHA-256 manifest. The capture deliberately does **not** claim accessibility compliance by itself.

Review the captured UIA tree for meaningful names/roles and logical focus exposure, run Accessibility Insights FastPass, exercise the primary flow without pointer input, and perform a focused Narrator pass. Human review remains authoritative for clipping/overlap, reading order, announcement quality and whether the live interaction is understandable.

## 4. Finish the remaining manual Phase 2 evidence

A green read-only audit JSON plus UI evidence is **not** Phase 2 closure by itself. Record the remaining owner-observed checks from `docs/PHYSICAL_VALIDATION.md`:

1. compare DPC/ISR attribution against an independent observer where practical;
2. exercise active-console-session rejection plus App-close, Service-restart and interrupted/partial-baseline behavior;
3. review the captured Light/Dark/High Contrast/narrow/text-scaled/keyboard/UIA evidence with Accessibility Insights and Narrator;
4. compare before/after machine state and explicitly record that the read-only validation caused no unrelated system mutation.

Only after those checks and the automated audit agree may Phase 2 be marked physically closed.

## 5. Gate A next

After Phase 2 is physically closed, follow `docs/PHASE3_PHYSICAL_VALIDATION.md` using the exact same current-revision **steady Real-world** baseline. Gate A still requires a real owner-local GPU affinity apply/restart/runtime-placement/rollback/recovery exercise. ConfigMgr allocated interrupt resources are provenance only; success requires the exact stored candidate around a clean kernel capture plus attributable GPU-driver ISR runtime placement on the requested processor.

Do not expose or arm public mutation before Gate A is physically proven. Gate B, then Gate C/D, then USB/NIC physical mutation evidence remain ordered dependencies rather than parallel checkbox work.
