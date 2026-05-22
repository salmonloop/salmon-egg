# Input State Selector Projection Design

## Goal

Unify the input-area state-machine rules for SalmonEgg composers while keeping the implementation layered and testable.

The unified specification covers the full Start composer, the regular Chat composer subset, and MiniChat as an explicit future validation target. The first implementation phase should land only the Start and regular Chat behavior unless the implementation plan later proves MiniChat can be included without widening risk.

## Scope

In scope:

- Send-action input locking from user send intent until prompt dispatch succeeds or fails.
- Voice input listening, live partial transcription, and stop-as-escape behavior.
- ACP new-session draft loading for mode/config metadata.
- Selector placeholder handling for mode, agent, and project.
- Regular Chat page behavior as a subset of Start page capabilities.
- Unit, integration, XAML contract, and GUI smoke coverage.

Out of scope for the first implementation phase:

- Reworking the ACP protocol layer.
- Replacing native ComboBox behavior.
- Implementing MiniChat changes unless they fall out as a small, isolated projection consumer.
- Showing detailed exception text inside selector items.

## Existing Facts

Current code already has separate state owners:

- `ChatInputStatePresenter` owns global composer interaction state for text input, send, cancel, voice, slash commands, and blocked surfaces.
- `StartSessionModePolicy` owns Start-page mode readiness during ACP `session/new` draft loading.
- `StartViewModel` owns Start-page launch/submitting state and delegates to the shared `ChatInputArea`.

The regular Chat page exposes fewer interactive slots than Start. It is not a separate state-machine category; it is a subset configuration of the same rules.

## Architecture

Use a layered state-machine design.

`ChatInputStatePresenter` remains the global composer-interaction presenter. It should continue to decide:

- text input enabled
- composer tools enabled
- send/cancel enabled and visible
- voice start/stop enabled and visible
- slash-command visibility eligibility

Add a selector projection layer:

- `SelectorProjectionPresenter`: shared projection engine.
- `ModeSelectorPolicy`: ACP `session/new`, config-options, and mode-authority rules.
- `AgentSelectorPolicy`: local agent list plus connection and selected-profile intent rules.
- `ProjectSelectorPolicy`: local project catalog, pending project intent, and `Unclassified` fallback rules.

The selector layer outputs display models only. Placeholder items must not be inserted into the original business collections for modes, agents, or projects.

Start uses the full selector set:

- agent
- mode
- project

Regular Chat uses only the supported subset, normally:

- mode

Missing slots must not create phantom placeholders or submit-blocking reasons.

MiniChat must be included in the spec and validation matrix as a future consumer of the same projection model.

## Selector Projection Model

Each selector policy receives:

- selector kind: mode, agent, or project
- raw business items
- selected semantic value
- current intent identity
- authoritative/loading/error/fallback state
- optional previous display context

The shared presenter returns:

- `DisplayItems`
- `SelectedDisplayItem`
- `IsEnabled`
- `CanCommitSelection`
- `PlaceholderKind`
- `SubmitBlockReason`

Each display item must carry:

- display text resource key or resolved text
- semantic value, if it represents a real selectable value
- placeholder kind, if it is a placeholder
- enabled/selectable flags
- identity/version metadata needed to reject stale selection

## Placeholder Kinds

Blocking placeholder kinds:

- `Loading`
- `Error`
- `Unresolved`

Non-blocking placeholder kinds:

- `Default`
- `Fallback`
- `EmptyNonBlocking`

Non-blocking placeholders may be selected or submitted only when they map to an explicit semantic value. Project `Unclassified` is a valid semantic value and is not a blocking placeholder.

Pure visual placeholders must never be written back as business values.

## Placeholder Display Rules

Mode selector:

- If mode data is not authoritative, current display switches to a placeholder.
- Old mode options must not look selected while a new draft is loading or faulted.
- Loading, error, and unresolved mode placeholders block send.
- `session/new` success with no mode/config capability is allowed: display a default or no-options placeholder, disable the mode selector, and allow send.
- If the agent declares mode/config capability but returns empty, invalid, or unparseable mode state, display an error/unresolved placeholder and block send.

Agent selector:

- Agent list comes from local configuration.
- During connection or selected-profile switching, existing local agent items may remain visible.
- The dropdown should include a generic status placeholder at the top when the current agent intent is not confirmed.
- Loading, error, and unresolved agent placeholders block send when the send path depends on the unconfirmed agent.

Project selector:

- Project list comes from the local project catalog.
- `Unclassified` is a legal fallback and does not block send.
- If a pending project intent cannot resolve and no legal fallback is available, display an unresolved placeholder and block send.
- Empty local project catalog should still display a clear fallback or empty placeholder; the selector must not appear blank.

All selector placeholder text must be generic. Detailed exception text, profile ids, URLs, paths, and raw protocol errors belong in InfoBar, toast, or existing error surfaces, not inside ComboBox items.

## State Priority

State priority is:

1. Voice stop escape
2. Submitting whole-composer lock
3. Remote selector placeholders
4. Local fallback placeholders

Voice stop escape:

- Stop voice is the highest-priority user escape action.
- It must stay available while voice is actively listening, even if transport is busy or selectors are loading/error.
- Stop should release the visible listening state immediately.
- While the stop transport is still completing, voice restart remains blocked.

Submitting whole-composer lock:

- Starts when the user triggers send.
- Ends when this prompt dispatch request succeeds or fails.
- It does not wait for the agent's final reply.
- If a remote session must be created as part of send, that creation belongs to the lock.
- In the normal Start flow, draft `session/new` must already be ready before send is allowed, because mode/config data depends on that result.

Selector projection continues updating while the composer is locked. The lock only blocks interaction. When the lock ends, the UI shows the latest selector projection.

## Identity And Stale Results

Async correctness must not depend on cancellation alone.

Remote selector state must carry identity sufficient to reject stale data:

- profile id
- connection instance id
- cwd or project intent
- draft version or equivalent request version

The store/action layer should reject stale results before they become authoritative state.

The selector presenter must also compare the projected state with the current intent identity. Mismatched state can only produce placeholder output; it cannot produce current real options or selected values.

Selection commands must receive semantic value plus identity metadata. ViewModel handlers must reject selections whose identity no longer matches current state.

## Open Dropdown Behavior

Do not force-close native ComboBox dropdowns from code-behind.

When state changes while a dropdown is open:

- projection replaces display items atomically
- stale real items become invalid
- selection commands perform identity checks
- selected display must switch to a placeholder if the current real value is no longer authoritative
- visual state must never become blank

This preserves native WinUI/Uno behavior and keeps correctness in the ViewModel/projection layer.

## Regular Chat As Start Subset

Regular Chat must be tested as a selector-slot subset of Start.

Requirements:

- no agent/project placeholders when those slots are not present
- no extra submit blocking caused by missing Start-only selector slots
- mode placeholder and send/voice/cancel rules still follow the same presenter and policy contracts
- open dropdown and stale mode behavior still apply

This prevents Start-specific assumptions from becoming hidden requirements in the shared composer.

## Testing Plan

Unit and policy tests:

- `ModeSelectorPolicy`: loading, error, unresolved, default/no-capability, config-empty, invalid config, stale identity.
- `AgentSelectorPolicy`: empty local list, selected profile missing, connecting, connection error, profile switch.
- `ProjectSelectorPolicy`: empty catalog, `Unclassified` fallback, pending intent resolved/unresolved, deleted project.
- `SelectorProjectionPresenter`: placeholder kind, selected display, submit block reason, selectable semantic value, stale selection identity.

ViewModel integration tests:

- Start full matrix: agent, mode, and project projections interact with send eligibility.
- Start mode loading/error blocks send while text input can remain logically editable unless the whole composer is locked by submit.
- Project `Unclassified` does not block send.
- Send whole-composer lock blocks all Start input slots until dispatch success/failure.
- Regular Chat subset matrix: missing agent/project slots do not create phantom placeholders or submit blocks.
- Stale `session/new` result after agent/project switch cannot restore old mode options or old errors.
- Voice stop remains available in selector loading/error/disabled states and releases listening UI immediately.

XAML contract tests:

- Shared `ChatInputArea` binds selector display items and selected display projection, not raw business collections when placeholder support is active.
- Start passes complete selector projections.
- Chat passes mode-only projection.
- View does not synthesize placeholder strings or infer selector state.

GUI smoke tests:

- Start mode loading and error placeholders are visible in the current ComboBox display and are not blank.
- Agent and project dropdowns show top status placeholders when their current state requires one.
- Regular Chat has no agent/project placeholder remnants.
- Dropdown remains stable when state changes to stale/loading/error while open.
- Stale items cannot be submitted after projection identity changes.
- Voice stop remains clickable during selector loading/error states.
- Send-stage whole-composer lock disables all input slots and restores after prompt dispatch success/failure, not after the agent's final reply.

## Implementation Phasing

Phase 1:

- Define selector projection contracts and policies.
- Wire Start and regular Chat.
- Add policy, ViewModel, XAML contract, and GUI smoke coverage.

Phase 2:

- Apply the same projection contracts to MiniChat if it still has independent selector/input state.
- Add MiniChat GUI smoke coverage or explicitly document why it remains out of scope.

## Risks

- Over-generalizing the selector presenter could hide mode-specific ACP rules. Keep the shared presenter mechanical and put selector-specific semantics in policies.
- GUI smoke tests can become brittle if they assert raw localized strings too deeply. Prefer automation ids, placeholder kind exposure, and high-level visible text checks.
- Placeholder display must not regress native ComboBox behavior. Do not add code-behind close/reopen logic.

## Acceptance Criteria

- Start and regular Chat both consume the same selector projection model.
- Start full matrix and Chat subset matrix are covered by automated tests.
- No placeholder item enters protocol, agent config, or project catalog business collections.
- Loading/error/unresolved placeholders are visible and block send.
- Legal fallback placeholders with semantic values can be selected/submitted.
- Stale async results cannot overwrite current selector state.
- GUI smoke verifies visible placeholders and send/voice interaction behavior on the real app surface.
