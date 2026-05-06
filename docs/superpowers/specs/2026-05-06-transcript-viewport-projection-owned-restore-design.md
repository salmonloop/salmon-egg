# Transcript Viewport Projection-Owned Restore Design

Date: 2026-05-06
Repo: `C:\Users\shang\Project\salmon-acp`
Scope: Replace view-local warm-return restore heuristics with a projection-owned restore contract so detached transcript reading position survives warm return without breaking virtualization or native WinUI/Uno behavior
Status: Approved design (implementation not started)

## 1. Problem Statement

The transcript viewport refactor already corrected one important ownership problem: follow versus detach semantics are now modeled as conversation-scoped policy instead of page-local transient state.

That fix is necessary, but it is not sufficient.

The remaining warm-return regression shows that detached state can survive while reading-position restore still fails. In the current shape:

1. `TranscriptViewportCoordinator` can correctly preserve `DetachedByUser`,
2. `ChatView` or `MiniChatView` can attempt a bounded restore,
3. but the restore target is still inferred from view-local geometry and activation timing instead of an authoritative projection contract.

This produces the current failure mode:

1. state says the conversation is detached,
2. warm return does not reattach auto-follow,
3. but the viewport still lands in the wrong reading region because restore is not anchored to stable transcript projection identity.

The next design step must therefore fix restore ownership, not re-open the already-corrected detach policy boundary.

## 2. Design Goals

1. Move reading-position restore from view-local geometry heuristics to a projection-owned restore contract.
2. Preserve the existing conversation-scoped `Following`, `DetachedByUser`, and `BootstrapSettling` policy ownership in `TranscriptViewportCoordinator`.
3. Ensure `A -> B -> A` warm return keeps detached conversations detached and restores the prior reading region as closely as practical.
4. Make restore depend on projection readiness instead of firing directly from session activation timing.
5. Preserve native WinUI/Uno virtualization, avoid synchronous layout forcing, and keep warm return smooth.
6. Leave a clean extension path for future higher-fidelity restore without reworking the ownership model again.

## 3. Non-Goals

1. No ACP protocol changes.
2. No redesign of the broader remote hydration or replay ownership in `ChatViewModel`.
3. No pixel-perfect restoration requirement in this phase.
4. No general-purpose UI restore framework beyond transcript viewport restore.
5. No virtualization disablement, full list materialization, or synchronous layout hacks.

## 4. Core Design Decision

The system should adopt a projection-owned restore token.

This means:

1. transcript projection owns the identity of the reading point,
2. viewport policy owns whether restore should happen,
3. View adapters own only execution and fact reporting.

The design intentionally does not keep restore ownership in `ChatView` / `MiniChatView`, because the failing behavior proves that view-local geometry and activation timing are not authoritative enough.

## 5. Ownership Boundaries

### 5.1 `TranscriptViewportCoordinator`

`TranscriptViewportCoordinator` remains in `Presentation.Core` and remains the owner of viewport policy.

Its responsibilities are:

1. maintain conversation-scoped mode such as `Following`, `DetachedByUser`, and `BootstrapSettling`,
2. maintain restore sub-state such as pending or active restore transactions,
3. decide whether the current conversation should do nothing, follow bottom, or request restore,
4. react to restore lifecycle outcomes.

It must not own:

1. transcript item identity generation,
2. projection readiness detection,
3. geometry-specific restore mechanics.

### 5.2 Transcript Projection

Transcript projection becomes the owner of reading-point identity and restore eligibility.

Its responsibilities are:

1. generate stable `ProjectionItemKey` values,
2. build `TranscriptProjectionRestoreToken` instances,
3. expose projection epoch and restore-ready facts for the active conversation.

### 5.3 View Adapters

`ChatView` and `MiniChatView` become thin restore executors.

Their responsibilities are:

1. publish viewport facts,
2. publish explicit attach and detach facts,
3. execute bottom-follow commands,
4. execute restore requests against the currently projected list,
5. report restore outcomes back to the coordinator.

They must not own:

1. restore identity semantics,
2. the decision that restore should happen,
3. the interpretation of non-bottom geometry as a policy change.

## 6. New Restore Contract

### 6.1 `TranscriptProjectionRestoreToken`

The new authoritative restore token should contain at least:

1. `ConversationId`
2. `ProjectionEpoch`
3. `ProjectionItemKey`
4. `OffsetHint`

Semantics:

