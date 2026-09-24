# Owner closure workflow

This runbook is the shortest supported path from source-complete to owner-local physical evidence. It does **not** weaken the safety contract and it does not arm public mutation.

## 1. Capture current-revision Phase 2 baselines

Use a clean `main` checkout whose exact HEAD already has a successful hosted `Tests` workflow. Install/run the observation Service from that same source revision and capture both:

- one five-window **Steady real-world workload** decision baseline;
- one five-window **Controlled idle** decision baseline.

For the steady Real-world baseline, hold one warmed scene, action loop or workload pattern through all five windows. A scripted benchmark that intentionally changes scenes/phases is not a substitute for this closure baseline; repeated whole benchmark runs are a separate experiment shape.

Both files must be closure-grade `latencypilot-evidence-v9` evidence from the exact source revision. Do not reuse an older baseline after source changes.

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

The command is deliberately read-only except for writing the requested audit JSON. It fails closed unless required source/CI/Service/journal/ETW/device/USB/evidence checks pass, including:

- clean local `main` at the exact requested HEAD;
- GitHub `main` and a successful exact-SHA hosted `Tests` run;
- `ServiceBoundary.MutationAvailable=false`;
- exact protected observation-Service provenance;
- zero unresolved mutation journal entries;
- no stale LatencyPilot kernel ETW session;
- representative GPU and xHCI/device topology visibility required by the current audit;
- both baseline files match the exact revision/scenario and pass the canonical evidence verifier.

The Service provenance check reads version metadata from the executable configured in Windows Service Control Manager. A correctly named or correctly located stale binary is not accepted as exact-revision closure evidence.

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

## 5. Run observer-isolated v5 GPU Gate A

After the exact revision is green and the prerequisite read-only substrate is understood, follow `docs/PHASE3_PHYSICAL_VALIDATION.md`.

GPU Gate A is a **different measurement shape** from the five-window Real-world baseline. It uses the built-in deterministic D3D12 benchmark and direct local controls:

```text
3–5 Original observations for robust median/MAD variability
→ Original before → Candidate → Original after local pairs
→ Stage-A physical-core representative screen
→ bounded uncertainty-aware core/sibling refinement
→ observed top four logical CPUs rechecked for 10 s when available
→ at most one uncertainty-overlapping fifth challenger
→ top two finalists
→ two shuffled 15 s local pairs per finalist
→ optional third 15 s round only while uncertainty remains
→ best-observed CPU + confidence
→ separate Keep guardrails
→ final clean target-only ETW ISR-placement proof before Keep
```

Do not feed the Real-world Phase 2 baseline into GPU candidate ranking or treat it as a substitute for the Gate A local controls.

Key v5 closure rules:

- screening/recheck windows are 10 s and finalist windows are 15 s;
- raw observations remain unchanged;
- paired effect is derived from the geometric mean of adjacent Original controls;
- one fresh retry is allowed for high local drift, but a structurally valid retry remains rankable and lowers confidence rather than erasing the candidate;
- full search screens one eligible logical representative per physical core before bounded refinement;
- Stage C rechecks the observed top four logical CPUs when available and admits at most one additional uncertainty-overlapping challenger;
- only the top two advance to finalist confirmation;
- two shuffled 15 s finalist pairs are the default, with one third round only while uncertainty still overlaps;
- rank 1 remains the best-observed estimate even when confidence is low or a practical tie is present;
- Keep guardrails are independent from ranking;
- final Keep requires attributable target-only runtime GPU ISR placement on the requested logical processor;
- terminal stored state and journal ownership must verify.

A successful Gate A session also needs the in-product result/evidence path inspected: validated report → evidence ZIP → Overview decision presentation. The UI must distinguish raw observations, paired effects, persisted decision/finalist authority, confidence and verified terminal state.

`GateAClosureEligible` means source/evidence eligibility only. Product copy should say **Evidence eligible** and must not imply that physical Gate A is already closed.

Do not expose or arm public mutation before Gate A is physically proven.

## 6. Repeat, stop safely and recover

Gate A closure requires more than one happy-path run. On the same exact clean green revision:

1. run the complete v5 search and preserve the full evidence bundle;
2. return to exact Original and repeat the whole search for practical reproducibility;
3. run a separate search and invoke **Stop safely** while a candidate mutation is owned; require exact Original and `unresolved=0`;
4. exercise one supported failure/recovery path from the existing physical-validation tooling; require exact Original or explicit fail-closed/manual-intervention state, then zero unresolved ownership before closure;
5. inspect the real result/progress surfaces in Light, Dark, High Contrast, text scaling, narrow/wide, keyboard and UIA/Narrator states.

Hosted CI is necessary software evidence but cannot substitute for these owner-local physical checks.

## 7. Continue the v1 dependency chain

Only after GPU Gate A closes:

1. introduce/verify the typed allowlisted product mutation boundary;
2. prove physical App → Service mutation authorization;
3. close the integrated primary-input → USB → xHCI recommendation/apply/runtime-verification path;
4. close combined reboot/resume/recovery behavior;
5. capture final comparable before/after evidence;
6. finish normal-user one-button UX, prominent Restore original settings, accessibility/runtime checks and signed package/install/upgrade/uninstall evidence.

NIC/RSS automatic mutation, audio affinity, MSI-mode toggling, HAGS/BIOS/power changes and generic cross-subsystem optimization are **not** v1 closure dependencies.

## Completion rule

Do not call the project physically complete because source builds or hosted tests are green. Source completion requires canonical docs and exact-head hosted verification to match the implemented contract. True v1 completion additionally requires the physical GPU, xHCI, reboot/recovery, before/after, accessibility and packaging gates described in `ROADMAP.md`.