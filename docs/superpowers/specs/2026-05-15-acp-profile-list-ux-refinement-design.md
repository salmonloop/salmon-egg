# ACP Profile List UX Refinement Design

## Context

The ACP Agent settings page already uses native WinUI/Uno controls and a shared settings-page visual system. The area that still feels weaker is the top "ACP connection configuration" profile list. Each row currently carries profile identity, endpoint details, connection status, the connect toggle, and management actions in a single tight horizontal sequence. The behavior is correct, but the information hierarchy is not calm enough for a settings surface that developers may revisit often.

This refinement is scoped only to the profile list section at the top of `AcpConnectionSettingsPage.xaml`. Local path mappings and advanced session loading policy remain unchanged.

## Goals

1. Make each ACP profile row easier to scan by separating identity, endpoint, status, connection action, and overflow actions.
2. Keep the section-level commands, refresh and new profile, near the section title.
3. Preserve native WinUI/Uno behavior for `ListView`, `ToggleSwitch`, `Button`, `MenuFlyout`, focus, keyboard navigation, and selection.
4. Keep the ViewModel as the single source of truth for profile list items and connection state.
5. Improve narrow-window resilience without adding custom behavior or platform-specific code.

## Non-Goals

1. Do not change ACP protocol behavior, connection sequencing, profile persistence, or command execution.
2. Do not introduce a separate selected-profile details panel.
3. Do not change local path mapping or hydration policy sections.
4. Do not replace native control templates or intercept pointer/keyboard behavior.
5. Do not add a second owner for connected, connecting, selected, or status state.

## Proposed Design

Use the existing `ListView` as the profile container and keep `SelectedItem="{x:Bind ViewModel.Profiles.SelectedProfileItem, Mode=TwoWay}"`. Each row remains a native `ListViewItem`, but its internal layout becomes more deliberate:

- The leading transport glyph stays as a compact visual identifier.
- The primary text block contains the profile name and endpoint description, with ellipsis trimming for long values.
- Status moves into a visually secondary but stable status area so it does not compete with the profile name.
- The connection `ToggleSwitch` stays in the row action area and remains the only direct connect/disconnect affordance.
- The overflow `Button.Flyout` remains the place for edit and delete commands.

The row should use shallow `Grid` layout rather than deeper nested panels. The main row can keep a horizontal shape at normal widths, while text wrapping/trimming and stable action widths prevent actions from colliding with long endpoint text. Any responsive changes must be achieved with standard layout constraints, not code-behind state synchronization.

The section header remains a `Grid` with title and command group. "Refresh" stays secondary, "New profile" remains the primary section action.

## Accessibility And Localization

All visible text introduced or changed in XAML must use `x:Uid` and corresponding resource entries. Existing localized strings should be reused when their meaning remains the same.

The native focus visuals, selection visuals, toggle semantics, menu semantics, and keyboard navigation must remain owned by WinUI/Uno controls. Automation IDs that already support tests or GUI smoke must be preserved. If a new stable test hook is needed, add it as an `AutomationProperties.AutomationId` without changing behavior.

## Test Plan

Update focused XAML contract tests to verify:

1. The ACP profile list remains a native `ListView` bound to `ViewModel.Profiles.ProfileItems`.
2. Selection remains bound to `ViewModel.Profiles.SelectedProfileItem`.
3. Refresh, add, edit, delete, and connection toggle entry points remain present.
4. The profile row still uses native `ToggleSwitch` and `MenuFlyout`.
5. The refinement does not replace path mapping bindings or the advanced hydration `Expander`.

Run the targeted Presentation Core tests that cover settings XAML. Because this is a XAML change, run a build of the app project after implementation.

## Risks

The main risk is accidentally turning a visual refinement into a second behavioral layer. The mitigation is to keep edits in XAML layout and resources only, preserve existing bindings and event handlers, and avoid new code-behind or ViewModel state.