1. `ProjectionItemKey` is the primary identity anchor.
2. `OffsetHint` is only a local refinement hint.
3. `ProjectionEpoch` does not define identity. It defines whether the current projection is new and ready enough for restore.

### 6.2 `ProjectionItemKey`

`ProjectionItemKey` must be projection-generated and must remain stable for the same semantic transcript item across:

1. appended replay or transcript growth,
2. container recycle and re-materialization,
3. warm return projection rebuilds.

`ProjectionItemKey` must not be synthesized in the View from:

1. current item index,
2. current visible order,
3. transient text matching,
4. geometry-only offsets.

### 6.3 `OffsetHint`

`OffsetHint` is explicitly secondary.

It exists only to refine the visual region after identity-based positioning succeeds. It must not become the primary restore key, because offset-only restore is not stable across:

1. item height changes,
2. markdown or tool-call expansion,
3. window-size changes,
4. Main versus Mini view differences.

## 7. Projection Readiness

Restore must not be triggered solely because `SessionActivated(WarmReturn)` occurred.

Instead, transcript projection must expose a readiness fact that is intentionally narrow:

1. the active conversation projection exists,
2. its current `ProjectionEpoch` is known,
3. the target token can be resolved against the current projected item set,
4. restore can be attempted without forcing full materialization.

This is not a global "everything is ready" gate. It is a restore-specific readiness fact.

## 8. Restore Lifecycle Protocol

### 8.1 Command Shape

`TranscriptViewportCoordinator` should stop issuing a view-local `RestoreAnchor` concept.

It should instead issue `RequestRestore(token)`.

That command means:

"restore this detached conversation to the reading region represented by this projection-owned token."

### 8.2 Restore Events

The protocol should support the following lifecycle facts:

1. `ProjectionReady(conversationId, projectionEpoch)`
2. `RestoreDispatched(conversationId, token)`
3. `RestoreConfirmed(conversationId, token)`
4. `RestoreUnavailable(conversationId, reason)`
5. `RestoreAbandoned(conversationId, reason)`

`ProjectionReady` comes from projection.

`RestoreDispatched`, `RestoreConfirmed`, `RestoreUnavailable`, and `RestoreAbandoned` come from the View adapter executing the request.

### 8.3 Restore Sub-States

Keep the existing primary viewport modes, but add explicit restore sub-state to the conversation-scoped coordinator model:

1. `DetachedByUser`
2. `DetachedPendingRestore`
3. `DetachedRestoring`

Semantics:

1. `DetachedByUser` means the user remains detached and no restore transaction is active.
2. `DetachedPendingRestore` means a restore token exists but projection or execution preconditions are not ready yet.
3. `DetachedRestoring` means restore has been dispatched and the system is waiting for explicit outcome.

### 8.4 State Transitions

Recommended transitions:

1. `WarmReturn + DetachedByUser + token available + projection not ready`
   - transition to `DetachedPendingRestore`
2. `ProjectionReady + pending token`
   - dispatch `RequestRestore(token)`
   - transition to `DetachedRestoring`
3. `RestoreConfirmed`
   - transition back to `DetachedByUser`
   - do not transition to `Following`
4. `RestoreUnavailable` or `RestoreAbandoned`
   - transition back to `DetachedByUser`
   - keep detached semantics
   - do not reclaim bottom

The key rule is that restore completion never implies renewed consent to auto-follow.

## 9. Interaction with Virtualization

The design must remain virtualization-safe.

### 9.1 Identity-First Restore

Restore should happen in phases:

1. coarse positioning by `ProjectionItemKey`,
2. one lightweight refinement using `OffsetHint`,
3. explicit confirmation.

This keeps the algorithm compatible with virtualized lists because it relies on logical item identity instead of requiring full container realization.

### 9.2 Prohibited Techniques

The implementation must not:

1. call synchronous `UpdateLayout()` to force readiness,
2. disable virtualization,
3. fully materialize the transcript,
4. chase layout indefinitely with unbounded retries,
5. treat absolute scroll offset as authoritative restore state.

### 9.3 Allowed Recovery Budget

The implementation may:

1. perform one coarse positioning attempt,
2. perform one bounded refinement after materialization,
3. remain pending while waiting for a real readiness signal,
4. abandon restore while preserving detached semantics if conditions never become valid.

## 10. Expected A -> B -> A Behavior

### 10.1 Detach in A

When the user detaches in session A:

