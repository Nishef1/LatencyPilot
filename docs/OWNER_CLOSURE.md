# Owner closure workflow

This runbook is the shortest supported path from source-complete to owner-local physical evidence. It does **not** weaken the Gate A/B/C/D safety contract and it does not arm public mutation.

## 1. Capture current-revision baselines

Use a clean `main` checkout whose exact HEAD already has a successful `Tests` workflow. Install/run the observation Service from that same source revision and capture both of these from the App:

- one five-window **Real-world workload** decision baseline;
- one five-window **Controlled idle** decision baseline.

Both files must be closure-grade `latencypilot-evidence-v8` evidence from the exact 40-hex source revision. Do not reuse an older baseline after source changes.

## 2. Run the consolidated read-only audit

From the repository root in PowerShell:

```powershell
$head = (git rev-parse HEAD).Trim()
$out = Join-Path $env:USERPROFILE "Documents\LatencyPilot\validation\phase2-readonly-closure-$head.json"

dotnet run --project .\tools\LatencyPilot.ReadOnlyClosure\LatencyPilot.ReadOnlyClosure.csproj -- `
  --expected-commit $head `
  --realworld-baseline "C:\path\to\realworld-baseline.json" `
  --controlled-idle-baseline "C:\path\to\controlled-idle-baseline.json" `
  --output $out
```

The command is deliberately read-only except for writing the requested audit JSON. It fails closed unless all automated checks pass:

- clean local `main` at the exact requested HEAD;
- GitHub `main` matches that SHA;
- an exact-SHA successful `Tests` workflow exists;
- `ServiceBoundary.MutationAvailable=false`;
- the installed observation Service is running from the protected LatencyPilot Service path;
- the mutation journal has zero unresolved entries;
- no stale `LatencyPilot-Kernel-*` ETW session is active;
- representative GPU, NIC and xHCI devices are present;
- USB topology is readable with connected/xHCI-linked ports;
- the Windows RSS provider can be read;
- both baseline files match the exact source revision and expected scenario;
- both baseline files pass the canonical `scripts/Verify-Evidence.ps1` clean-capture/valid-baseline check and SHA-256 reconciliation.

`logman query -ets` and Windows service inspection are used only as observation surfaces; the audit does not start/stop a Service, ETW session, device, or mutation experiment.

## 3. Finish the manual Phase 2 evidence

A green audit JSON is **not** Phase 2 closure by itself. Record the remaining owner-observed checks from `docs/PHYSICAL_VALIDATION.md`:

1. compare DPC/ISR attribution against an independent observer where practical;
2. exercise active-console-session rejection plus App-close, Service-restart and interrupted/partial-baseline behavior;
3. inspect Light, Dark, High Contrast, narrow/text-scaled layouts, keyboard-only navigation and screen-reader/UIA naming;
4. compare before/after machine state and explicitly record that the read-only validation caused no unrelated system mutation.

Only after those checks and the automated audit agree may Phase 2 be marked physically closed.

## 4. Gate A next

After Phase 2 is physically closed, follow `docs/PHASE3_PHYSICAL_VALIDATION.md` using the exact same current-revision Real-world baseline. Gate A still requires a real owner-local GPU affinity apply/restart/runtime-placement/rollback/recovery exercise. ConfigMgr allocated interrupt resources are provenance only; success requires the exact stored candidate around a clean kernel capture plus attributable GPU-driver ISR runtime placement on the requested processor.

Do not expose or arm public mutation before Gate A is physically proven. Gate B, then Gate C/D, then USB/NIC physical mutation evidence remain ordered dependencies rather than parallel checkbox work.
