# ACP Multi-Connection Chat Owner Fix Design

## Background

Recent ACP connection SSOT convergence work reduced several routing fallbacks, but follow-up audit exposed three remaining user-visible failures:

1. Explicit profile connect from Settings can still be interpreted as a conversation-preserving connect, creating cross-profile ownership drift.
2. A previously loaded remote session can degrade from hot return to slow `session/load` after switching away and back, even when the original connection is still reusable.
3. Cached chat content can become visible before the blocking chat skeleton/overlay owns the screen during session switching.

The product requirement is that multiple ACP connections may coexist. A manual connect in Settings is an explicit connect for one ACP server/profile only. It must not take ownership of the currently visible chat conversation, must not rewrite the active chat page, and must not change the conversation that the user is currently viewing unless the user explicitly switches conversations.

## Goals

- Preserve true multi-connection behavior: pool connections may coexist by `profileId`.
- Ensure only the conversation activation chain owns active chat conversation state.
- Prevent Settings explicit connect from preserving or rewriting the currently visible chat conversation.
- Preserve hot return for previously hydrated remote sessions when conversation identity and connection identity still match.
- Guarantee that blocking overlay/skeleton appears before stale or cached conversation content can leak during a visible switch.

## Non-Goals

- No new cross-profile live unread fan-in feature.
- No ACP protocol feature expansion such as `session/resume`.
- No unrelated UI redesign.
- No broad refactor of the entire ACP transport stack beyond the ownership boundaries needed for this fix.

## Product Semantics To Preserve

1. Multiple ACP profile connections may coexist in the pool.
2. Settings explicit connect is a pool-level operation, not a chat-page ownership operation.
3. The chat page remains owned by the user’s current conversation activation intent.
4. A later background/pool connection establishment must never rewrite the page the user is currently on.

## Authoritative Ownership Model

The fix formalizes three distinct state classes:

### 1. Connection Pool State

Owned by profile-level ACP connection services.

Responsibilities:

- Whether a profile is connected, connecting, disconnected, or reusable.
- Which connection instance belongs to which `profileId`.
- Pool retention, reuse, and cleanup.

Non-responsibilities:

- Deciding which conversation is visible.
- Marking a conversation hydrated or warm.
- Routing visible chat projection.

### 2. Conversation Runtime State

Owned only by the conversation activation chain:

`INavigationCoordinator -> IConversationSessionSwitcher -> conversation activation / hydration flow`

Responsibilities:

- The active conversation’s activation phase.
- The conversation’s `remoteSessionId`.
- The conversation’s `ConnectionInstanceId`.
- Whether that conversation is `Warm`, `Hydrating`, `Faulted`, or otherwise.

Non-responsibilities:

- Profile pool lifecycle unrelated to the active conversation.

### 3. Chat Visible Projection

Owned by UI projection from authoritative state.

Responsibilities:

- Which header/transcript/input surface is shown.
- When blocking overlay/skeleton is visible.
- Preventing stale content exposure during visible switches.

Non-responsibilities:

- Deriving authority from “a connection was just established”.

## Required Behavioral Contracts

### Contract A: Settings Explicit Connect Is Pool-Only

When Settings explicitly connects `profile B` while Chat is showing `conv-A` bound to `profile A`:

- `profile B` may connect and become reusable in the pool.
- `conv-A` remains the active chat owner.
- `conv-A` transcript, header, `remoteSessionId`, hydration phase, warm/runtime state, and overlay owner must remain unchanged.
- No code in this path may read `CurrentSessionId` to infer a preserve target.
- No code in this path may generate a conversation-preserving ACP connection context.

### Contract B: Pool Events Never Own Chat Conversations

Events such as:

- connect success
- connect failure
- connection reuse
- service replacement
- pool cleanup

may update pool/profile state, but they must not directly:

- clear unrelated conversation runtime state
- mark conversations hydrated or warm
- rewrite the currently visible conversation
- change overlay ownership
- change `remoteSessionId -> conversationId` routing for the current page

### Contract C: Warm Return Requires Exact Identity Match

Hot return is allowed only when all are true:

- the conversation remains bound to the same `remoteSessionId`
- the conversation runtime is still `Warm`
- the runtime `ConnectionInstanceId` exactly matches the actual owner connection instance

If any identity component differs, remote hydration may run. But identity mismatch must result from authoritative owner logic, not from an unrelated background connect path or pool-side replacement.

### Contract D: Overlay Must Be Monotonic During Switch

Once latest intent points to a new conversation while old conversation content is still at risk of being visible:

- blocking overlay/skeleton must already own the screen
- stale content may not appear before overlay ownership
- UI may progress from more-blocking to less-blocking
- UI may not regress from visible stale content to delayed blocking overlay

## Design Changes

### Change 1: Introduce A Pool-Only Explicit Connect Path

The current Settings explicit connect path must stop reusing the conversation-preserving connect API shape.

Design requirement:

