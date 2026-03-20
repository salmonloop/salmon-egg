# WorkspaceWriter Store Projection (Snapshot-Only)

## Summary
Wire `WorkspaceWriter` into the chat store projection path so chat state snapshots are persisted to `ChatConversationWorkspace` without making the workspace a source of truth. This is additive and temporary: `ChatViewModel` will continue writing directly until removed by a future change.

## Goals
- Persist snapshot-only projections from `ChatStore` into `ChatConversationWorkspace`.
- Keep `ChatStore` as the single source of truth.
- Avoid any direct writes from `ChatViewModel` (no changes there in this task).
- Preserve cross-platform behavior and thread-safety expectations.

## Non-Goals
- Refactoring or removing existing `ChatViewModel` persistence logic.
- Changing `ChatConversationWorkspace` to call `WorkspaceWriter` internally.
- DI registration changes (will be noted if required, but not applied).

## Architecture
- `ChatStore` remains the authoritative source for `ChatState`.
- `WorkspaceWriter` remains a snapshot-only sink and does not own state.
- `ChatConversationWorkspace` remains a destination and is not wrapped or modified.

## Components
- `ChatStore` gains an optional dependency on `IWorkspaceWriter`.
- A store-side projection subscribes to `IState<ChatState>` and invokes `WorkspaceWriter.Enqueue(state, scheduleSave: true)`.
- Existing `WorkspaceWriter` code remains unchanged.

## Data Flow
1. `ChatStore.State` emits a new `ChatState`.
2. Store projection enqueues a snapshot write via `WorkspaceWriter`.
3. `WorkspaceWriter` clones the state into a `ConversationWorkspaceSnapshot` and upserts into `ChatConversationWorkspace`.
4. `ChatConversationWorkspace` remains the destination only.

## Error Handling and Lifecycle
- `ChatStore` owns and cancels the projection subscription on disposal to avoid leaks.
- `WorkspaceWriter` is already defensive against null/empty state and handles throttling.
- No retries; exceptions during projection are logged or ignored depending on existing patterns.

## Testing
- No new tests are added in this change.
- Tests are not run per request.

## Rollout Notes
- Expect temporary duplicate writes from `ChatViewModel` and `ChatStore` until the ViewModel path is removed.
- If DI does not provide `IWorkspaceWriter`, projection will be a no-op and should be noted.
