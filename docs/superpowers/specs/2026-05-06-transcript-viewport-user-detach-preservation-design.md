# Transcript Viewport User-Detach Preservation Design

Date: 2026-05-06
Repo: `C:\Users\shang\Project\salmon-acp`
Scope: Preserve user-controlled detached transcript viewport behavior across session switches, warm returns, hydration, and replay without breaking native virtualization or auto-follow correctness
Status: Approved design (implementation not started)

## 1. Problem Statement

The current transcript viewport refactor moved policy into a Core coordinator, but it still models viewport behavior as page-local transient state instead of conversation-scoped state.

This creates two regressions:

1. After auto-follow reaches bottom, some real user scroll paths are not classified as explicit user detachment, so the system immediately reclaims bottom and makes the viewport feel locked.
2. When the user detaches from bottom in session A, switches to session B, and later returns to session A, the detached reading position is reset because activation re-enters settle/follow semantics instead of restoring conversation-specific viewport intent.

The fix must preserve native WinUI/Uno behavior, keep session/hydration authority in `ChatViewModel`, and prevent hack-style View-side policy drift.

## 2. Design Goals

1. Preserve user-detached transcript behavior per conversation, not per currently visible page instance.
2. Ensure warm return to a detached conversation does not auto-follow or reset the viewport to bottom.
3. Restore the user to the original reading region as closely as practical without requiring pixel-perfect reconstruction.
4. Keep auto-follow active only for explicit follow/bootstrap states.
5. Preserve virtualization, avoid synchronous layout forcing, and keep session activation smooth.
6. Leave clear extension points for future higher-fidelity viewport restoration.

## 3. Non-Goals

1. No ACP protocol changes.
2. No redesign of the remote hydration/replay owner in `ChatViewModel`.
3. No loading overlay copy/styling changes.
4. No requirement for exact pixel-perfect restoration in phase one.
5. No virtualized list de-optimization through full materialization or synchronous layout hacks.

## 4. Hard Constraints

1. `DetachedByUser` must be conversation-scoped.
2. Session activation must not implicitly reattach auto-follow to bottom.
3. View code-behind may publish native facts and execute native commands, but it must not own viewport policy.
4. `ChatView` and `MiniChatView` must share one semantic model.
5. Failed restore must never fall back to forced bottom recovery.
6. All lifecycle-sensitive viewport decisions must remain guarded by authoritative conversation identity and generation.

## 5. Recommended Architecture

Use a conversation-scoped viewport state model with a Core coordinator and thin View adapters.

### 5.1 Ownership Model

1. `ChatViewModel` remains authoritative for session activation, hydration, replay, and overlay lifecycle.
2. `TranscriptViewportCoordinator` in `Presentation.Core` becomes the only owner of viewport policy.
3. `ChatView` and `MiniChatView` act as native adapters:
   - publish viewport facts,
   - publish explicit user-detach events,
   - execute coordinator commands.

### 5.2 Core Shift

The existing page-local coordinator model is replaced with a conversation-scoped policy model:

1. viewport state is stored per `conversationId`,
2. session activation restores that conversation's viewport mode,
3. detached conversations do not re-enter bottom-follow semantics just because they become visible again.

## 6. Conversation Viewport State Model

Each conversation owns one `ConversationViewportState`.

### 6.1 Minimum Fields

1. `Mode`
   - `BootstrapSettling`
   - `Following`
   - `DetachedByUser`
2. `Anchor`
   - lightweight reading anchor snapshot
3. `LastKnownBottomState`
4. `LastActivationGeneration`
5. optional future-friendly restore metadata

### 6.2 Why This Boundary

This state belongs to the conversation because user reading intent survives page hiding, navigation away, and hot return. It does not belong to the current `ChatView` instance.

## 7. Anchor Model and Future Extensibility

Phase one should not aim for exact pixel reconstruction, but it must reserve a stable upgrade path.

### 7.1 Anchor Snapshot Shape

The anchor should be identity-based, not offset-only.

Recommended fields:

1. `AnchorMessageId`
2. `AnchorKind`
   - first visible item
   - primary reading item
3. `RelativeOffsetWithinAnchor`
4. `TranscriptVersion`

### 7.2 Rationale

Absolute `VerticalOffset` is not reliable across:

1. virtualization churn,
2. dynamic message height changes,
3. markdown/tool-call expansion,
4. window size changes,
5. mini/main view transitions.

An identity-based anchor keeps phase one compatible with virtualization and gives phase two a clean path toward more exact restoration.