- Add a dedicated explicit-profile-connect command surface for pool-only connects.
- This path must operate without conversation ownership context.
- It must not call logic that marks the active conversation hydrated, preserved, or resynced.

Expected effect:

- Settings can explicitly bring up or warm a profile connection.
- The active chat conversation remains unchanged unless the user activates a conversation bound to that profile.

### Change 2: Stop Global Runtime Resets On Non-Owner Service Replacement

`ReplaceChatServiceAsync` or equivalent service-swap paths must no longer unconditionally reset all conversation runtime state.

Design requirement:

- Conversation runtime updates must be scoped to the owner conversation activation flow.
- Background profile connect, pooled reuse, or non-owner service replacement must not clear unrelated runtime slices.
- Runtime invalidation should be targeted and identity-driven, never global by default.

Expected effect:

- A previously warmed conversation can still satisfy hot return after unrelated pool activity.
- Warm SSOT no longer drifts simply because another profile connected or another service became active in the pool.

### Change 3: Promote Blocking Overlay Earlier Than Visible Content Switch

Current layout-loading and activation-overlay contracts must be unified around visible stale-content risk.

Design requirement:

- If latest intent points to another conversation and stale header/transcript could still be shown, the blocking presenter must already be visible.
- Layout loading alone is insufficient if it does not surface the actual presenter/skeleton.
- Session-switch preview and hydration overlay rules must be monotonic for user-visible transitions.

Expected effect:

- No cached or stale chat content leaks before the skeleton.
- Remote warm-cache switches retain native-feeling continuity while still hiding stale visible content.

## Concurrency Rules

1. Latest owner wins.
   Only the latest conversation activation may advance conversation runtime for the visible session.

2. Pool event is never conversation authority.
   Background connect completion cannot rewrite the chat page the user is on.

3. Warm reuse is identity-based, not timing-based.
   Reuse decisions must be derived from `conversationId + remoteSessionId + ConnectionInstanceId`.

4. Visible switch protection is monotonic.
   Once stale visible content is possible, overlay ownership must not arrive late.

## Error Handling Rules

- Settings explicit connect failure affects only the target profile’s connection status.
- Pool disconnect/failure for a non-active profile must not fault the current visible conversation page.
- If hot return falls back to slow hydration due to identity mismatch, logs must include:
  - `ConversationId`
  - `RemoteSessionId`
  - `ExpectedConnectionInstanceId`
  - `ActualConnectionInstanceId`
  - `Reason`

## File-Level Implementation Boundary

Likely touch points:

- `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/SettingsChatConnectionAdapter.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpChatCoordinator.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- supporting ACP command interfaces/seams used by the above
- related Core / Presentation tests
- GUI smoke covering hot return and overlay ordering

This fix must stay scoped to ownership, runtime invalidation, and overlay gating. No unrelated refactors should be bundled.

## Testing Strategy

### Core Tests

Add or update tests that prove:

- Settings explicit connect for `profile B` does not create a preserve-conversation context from the current chat page.
- Background connect/reuse/replace does not clear unrelated conversation runtime slices.
- Only explicit owner supersession can invalidate or replace conversation runtime state.
- Warm return skips `session/load` only on exact identity match.
- A later non-owner connection success cannot change visible conversation or overlay owner.

### Presentation / ViewModel Tests

Add or update tests that prove:

- When Chat shows `conv-A(profile-A)`, explicitly connecting `profile-B` leaves header, transcript, runtime phase, and current visible conversation unchanged.
- `conv-A -> conv-B -> conv-A` returns hot when A’s warm identity still matches.
- If latest intent changed while old transcript could still be visible, blocking overlay appears before stale content can leak.
- A later connection completion cannot back-write the current page to a different session state.

### GUI Smoke

Keep and extend GUI smoke coverage:

- existing `A-B-A` hot return smoke remains mandatory
- add Settings explicit connect of another profile while viewing an active remote conversation; current chat page must not jump, flash, or swap header/transcript
- add a visible-switch smoke ensuring skeleton appears before stale or cached content on remote warm-cache activation

## Acceptance Criteria

The fix is accepted only if all are true:

1. Explicit Settings connect for another profile never changes the conversation page the user is currently viewing.
2. Later background connection completion never back-writes the current visible chat page.
3. Hot return does not trigger extra `session/load` when conversation and connection identity still match.
4. No stale/cached transcript or header becomes visible before blocking overlay ownership during a session switch.
5. Logging remains sufficient to explain any fallback from hot return to slow hydration.

## Risks

- The current codebase has overlapping “connection established” and “conversation activated” assumptions; missing one edge path could leave partial owner drift.
- Overlay behavior is sensitive to ordering and UI-thread projection timing; tests must verify order, not just final state.
- Dirty worktree context means implementation must avoid unintentionally coupling this fix to unrelated pending ACP changes.

## Out-Of-Scope Follow-Ups

- Full explicit multi-connection event aggregation across profiles for future background awareness.
- Broader ACP lifecycle simplification beyond the ownership seams covered here.
