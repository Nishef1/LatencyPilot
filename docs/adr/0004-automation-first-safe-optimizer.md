# ADR 0004: Make safe one-click optimization the Phase 3 product path

- **Status:** Accepted
- **Date:** 2026-09-14
- **Amends:** the strict sequential wording between Phase 2 validation polish and Phase 3 source implementation

## Context

LatencyPilot exists to improve latency, not to make the user manually operate a laboratory forever. Phase 2 established a trustworthy ETW observation path and `baseline-quality-v2`; physical evidence on the owner's Windows 11 machine has now produced a valid five-window Real-world baseline with clean capture integrity and a repeatable graphics-stack / CPU0 concentration hypothesis.

A step-back review found a product-risk in continuing to require every diagnostic and product-polish check before writing any optimizer code: the project could become an excellent observer without delivering the core `Measure → Experiment → Verify → Compare → Keep or Revert` loop.

Prior art reinforces the need for automation but not blind copying:

- `valleyofdoom/AutoGpuAffinity` automatically applies GPU interrupt-affinity candidates, restarts the graphics device, measures each candidate for a configurable interval (30 seconds by default), and restores Windows default affinity after the benchmark;
- the related PC-Tuning guidance treats GPU/xHCI interrupt placement and NIC RSS as workload-specific candidates that should be benchmarked rather than assumed universally beneficial;
- Microsoft documents the supported `DevicePolicy` / `AssignmentSetOverride` interrupt-affinity semantics, but default policy guidance is not evidence that the default is latency-optimal on every machine;
- PresentMon exposes frame/CPU/GPU/latency metrics suitable for workload guardrails rather than relying only on DPC/ISR duration.

AutoGpuAffinity also documents a material failure mode: restarting the GPU can leave the driver unresponsive. LatencyPilot therefore must automate **more safely**, not merely automate more aggressively.

## Decision

### 1. One-click is the product direction

Starting in Phase 3, the primary UX target is a user action such as:

```text
Optimize GPU
```

The intended orchestration is:

```text
preflight
→ identify target/workload
→ obtain or refresh a valid baseline
→ snapshot exact original state
→ persist pending journal
→ generate bounded candidates
→ apply one candidate
→ verify actual state/runtime placement
→ measure target + guardrails
→ revert control
→ confirm finalists with balanced repeated ordering
→ Keep best only when evidence clears the comparison gate
→ otherwise restore exact original state
→ show result/history
```

The user may still inspect advanced evidence, but normal operation must not require manually driving every capture and mutation step.

### 2. Phase 3 source work may overlap remaining Phase 2 polish

A valid Real-world decision baseline on an exact clean, CI-green source revision is sufficient to begin implementing Phase 3 safety infrastructure and candidate planning.

This does **not** mean mutation is armed. Automatic mutation remains unavailable until the safety gate below is satisfied.

Controlled-idle measurement remains useful diagnostic context, especially for distinguishing workload-specific from background behavior, but it is no longer a prerequisite for writing the targeted Real-world GPU optimizer. Likewise, full visual/accessibility/productization polish must not block implementation of the mutation substrate; those requirements remain release obligations.

### 3. Mutation arming has a strict safety gate

Before any one-click flow is allowed to change Windows state on a user's machine, all of the following must exist and be physically validated for the supported mutation:

1. exact applicability/target identification;
2. exact original-state snapshot including absent registry values and original value kinds/bytes where relevant;
3. durable SQLite journal written **before** apply;
4. mutation-specific IPC authorization and allowlisted typed commands only;
5. candidate validation against CPU topology/device constraints;
6. post-write stored-state verification;
7. runtime/effective-state verification where Windows exposes trustworthy evidence;
8. deterministic revert to the exact original state;
9. restart/crash/reboot recovery that re-reads actual state before action;
10. forced-failure rollback exercise on physical hardware.

An unresolved journal blocks new mutation experiments.

### 4. Recovery is biased toward rollback

The first durable-journal version does not automatically "resume forward" after an interruption. If an experiment is left in an uncertain non-terminal state, recovery re-reads actual state and prefers verified rollback to the exact original snapshot. Forward-resume can be added later only with its own proven state semantics.

### 5. GPU candidate search is staged, not brute force

LatencyPilot will not blindly benchmark every logical CPU at full duration.

Initial GPU affinity search:

- preserve current/default Windows state as the control;
- support one processor group first because `AssignmentSetOverride` is a group-local `KAFFINITY` mask;
- generate one candidate per physical core, choosing SMT siblings using measured pressure rather than treating siblings as independent cores;
- do not hard-ban CPU0;
- screen a small bounded candidate set using prior ETW evidence;
- confirm only control + finalists with longer approximately 30-second A/B runs and balanced/interleaved order such as ABBA/BAAB;
- Keep only if target metrics improve beyond the comparison/noise policy without material guardrail regressions.

### 6. DPC/ISR is not the only GPU objective

ETW remains authoritative for DPC/ISR timing and CPU/module attribution. For graphics workloads, PresentMon frame-time, CPU/GPU busy/wait, GPU/display latency, presented/displayed behavior and dropped-frame data become targets/guardrails when available and adequately sampled.

No opaque universal score may hide a regression.

## Safety boundary for the first implementation

The first mutation implementation is deliberately narrow:

```text
kind = gpu-interrupt-affinity
supported OS = Windows 11 x64
processor groups = exactly 1
registry policy = documented Interrupt Management\Affinity Policy values only
target = validated present display adapter only
arbitrary registry/process/PowerShell = forbidden
```

MSI/MSI-X changes are **not** bundled into the first affinity experiment merely because another tuning tool exposes them. They require separate applicability, actual-state verification, rollback and evidence.

## Consequences

### Benefits

- product development now moves directly toward measurable latency improvement;
- the end-state is a one-click workflow instead of a manual benchmark console;
- prior art is reused at the experimental-design level without inheriting unsafe assumptions;
- crash/recovery safety is built before mutation, not retrofitted after a failed test;
- candidate count and runtime stay bounded.

### Costs

- Phase 2 source and Phase 3 source can overlap, so status reporting must distinguish "implemented" from "armed/validated";
- SQLite becomes a real dependency now;
- the Service/protocol will need a version bump when typed mutation commands are introduced;
- physical forced-failure testing is mandatory before automatic apply is enabled.

## Non-decision

This ADR does not claim that moving the GPU away from CPU0 will improve the owner's machine. Current evidence makes that a high-value hypothesis; the automated experiment still decides whether the candidate is kept.
