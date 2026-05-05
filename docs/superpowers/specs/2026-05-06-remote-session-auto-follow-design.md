# Remote Session Auto-Follow Design

Date: 2026-05-06  
Repo: `C:\Users\shang\Project\salmon-acp`  
Scope: Remote session load/hydration auto-follow (auto-scroll to transcript bottom) architecture optimization  
Status: Approved design (implementation not started)

## 1. Problem Statement

Current auto-follow behavior during remote session hydration and replay relies on a dense set of View-side event branches (`LayoutUpdated`, viewport changed callbacks, pointer/key/wheel handlers, VM property changes).  
This creates three recurring risks:

1. User-intent ambiguity: "viewport temporarily left bottom" can be misclassified as manual detachment.
2. Lifecycle coupling: hydration/session-switch semantics and viewport semantics are mixed in View orchestration.
3. Long-term maintainability risk: policy logic is hard to reason about and easy to regress.

The goal is to preserve native WinUI/Uno behavior while moving policy ownership to a testable Core coordinator, with View limited to platform facts and platform command execution.

## 2. Design Goals and Non-Goals

### Goals

1. Establish a single policy owner for auto-follow semantics.
2. Keep platform-specific viewport detection/execution in View adapter only.
3. Guarantee latest-intent and session identity safety (`ConversationId + Generation`).
4. Eliminate false detach caused by non-user layout/virtualization noise.
5. Improve deterministic testability and observability.

### Non-Goals

1. No ACP protocol contract changes.
2. No redesign of remote hydration state machine in `ChatViewModel`.
3. No UX redesign of loading overlay copy/styling.

## 3. Recommended Architecture (Hybrid, Best-Practice)

Use a hybrid ownership model:

1. `Presentation.Core` owns policy semantics (`TranscriptViewportCoordinator`).
2. View owns native control facts and native command execution.
3. `ChatViewModel` remains authoritative for session/hydration lifecycle.

Rationale:

1. Pure Core ownership cannot directly and safely observe all native viewport details.
2. Pure View ownership keeps policy fragmented and event-order fragile.
3. Hybrid preserves MVVM cleanliness and native-first constraints with practical runtime correctness.

## 4. Components and Responsibilities

### 4.1 `TranscriptViewportCoordinator` (new, Core)

Single owner of auto-follow policy decisions.

Inputs:

1. Session lifecycle events (`SessionActivated`, `ConversationContextInvalidated`).
2. Hydration/overlay events (`HydrationPhaseChanged`).
3. User intent events (`UserIntentScroll`).
4. Viewport fact events (`ViewportFactChanged`).
5. Transcript growth events (`TranscriptAppended`).

Outputs (commands):

1. `IssueScrollToBottom(generation)`
2. `StopProgrammaticScroll(reason)`
3. `MarkAutoFollowAttached`
4. `MarkAutoFollowDetached`
5. `Noop`

### 4.2 View Adapter (ChatView / MiniChatView)

Responsibilities only:

1. Observe native state and publish `ViewportFact`.
2. Execute coordinator commands with native APIs (`ScrollIntoView`, existing monitor hooks).
3. Return execution outcome/fresh facts.

View must not infer business policy (for example, must not infer detachment from transient non-bottom without user intent).

### 4.3 `ChatViewModel` and Store

No ownership change for:

1. Conversation activation and hydration stages.
2. Remote replay completion semantics.
3. Overlay owner and latest-intent state transitions.

VM only publishes lifecycle signals consumed by the coordinator.

## 5. Coordinator State Machine

States:

1. `Idle`: no active session or no messages.
2. `Settling`: post-activation/hydration initial settle-to-bottom phase.
3. `Following`: auto-follow attached.
4. `DetachedByUser`: explicitly detached by user intent.
5. `Suspended`: context invalidated while transition in flight.

Hard invariants:

1. `DetachedByUser` is entered only by explicit user-intent event.
2. Transient non-bottom without user intent never detaches auto-follow.
3. New session activation resets previous detachment context.
4. Commands carrying stale `ConversationId + Generation` are ignored.

