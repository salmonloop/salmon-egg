# Remote Session Project Affinity Design

## Background

Today, a session imported from Discover is grouped in navigation by its `cwd`, not by an explicit persisted `ProjectId`.

Current behavior:

1. Discover reads remote ACP `session/list` items, including `cwd`.
2. Import creates a local session using that remote `cwd`.
3. The conversation workspace persists `cwd`, `RemoteSessionId`, and `BoundProfileId`.
4. Navigation and other consumers classify the conversation into a project by matching `cwd` against configured local project roots.

This gives a useful default, but it has several UX and architecture problems:

- The rule is implicit and hard for users to understand.
- Remote and local paths can differ in real-world environments such as WSL, containers, SSH, or another machine.
- Classification logic is duplicated across features instead of being owned by one SSOT-friendly Core service.
- There is no first-class way for users to correct the inferred project while keeping remote metadata authoritative.

## Problem Statement

We need a project-affinity design for imported and remote-bound sessions that is:

- fast in the common path,
- understandable when automatic matching fails,
- resilient to remote/local path differences,
- consistent with SSOT and MVVM best practices,
- and maintainable as Discover, Navigation, Search, and future surfaces evolve.

## Design Goals

- Keep ACP agent metadata as the SSOT for remote session facts such as `title`, `cwd`, `updatedAt`, and `remoteSessionId`.
- Keep the client as the SSOT for local project definitions, local path mappings, and user-made project affinity overrides.
- Avoid interrupting high-frequency flows with unnecessary dialogs.
- Make the effective project assignment explainable and correctable.
- Centralize project-affinity resolution in one Core abstraction consumed by all ViewModels.

## Non-Goals

- We do not introduce a local persisted `ProjectId` as the authoritative source for all remote-bound sessions.
- We do not change ACP protocol behavior or require the remote agent to understand local projects.
- We do not block import on manual project selection in the normal case.

## Real User Scenarios

### Scenario 1: Same-machine agent

The remote session `cwd` already points into a locally configured project root.

Expected experience:

- Discover shows which project the session will belong to.
- Import completes without asking extra questions.
- The session appears under that project immediately.

### Scenario 2: Remote path differs from local path

The remote agent runs in WSL, a container, or another host, so the remote `cwd` does not directly match local project roots.

Expected experience:

- The app attempts path mapping first.
- If mapping succeeds, the session lands in the correct project with no interruption.
- If mapping fails, the session goes to an explicit fallback bucket and offers a clear correction action.

### Scenario 3: User wants a different grouping

The user wants a specific imported session to appear under a different local project than automatic matching would choose.

Expected experience:

- The user can override project affinity once.
- That override wins over automatic inference for that conversation.
- The override is stable across app restart and remote rehydration.

## Recommended Product Behavior

### Default behavior

Import should not open a project-selection dialog by default.

The system should resolve the effective project using:

`effectiveProject = localOverride ?? mappedProject(remoteCwd, profileMappings, localProjects)`

If no match is found, the session should land in `Unclassified`.

### Discover page

Each discoverable session row should show a lightweight project-affinity badge:

- matched project name,
- `Unclassified`,
- or `Needs mapping` if the client can determine that the path looks remote but cannot map it.

This keeps the import result predictable before the user clicks.

### Imported conversation surface

If the session cannot be classified with confidence, the chat page should show a non-blocking affordance such as `Associate with project`.

The correction path should be fast, local, and reversible.

### Escalation behavior

Only show a blocking selection dialog when all of the following are true:

1. multiple candidate projects exist,
2. automatic resolution confidence is low,
3. and the user cannot reasonably predict the result from the current UI.

This should be the exception, not the default flow.

## Architecture

### SSOT model

Remote SSOT:

- `remoteSessionId`
- remote `title`
- remote `description`
- remote `cwd`
- remote `updatedAt`

Local SSOT:

- configured projects and root paths
- per-profile remote-to-local path mappings
- optional per-conversation project-affinity override