1. A enters `DetachedByUser`,
2. transcript projection publishes a restore token for the current reading point,
3. the coordinator stores detached policy state plus the latest token reference.

### 10.2 Switch to B

When the user switches to B:

1. A's detached state and restore token remain conversation-scoped,
2. B activates based on B's own state,
3. B must not overwrite A's restore ownership.

### 10.3 Background Growth in A

If A grows in the background:

1. detached semantics remain intact,
2. A must not reattach to bottom,
3. projection may advance epoch and keep the token resolvable against the new projection.

### 10.4 Return to A

On warm return to A:

1. the coordinator restores detached mode first,
2. the coordinator waits for projection readiness,
3. the coordinator dispatches `RequestRestore(token)` only after readiness,
4. if restore succeeds, A returns near the prior reading region,
5. if restore cannot be completed, A stays detached and still does not auto-follow.

## 11. File-Level Impact

### 11.1 Core

Primary files:

1. `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs`
2. `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportContracts.cs`
3. `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportConversationState.cs`

Required changes:

1. add restore sub-state and lifecycle handling,
2. replace `RestoreAnchor`-style command semantics with `RequestRestore(token)`,
3. keep token identity and restore transaction facts in Core state without adding View geometry concerns.

### 11.2 Projection

Projection owners must add:

1. stable `ProjectionItemKey` generation,
2. `TranscriptProjectionRestoreToken` publication,
3. projection-ready and epoch facts consumable by viewport restore.

This is the architectural center of the new design.

### 11.3 View Adapters

Primary files:

1. `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs`
2. `SalmonEgg/SalmonEgg/Presentation/Views/MiniWindow/MiniChatView.xaml.cs`

Required changes:

1. remove view-owned restore transaction semantics as the source of truth,
2. execute restore requests against the currently projected list,
3. report explicit restore outcomes back to Core,
4. keep attach and detach input capture, but stop owning restore identity meaning.

## 12. Testing Strategy

### 12.1 Core Tests

Add or update tests to cover at least:

1. detached warm return with token present but projection not ready enters `DetachedPendingRestore`,
2. `ProjectionReady` dispatches `RequestRestore(token)`,
3. `RestoreConfirmed` keeps the conversation detached,
4. `RestoreUnavailable` and `RestoreAbandoned` keep the conversation detached and do not reclaim bottom,
5. stale conversation, stale generation, and stale token events are ignored.

### 12.2 Projection-Facing Tests

Add focused tests for:

1. `ProjectionItemKey` stability across append,
2. restore-token continuity across warm return projection rebuild,
3. readiness signals not firing before the token can actually be resolved.

### 12.3 GUI Smoke

The primary smoke path should be:

1. detach in session A,
2. switch to B,
3. allow background replay growth in A,
4. switch back to A,
5. verify no bottom reclaim,
6. verify return to the prior reading region or bounded nearby region,
7. verify failure to restore still preserves detached semantics.

Longer-term, GUI probes should evolve toward projection-backed restore verification rather than relying only on visible replay text heuristics.

## 13. Risks and Mitigations

1. Risk: projection identity is not stable enough across current replay rebuild paths.
   Mitigation: make `ProjectionItemKey` stability an explicit contract and test target before wiring restore policy onto it.

2. Risk: readiness signals become too broad and delay visible activation.
   Mitigation: define readiness narrowly for restore eligibility, not full-page completeness.

3. Risk: implementation drifts back into View-side retries and geometry heuristics.
   Mitigation: keep `RequestRestore(token)` and explicit restore outcome events as the only supported restore protocol.

4. Risk: future exact-restore work reopens this design.
   Mitigation: reserve extensibility in `TranscriptProjectionRestoreToken` while keeping identity ownership unchanged.

## 14. Acceptance Criteria

The design is considered correctly implemented when:

1. detached conversations remain detached across warm return,
2. warm return restore is driven by projection readiness rather than raw activation timing,
3. restore operates on a projection-owned token rather than a view-local anchor snapshot,
4. restore failure never reclaims bottom,
5. virtualization remains intact and no synchronous layout forcing is introduced,
6. `ChatView` and `MiniChatView` share the same restore semantics.

## 15. Recommendation

Implement this as a focused second-stage architecture correction on top of the already-approved conversation-scoped follow/detach policy work.

Do not continue expanding the current view-local pending-anchor approach. It is useful as a prototype seam, but it is not the right long-term owner for reading-point restore.