## 6. Data Flow and Concurrency Semantics

Flow:

1. VM emits lifecycle signal.
2. View adapter emits `ViewportFact`.
3. Coordinator serially processes events.
4. Coordinator emits command.
5. View executes command and emits follow-up fact.

Concurrency rules:

1. Every event includes `ConversationId + Generation`.
2. Stale events are dropped at coordinator boundary.
3. Programmatic-scroll in-flight suppresses user-detach inference from viewport movement.
4. User-intent events have highest priority and can abort settle rounds.

## 7. Performance Strategy

1. Prefer monitor numeric signals (`verticalOffset`, `scrollableHeight`) for bottom detection.
2. Keep geometry transform checks as fallback only.
3. Demote `LayoutUpdated` from strategy owner to lightweight fact refresh trigger.
4. Add command rate limiting (max one issue per frame-equivalent window in burst scenarios).
5. Keep bounded retry budget for settle rounds to avoid infinite loops.

## 8. Error Handling and Degradation

1. If native viewport not ready (tree/container not materialized), stay in `Settling`, do not detach.
2. If settle budget exhausted, emit warning and converge to stable non-looping state.
3. If command execution fails due to context switch, ignore by generation guard.
4. If event order is late/out-of-order, drop by identity guard.

## 9. Observability

Add structured transition logs:

1. `fromState`, `toState`
2. `event`
3. `reason`
4. `conversationId`
5. `generation`

Keep `ChatView.TranscriptViewportState` probe, but project coordinator semantic state instead of mixed low-level transient state to reduce diagnostic ambiguity.

## 10. Testing Strategy

### 10.1 New Core tests (table-driven)

1. Hydration settle reaches bottom.
2. Replay bursts keep follow semantics and avoid premature detach.
3. User scroll detaches and blocks auto-reclaim.
4. Return-to-bottom re-attaches.
5. Cross-conversation stale events are ignored.

### 10.2 View adapter tests

1. `ViewportFact` sampling priority (monitor first, geometry fallback).
2. Native command dispatch and no-op behavior on stale generation.

### 10.3 Existing gate retention + extension

Retain and strengthen GUI smoke:

1. `SelectRemoteSessionWithSlowReplay_ViewportStateReportsBottomAfterHydration`
2. `SelectRemoteSessionWithSlowReplay_PageUpDetachesViewportAfterHydration`

Add:

3. "No user intent + viewport jitter must not detach" scenario.

## 11. Migration Plan

### Phase 1: Introduce coordinator and interface seams

1. Add coordinator and event/command contracts.
2. Keep current flow behind an internal toggle fallback.

### Phase 2: Switch ChatView primary path

1. Route decisions through coordinator.
2. Keep old branch for temporary rollback.

### Phase 3: Align MiniChatView

1. Reuse same coordinator semantics.
2. Remove mini-window-only policy drift.

### Phase 4: Cleanup and convergence

1. Remove legacy duplicated flags/branches.
2. Normalize probes and structured logs.
3. Remove fallback toggle after validation closure.

## 12. Risks and Mitigations

1. Risk: migration overlap with active hydration/session work.  
   Mitigation: phase gates + deterministic state-machine tests before branch cleanup.

2. Risk: behavior drift between main chat and mini chat.  
   Mitigation: shared coordinator contract + mirrored scenario tests.

3. Risk: hidden platform-specific edge cases.  
   Mitigation: keep native execution in View adapter and add platform-specific adapter tests.

## 13. Acceptance Criteria

1. Auto-follow is not detached without explicit user scroll intent.
2. Hydrated remote sessions settle to bottom under slow replay and replay bursts.
3. User manual detachment remains respected until explicit return-to-bottom.
4. No stale event can mutate current conversation viewport policy.
5. Core + GUI coverage includes all scenarios in sections 10.1 and 10.3.

## 14. Out of Scope for This Design

1. Refactoring ACP connection ownership model.
2. Changing overlay copy or visual style system.
3. Introducing protocol-level new fields for viewport synchronization.