Derived state:

- effective project affinity
- project-affinity explanation for UI
- whether the user should be prompted or shown a correction affordance

### New Core service

Introduce a single Core resolver, for example `IProjectAffinityResolver`.

Responsibilities:

- resolve effective project for a conversation or remote session preview,
- apply per-profile path mappings,
- perform project-root matching,
- respect user override precedence,
- return structured diagnostics for UI projection.

Suggested result model:

```csharp
public sealed record ProjectAffinityResolution(
    string EffectiveProjectId,
    ProjectAffinitySource Source,
    string? MatchedProjectId,
    string? OverrideProjectId,
    string? RemoteCwd,
    string? LocalResolvedPath,
    bool NeedsUserAttention,
    string Reason);
```

This keeps ViewModels declarative and avoids embedding classification logic in UI-facing types.

### Resolution order

1. Conversation-level local override.
2. Profile-level path mapping from remote path to local path.
3. Direct longest-prefix match between resolved path and local project roots.
4. `Unclassified` fallback.

### Why this is MVVM-friendly

- ViewModels consume one read model instead of reimplementing inference rules.
- Views only render projected state such as badge text, icons, and commands.
- Core owns the decision rule, so Search, Discover, Navigation, and future surfaces stay consistent.

## Data Model Changes

### Persisted conversation state

Keep persisting:

- `Cwd`
- `RemoteSessionId`
- `BoundProfileId`

Add optional persisted local override metadata for project affinity.

Suggested conversation-local field:

- `ProjectAffinityOverrideProjectId`

This is local UX state, not remote truth.

### Persisted profile state

Add per-profile remote path mappings.

Suggested shape:

- profile id
- remote root
- local root

These mappings should be owned by local preferences/settings, not by conversation history.

## UI Projection Rules

### Discover page

Page state remains focused on list loading and action lifecycle.

Per-row read model should include:

- remote title,
- remote description,
- remote `cwd`,
- resolved project badge,
- affinity status text,
- and whether the row needs user attention.

### Navigation

Navigation should group conversations exclusively by resolver output, never by ad hoc local logic.

This removes the current risk where different entry points classify the same conversation differently.

### Global search

Search should use the same resolver-backed effective project rather than its own `cwd` prefix routine.

## Error Handling

- If remote metadata is missing, classify as `Unclassified` and explain that the session has no usable working directory.
- If a mapping exists but produces an invalid local path, surface a recoverable local warning and fall back to `Unclassified`.
- If an override references a deleted project, ignore the override and fall back to automatic resolution.

## Testing Strategy

### Core unit tests

Add tests for:

- direct path match,
- longest-prefix winner,
- no `cwd` fallback,
- profile mapping success,
- profile mapping miss,
- override precedence,
- deleted-project override fallback,
- and cross-surface consistency contracts.

### Presentation tests

Add ViewModel tests for:

- Discover row badge projection,
- Navigation grouping using resolver output,
- Search using resolver output,
- and correction affordance visibility.

### GUI smoke

Add end-to-end smoke for:

1. import remote session with direct project match,
2. import remote session that becomes `Unclassified`,
3. manually re-associate it,
4. restart app,
5. verify the same conversation still appears under the chosen project.

## Rollout Plan

1. Introduce the resolver and tests in Core.
2. Switch Navigation to consume resolver output.
3. Switch Discover to show preview affinity from the same resolver.
4. Switch Global Search to the same resolver.
5. Add local override and path-mapping UI.
6. Add smoke coverage for import, restart, and reassociation flows.

## Decision

The recommended design is:

- no blocking project picker on normal import,
- automatic classification based on remote `cwd`,
- path mapping for remote/local environment differences,
- local per-conversation override when users need control,
- and one shared Core resolver as the project-affinity SSOT for all ViewModels.

This gives the fastest common-path workflow while keeping the system explainable, correctable, and aligned with SSOT-driven MVVM architecture.
