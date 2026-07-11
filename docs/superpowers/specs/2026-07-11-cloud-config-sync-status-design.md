# Cloud Config Sync Status Design

## Goal

Make cloud configuration sync communicate connection and data-sync state independently, so a successful WebDAV connection, an active sync, a completed restore/upload, and a failure are all immediately visible and actionable.

## Current Problem

The current page projects every outcome into one status string. `IsCloudConfigSyncEnabled` also acts as both active-provider state and connection state. This makes a successful connection visually weak and makes failures ambiguous: users cannot tell whether credentials are invalid, the provider is connected but synchronization failed, or no sync has occurred yet.

## State Ownership

`DataStorageSettingsViewModel` remains the single UI state owner. The view binds to state and commands only. Code-behind continues to handle password transfer and destructive confirmation dialogs, not cloud-sync visual state.

Connection and synchronization are separate projections:

- `CloudConfigConnectionState`: `Disconnected`, `NeedsConfiguration`, `Connecting`, `Connected`, `ConnectionFailed`.
- `CloudConfigTransferState`: `NotSynced`, `Syncing`, `Uploaded`, `Restored`, `ConflictRemoteApplied`, `Failed`.

An active configured provider is not automatically connected. `Connected` is established only after an operation reaches the provider and returns a successful upload, restore, or conflict-resolution result. A later sync failure does not erase the last established connection fact unless the result is specifically authorization/configuration related.

## State Transitions

| Trigger | Connection | Transfer | User-visible result |
|---|---|---|---|
| No active provider | Disconnected | NotSynced | Cloud sync is not connected |
| Selected provider configuration incomplete | NeedsConfiguration | NotSynced | Complete the highlighted fields |
| Connect command starts | Connecting | Syncing | Connecting to provider and checking cloud data |
| First remote file missing and upload succeeds | Connected | Uploaded | Connected; first configuration uploaded |
| Remote file restored | Connected | Restored | Connected; cloud configuration restored |
| Remote conflict applied | Connected | ConflictRemoteApplied | Connected; cloud version applied, local backup retained |
| Manual sync starts | Connected | Syncing | Connected; synchronizing configuration |
| Manual sync fails after prior connection | Connected | Failed | Connected; synchronization failed, retry available |
| Authorization/configuration failure | ConnectionFailed | Failed | Connection failed with provider message and retry |
| Disconnect starts/completes | Disconnected | NotSynced | Disconnected |
| Active provider fields change | NeedsConfiguration | NotSynced | Apply changes and reconnect |

Only the latest operation may update the UI. The busy state prevents overlapping user commands; selected-provider refreshes must still reject stale provider results.

## UI Structure

The cloud-sync section has three un-nested layers inside the existing settings section container:

1. A persistent status summary at the top.
2. Provider selection and provider-specific configuration fields.
3. Contextual actions.

The summary contains:

- provider display name;
- a native `ProgressRing` while connecting or syncing;
- a semantic status glyph and localized connection headline otherwise;
- a second line for transfer state and last-sync time;
- a non-transient error message for failed connection or sync;
- the active remote target when available.

Color is supplementary, not the only signal. Text and glyphs distinguish success, warning, progress, disconnected, and failure. Automation names expose the same state to assistive technology.

Actions adapt to state:

- `Connect <provider>` before connection;
- `Apply changes and reconnect` when active provider fields differ from the connected configuration;
- `Retry connection` after connection failure;
- `Sync now` only when connected and idle;
- `Disconnect` whenever an active provider exists and no operation is running.

## Error Handling

Validation remains next to the relevant fields. Provider/network errors appear in the persistent summary and remain until a new operation starts or succeeds. A sync failure after a prior successful connection keeps the connection headline successful and changes only the transfer line/error message.

## Accessibility And Localization

All visible text is localized in Presentation.Core resources. XAML uses `x:Bind`; no new runtime `Binding` is introduced. The summary exposes stable automation IDs for connection status, transfer status, error text, progress, and remote target. The layout uses system typography, theme resources, native controls, and no pixel-position workaround.

## Testing

Presentation.Core behavior tests cover every state transition and the separation between connection and transfer failure. XAML compliance tests cover the status surface and binding ownership. The Windows cloud-sync smoke verifies the summary exists and provider switching still reveals the correct setup fields. Build and platform-appropriate GUI smoke use the current build artifacts.

