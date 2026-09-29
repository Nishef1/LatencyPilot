# ADR 0012 — Subsystem-separated experiment surfaces

Status: **Accepted**  
Accepted: 2026-09-29

## Context

LatencyPilot's source had already separated the development Interrupt Policy Lab from GPU Gate A, but GPU Gate A still owned non-GPU work internally:

- the App resolved or asked the owner to choose a primary Raw Input mouse before starting GPU Gate A;
- the selected input identity crossed the elevation boundary as `--primary-input`;
- after the GPU search completed, the Gate A runner captured a quiet kernel window and persisted a USB/xHCI recommendation inside the GPU report.

That contradicted the intended product model. A button labeled GPU Gate A should have one authority: GPU measurement, ranking, Keep-or-Restore and physical GPU closure. USB/xHCI and Network/RSS have different topology, measurement and verification semantics and should be independently understandable and runnable.

## Decision

### 1. GPU Gate A is GPU-only

GPU Gate A owns only:

```text
GPU source/provenance preflight
→ GPU benchmark qualification
→ GPU candidate search/ranking
→ GPU Keep-or-Restore
→ GPU allocated-resource + runtime ISR verification
→ GPU Gate A closure evidence
```

GPU Gate A must not:

- select or ask for a primary mouse;
- resolve Raw Input → USB → xHCI as part of the GPU session;
- accept a `--primary-input` argument;
- compute or persist a new USB/xHCI recommendation;
- claim USB or network readiness/closure.

The optional `UsbRecommendation` field remains readable for historical report compatibility only. New GPU Gate A runs leave it unset.

### 2. USB/xHCI owns its own action and authority

USB/xHCI gets an independent product/development entry point.

Its flow may later consume an already verified GPU terminal state as an exclusion/guardrail input, but that does not make USB part of GPU Gate A. USB/xHCI owns:

- primary-input selection when needed;
- exact Raw Input → USB hub/port → xHCI routing;
- fresh interrupt-headroom evidence;
- xHCI CPU selection;
- xHCI apply/rollback ownership;
- controller-specific runtime verification;
- USB/xHCI result presentation.

Until the automatic apply path is physically armed, the independent USB action may remain evidence/readiness focused.

### 3. Network/RSS owns a separate read-only v1 action

Network/RSS gets a separate evidence action in the Devices surface.

It does not reuse GPU's single-logical-CPU tournament. RSS is a multi-queue, multi-CPU receive-steering mechanism and any future mutation requires its own ADR, bounded candidate model, guardrails and physical evidence.

Therefore Network/RSS remains read-only in v1.

### 4. Interrupt Policy Lab remains separate

Interrupt Policy Lab is an explicit manual development utility. It may share low-level snapshot/journal/restart/rollback plumbing, but it is not the automatic GPU, USB or network entry point and it does not inherit subsystem closure semantics.

### 5. Future combined orchestration composes; it does not collapse

A future normal-user `Optimize all` action may sequence independently proven subsystem actions, for example GPU followed by xHCI.

That orchestration must preserve each subsystem's own:

- applicability checks;
- measurement contract;
- progress/result state;
- verification authority;
- rollback ownership.

It must not turn GPU Gate A back into an umbrella session.

## Implemented source shape

The development App now reflects this decision directly:

- Devices exposes independent GPU, USB/xHCI and Network/RSS actions;
- GPU Gate A no longer resolves primary input or emits new USB recommendations;
- USB/xHCI owns an elevated readiness runner with fresh kernel ETW headroom evidence;
- USB/xHCI produces `DiagnosticOnly` ranking when no separately verified GPU reservation is available and refuses automatic Apply in that state;
- when a Ready recommendation exists, the USB action reuses the bounded journaled xHCI Apply/runtime-verify/rollback path directly rather than routing through Policy Lab;
- an owned `ApplyRebootPending` xHCI candidate is resumed with its exact journaled mask before any new recommendation;
- Network/RSS remains a separate read-only action in v1.

Public Service mutation remains unarmed; these development actions do not bypass the physical/product arming gates.

## Consequences

### Positive

- UI wording now matches execution authority.
- GPU physical evidence is no longer polluted by input-route selection or post-GPU USB recommendation work.
- USB/xHCI can evolve and be physically validated without changing GPU Gate A closure semantics.
- Network/RSS can use an RSS-aware method rather than inheriting an invalid GPU analogy.
- Historical reports remain readable.

### Tradeoffs

- The user sees multiple subsystem actions instead of one premature one-button optimizer.
- USB/xHCI automatic selection/apply still needs its own integrated product flow after physical evidence closes the current gate.
- A future `Optimize all` experience requires a thin orchestration layer once the individual actions are proven.

These tradeoffs are intentional. The product should expose the simplest truthful control surface, not hide distinct experiments behind one button.
