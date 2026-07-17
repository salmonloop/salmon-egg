# Conversation-Owned Activation Failure Design

## Problem

Session activation failures are currently published through two unrelated paths:

- `SessionActivationSnapshot` owns the activation identity, version, phase, and reason.
- `ViewModelBase.ErrorMessage` owns the user-visible failure text without a conversation identity.

This split permits a terminal failure from conversation A to remain visible after conversation B becomes the active transcript owner. Runtime evidence from 2026-07-17 shows conversation `30e79485a4b8431facf3547a95c8f3dc` publishing `MissingRemoteSessionId`, followed by conversation `b51f9ddfafe24994804a18791eed1751` becoming active and continuing authoritative hydration. The transcript router did not write B's updates into A: `session/update` resolves its target from the event remote session ID, and A has no remote session ID. The defect is failure-presentation ownership, not transcript persistence or routing.

## Goals

- Make one authoritative snapshot own both activation identity and its user-visible failure.
- Show an activation failure only when its conversation is the currently projected conversation.
- Preserve latest-intent semantics: a stale activation cannot publish or display a failure.
- Preserve remote transcript routing and background hydration behavior unchanged.
- Cover main chat and mini chat through the same ViewModel projection.
- Preserve non-activation operation errors without allowing them to cross conversation boundaries.

## Non-goals

- Recovering a missing historical remote session ID.
- Changing `session/list`, `session/load`, or `session/update` protocol behavior.
- Cancelling or deduplicating background hydration. That requires a separate request/transport audit.
- Redesigning connection-state, turn-failure, ask-user, or new-session-draft error owners.

## Architecture

### Authoritative owner

Extend `SessionActivationSnapshot` with an optional `FailureMessage`. The snapshot then atomically represents:

- `SessionId`
- `ProjectId`
- activation `Version`
- activation `Phase`
- diagnostic `Reason`
- user-visible `FailureMessage`

`ConversationActivationOutcomePublisher` publishes a terminal failure in one operation. It validates chat-shell ownership, latest activation version, and matching active snapshot before replacing the snapshot with `Phase = Faulted`, its reason, and message. It no longer writes activation failures to `ViewModelBase.ErrorMessage`.

The dispatcher callback repeats snapshot owner and version validation immediately before mutation. A failure that was current when queued but superseded before callback execution is discarded.

### ViewModel projection

`ChatViewModel` exposes dedicated read-only properties:

- `SessionActivationFailureMessage`
- `HasSessionActivationFailure`

The message is non-empty only when all conditions hold:

1. `ActiveSessionActivation` exists.
2. Its phase is `Faulted`.
3. Its `SessionId` equals `CurrentSessionId`.
4. Its `FailureMessage` is non-empty.

Changes to either `CurrentSessionId` or `ActiveSessionActivation` notify both properties. A new activation naturally replaces the old snapshot, so no clearing timer, delayed callback, or selection-side cleanup is required.

### Conversation operation errors

Non-activation failures remain a separate domain. `ChatViewModel` owns one ephemeral `ConversationOperationFailure` containing `ConversationId` and `Message`; it cannot change activation phase or reason. Chat commands, manually initiated hydration, reconnect attempts, and ACP `ErrorOccurred` events publish through owner-aware methods instead of inherited `SetError/ClearError`.

The operation error is visible only when its non-empty `ConversationId` equals `CurrentSessionId`. An error without a conversation owner is visible only while no conversation is active. Clearing requires a matching owner, so an older completion cannot clear a newer conversation's failure.

The owner is captured immutably at the producer boundary, never rediscovered in a later `catch` or completion callback:

- Commands and manual hydration capture `CurrentSessionId` before their first `await`.
- Reconnect captures the conversation owner when the attempt begins.
- ACP `ErrorOccurred` captures the active conversation owner when the event is received.
- Late failure and clear callbacks reuse that captured owner. They cannot attach A's outcome to B merely because selection changed while A was pending.

`CreateNewSessionAsync` is the one staged exception because its authoritative local conversation ID does not exist at operation start. Its initial owner is the conversation that was current when the command started, which may be null. Once `CreateAndActivateLocalConversationAsync` returns the authoritative new ID, the operation explicitly transfers ownership exactly once from that captured start owner to the returned ID. Failures before creation belong to the start context; failures after creation belong to the returned ID. Selection state is never reread or used as the transfer signal.

`ChatViewModel` exposes `ConversationOperationFailureMessage` and `HasConversationOperationFailure`. This state is not persisted and does not participate in protocol routing.

### UI projection

The session-activation failure callouts in `ChatView.xaml` and `MiniChatView.xaml` bind only to the dedicated activation-failure properties. A separate conversation-operation failure callout binds the operation-error properties. Neither surface binds chat errors to the inherited global `HasError/ErrorMessage` pair.

### Data-flow invariants

- Transcript updates remain keyed by remote session ID and stored in the resolved conversation content slice.
- Selecting a conversation continues to project only that conversation's content slice.
- A missing-binding conversation cannot receive another remote session's updates.
- Activation failure visibility is derived from snapshot owner equality, never from temporal clearing.

## Error and concurrency semantics

- Failure publication is atomic on the UI dispatcher.
- Stale versions and non-chat pending navigation are discarded before mutation.
- A failure cannot overwrite a newer activation snapshot or a snapshot owned by another conversation.
- A queued dispatcher callback cannot commit after its snapshot/version is superseded.
- A non-fault phase cannot overwrite a terminal fault for the same activation.
- The diagnostic `Reason` remains stable and separate from localized/user-visible text.
- A conversation operation error cannot display in or be cleared by another conversation.
- An asynchronous producer cannot determine error ownership from `CurrentSessionId` after its first asynchronous boundary.

## Testing

1. Publisher unit tests prove a current failure stores phase, reason, and message atomically.
2. Publisher unit tests prove stale and mismatched conversation failures do not mutate the snapshot.
3. ViewModel regression test reproduces the incident: A fails for missing remote binding, B becomes current with a valid transcript, and A's failure is not visible on B.
4. ViewModel test proves the failure is visible while A is current.
5. Property-change tests cover snapshot changes and current-conversation changes.
6. Dispatcher race tests prove a queued stale failure cannot commit after supersession.
7. Operation-error tests prove command/manual-hydration failures remain visible only for their owner.
8. Operation-error race tests prove A pending, selection switching to B, and A's late failure/clear cannot display in B or clear B's error.
9. ACP `ErrorOccurred`, reconnect, commands, and manual hydration retain user-visible errors through the operation owner.
10. New-session tests prove pre-create failure belongs to the captured existing conversation when present, remains ownerless when started without one, and transfers exactly once to the authoritative created conversation ID.
11. XAML contract tests require both chat surfaces to bind both dedicated projections and reject the old global bindings.
12. Existing session-update routing and reducer tests remain unchanged and pass.

## Verification

- Targeted Presentation.Core tests for publisher, ChatViewModel activation semantics, reducer/router, and XAML contracts.
- Full `SalmonEgg.Presentation.Core.Tests` suite.
- Build the affected Presentation.Core and application targets according to `BUILD_GUIDE.md`.
- Run the Windows GUI smoke that verifies the activation failure callout across rapid conversation switching if the existing harness supports the scenario; otherwise record the missing GUI fixture explicitly.