## 8. Coordinator Responsibilities

`TranscriptViewportCoordinator` remains a single policy owner, but now reads and writes conversation-scoped viewport state.

Responsibilities:

1. process lifecycle, transcript, user-intent, and viewport-fact events,
2. restore the active conversation's viewport mode on activation,
3. decide whether to follow, restore anchor, remain detached, or do nothing,
4. reject stale conversation/generation events,
5. preserve detached user intent during warm return and session switching.

The coordinator must not treat generic non-bottom observations as sufficient evidence of user intent or sufficient reason to recover bottom.

## 9. View Adapter Responsibilities

`ChatView` and `MiniChatView` should only:

1. observe native viewport state,
2. detect confirmed native user-driven detachment,
3. capture anchor snapshots at detachment time,
4. execute coordinator commands using native APIs,
5. emit follow-up facts after command execution.

The View must not infer policy such as:

1. "non-bottom means recover bottom",
2. "session activation means attach auto-follow",
3. "restore failure means scroll to bottom".

## 10. Event Model

The coordinator should process the following logical events.

### 10.1 `SessionActivated(conversationId, activationKind)`

`activationKind` should distinguish at least:

1. `ColdEnter`
2. `WarmReturn`
3. `OverlayResume`

Purpose:

1. load the target conversation's viewport state,
2. decide whether the activation path should bootstrap-follow, restore anchor, or remain detached.

### 10.2 `TranscriptAppended(conversationId, addedCount)`

Rules:

1. `Following` may continue follow/recover behavior.
2. `DetachedByUser` must not auto-follow; it only records that transcript growth occurred while detached.

### 10.3 `UserDetached(conversationId, anchorSnapshot)`

Purpose:

1. transition the conversation into `DetachedByUser`,
2. persist the latest reading anchor.

This is the primary extensibility seam for future higher-fidelity restoration.

### 10.4 `ViewportObserved(conversationId, viewportFact)`

Purpose:

1. refresh facts,
2. advance restore/follow workflows,
3. monitor recoverability of anchors.

It must not, by itself, reinterpret any non-bottom observation as explicit user detach.

### 10.5 `ContextInvalidated(conversationId)`

Purpose:

1. stop in-flight programmatic scroll,
2. suspend the active projection,
3. preserve conversation-scoped detached/anchor state unless explicitly invalidated.

## 11. Command Model

Recommended commands:

1. `ScrollToBottom`
2. `RestoreAnchor`
3. `StopProgrammaticScroll`
4. `Noop`

Rules:

1. `RestoreAnchor` is preferred over `ScrollToBottom` when the conversation is detached.
2. `ScrollToBottom` is only valid for bootstrap/follow semantics.
3. `RestoreAnchor` failure must never degrade to forced bottom recovery.

## 12. Target A -> B -> A Behavior

### 12.1 User Detaches in Session A

1. A enters `DetachedByUser`.
2. A stores the latest `AnchorSnapshot`.

### 12.2 User Switches to Session B

1. A's viewport state remains stored by conversation.
2. B uses its own independent viewport state.

### 12.3 Session A Receives Background Messages

1. A's transcript/version facts are updated.
2. A must remain detached.
3. No background append may convert A back to `Following`.

### 12.4 User Returns to Session A

1. If A has a recoverable anchor, the coordinator issues `RestoreAnchor`.
2. If A's anchor is not yet recoverable, A remains detached and enters a bounded pending-restore path.
3. A must not auto-follow to bottom during this return path.

## 13. Failure and Degradation Rules

If restore cannot be completed immediately:

1. keep `DetachedByUser`,
2. retry only within a bounded restore budget,
3. stop after the budget is exhausted,
4. remain at the best recoverable detached position,
5. never auto-follow as a fallback.

Cases that count as temporarily non-recoverable:

1. anchor item container not materialized,
2. hydration or replay not completed,
3. size/layout changes after message reflow,
4. view-size changes between main and mini chat surfaces.

Return to `Following` is allowed only when:

1. the user explicitly returns to bottom, or
2. the user triggers an explicit jump-to-bottom action.

## 14. Performance and Smoothness Strategy

The design must guarantee minimum acceptable performance and smoothness during session activation and warm return.

### 14.1 Core Performance Principles

1. Do not break virtualization to restore reading position.
2. Do not use synchronous layout forcing.
3. Restoration must be phased, bounded, and abandonable.

### 14.2 Virtualized Restore Strategy

Restore should use two steps:

1. coarse positioning via `ScrollIntoView(anchor item)`,
2. one lightweight refinement after the anchor container is materialized.

If refinement cannot complete within the budget, the system stops and remains detached.

This keeps the algorithm compatible with virtualized lists because it relies on logical anchors rather than full list realization.

### 14.3 Activation Path Expectations

1. `ColdEnter`
   - regular hydration/replay path,
   - bootstrap settling is allowed,
   - no extra full-layout forcing.
2. `WarmReturn + Following`
   - fast return is allowed to continue bottom-follow semantics,
   - follow commands must remain rate-limited.
3. `WarmReturn + DetachedByUser`
   - no bottom recovery,
   - restore anchor if possible,
   - otherwise remain detached without reclaiming bottom.

### 14.4 Anti-Jank Rules

1. No `UpdateLayout()`.
2. `LayoutUpdated` may only refresh facts, not own policy.
3. `RestoreAnchor` has a finite budget:
   - at most one coarse jump,
   - at most one lightweight refinement,
   - then stop.
4. Burst transcript appends:
   - may follow in `Following`,
   - must not move viewport in `DetachedByUser`.
5. Prefer `ScrollViewerViewportMonitor` numeric facts first; geometry checks are fallback only.

## 15. Native User-Intent Collection Boundary

The View layer still needs to capture native user interaction evidence, but only as evidence.

Allowed temporary native latches:

1. pointer drag in progress,
2. native user-driven scroll change in progress,
3. scrollbar/thumb manipulation signals,
4. keyboard/manual scroll path signals.

These latches may help confirm and publish `UserDetached(anchorSnapshot)`, but they must not directly mutate policy state.

## 16. Testing Strategy

### 16.1 Core Tests

Add or update tests to cover:

1. `Following + TranscriptAppended -> follow/recover`
2. `DetachedByUser + TranscriptAppended -> no bottom reclaim`
3. `A detached -> switch to B -> back to A -> detached state restored`
4. `WarmReturn with anchor -> RestoreAnchor, not ScrollToBottom`
5. `RestoreAnchor temporarily unavailable -> remain DetachedByUser`
6. stale generation/conversation events ignored

### 16.2 GUI Smoke

Add or extend smoke coverage for:

1. real manual detach paths beyond `PageUp`,
2. session A detached -> switch to B -> return to A,
3. background append while detached must not steal reading position,
4. warm return must remain smooth and avoid blocking overlays unless lifecycle requires them.

### 16.3 Acceptance Bar

1. Session activation must not be blocked by viewport restoration work.
2. Detached warm return must not auto-follow.
3. Restoration must use a bounded command budget and avoid visible oscillation.
4. Reading region restoration may be approximate in phase one, but it must remain near the prior reading area.
5. No synchronous layout forcing or virtualization-breaking full materialization is allowed.

## 17. Implementation Phases

### Phase 1: Introduce Conversation-Scoped Viewport State

1. Add conversation-scoped viewport state and event/command model.
2. Keep restore semantics lightweight and detached-preserving.

### Phase 2: Switch Main Chat Path

1. Integrate `ChatView` with the new coordinator/store semantics.
2. Make A/B/A detached preservation authoritative.
3. Implement bounded lightweight `RestoreAnchor`.

### Phase 3: Align Mini Chat

1. Reuse the same coordinator semantics and restore policy.
2. Eliminate main/mini behavior drift.

### Phase 4: Improve Restoration Fidelity

1. Enhance anchor quality.
2. Improve restore precision.
3. Preserve the same conversation-scoped architecture and command model.

## 18. Risks and Mitigations

1. Risk: restore logic accidentally reintroduces View-owned policy.
   Mitigation: keep all restore/follow decisions in Core and use static guard tests.

2. Risk: restoration logic harms list performance.
   Mitigation: anchor identity-based recovery, bounded command budget, no full materialization.

3. Risk: main chat and mini chat drift apart again.
   Mitigation: shared event/command semantics and mirrored behavior coverage.

4. Risk: future precision work forces architecture churn.
   Mitigation: reserve explicit anchor snapshot extensibility now.

## 19. Acceptance Criteria

1. User detachment from bottom persists per conversation across session switches.
2. Returning to a detached conversation does not auto-follow to bottom.
3. Detached conversations attempt bounded reading-region restoration instead of bottom recovery.
4. Background transcript growth while detached does not steal reading control.
5. Session activation remains smooth and virtualization-safe.
6. The architecture remains extensible toward future higher-fidelity restoration without changing policy ownership boundaries.

