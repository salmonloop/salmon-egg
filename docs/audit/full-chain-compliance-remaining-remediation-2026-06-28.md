# Full-chain Compliance Remaining Remediation Plan - 2026-06-28

> Execution note: use `subagent-driven-development` or `executing-plans` only after the user explicitly chooses an execution mode. This plan is a remediation tracker, not approval to batch-fix every item at once.

**Goal:** Track and execute the remaining compliance fixes from the 2026-06-27 full-chain review after the ACP P0 schema cleanup.

**Architecture:** Fix one finding at a time, with a report before and after each item. Prefer protocol/native/platform semantics over local compatibility shims. Keep changes scoped to the specific finding and add behavior or contract tests that prevent the same regression.

**Tech Stack:** .NET / C#, Uno / WinUI 3, XAML `.resw` localization, ACP v1 JSON-RPC protocol models, xUnit / NUnit test projects.

## Global Constraints

- `AGENTS.md` is authoritative for this repository.
- ACP standard compliance must be checked against official ACP v1 schema and extensibility docs before protocol edits.
- Do not keep historical compatibility for non-standard ACP root fields unless the user explicitly asks for a compatibility layer.
- View must remain ViewModel-driven; do not add code-behind state compensation for UI behavior.
- Prefer native WinUI / Uno semantics over app-side focus, selection, or input state machines.
- For non-document changes, run tests/builds matching the affected surface.
- For document-only changes, state explicitly that no product tests were run because the change is documentation-only.
- Before starting each task below, report the intended scope to the user. After finishing each task, report changed files, test evidence, and remaining risk.

---

## Current Baseline

The original review file `docs/audit/full-chain-compliance-review-2026-06-27.md` was deleted by accident. This document reconstructs the remaining remediation plan from the pasted review content and the current repository state on 2026-06-28.

### ACP Standard Snapshot Checked on 2026-06-28

Official ACP references rechecked while writing this plan:

- `https://agentclientprotocol.com/llms.txt` lists the current ACP v1 documentation, including Schema and Extensibility.
- `https://agentclientprotocol.com/protocol/v1/schema` currently defines `PromptRequest` with `_meta`, `prompt`, and `sessionId`; `PromptResponse` with `_meta` and `stopReason`; `SetSessionModeRequest` with `_meta`, `modeId`, and `sessionId`; and `SetSessionModeResponse` with `_meta` only.
- The same schema defines standard message identity on streamed update chunks: `ContentChunk`, `user_message_chunk`, `agent_message_chunk`, and `agent_thought_chunk` may contain `messageId`, where chunks with the same `messageId` belong to the same message and a changed `messageId` indicates a new message.
- The same schema defines `CurrentModeUpdate.currentModeId`; legacy `current_mode_update.modeId` must not be treated as the standard current-mode field.
- `https://agentclientprotocol.com/protocol/v1/extensibility` requires custom data for specification types to use `_meta` and says implementations must not add custom fields at the root of a standard type.

Important identity boundary for later remediation:

- The fields removed during the P0 cleanup were non-standard roots on `session/prompt` request/response and `session/set_mode` response.
- Do not delete or weaken standard JSON-RPC request/response IDs.
- Do not delete standard `session/update` / content chunk `messageId`; that is the authoritative message grouping identity ACP v1 gives clients.
- When scanning for `messageId` or `modeId`, classify the containing ACP type before changing code. The field name alone is not enough evidence of a violation.

### Already Completed Before This Plan

The two P0 ACP schema violations have been cleaned up and should not be reopened unless a regression is found.

- `session/prompt` no longer serializes root `maxTokens`, `stopSequences`, `messageId`, or response root `userMessageId`.
- `session/set_mode` response no longer models root `modeId`; successful local state projection uses the request `modeId`.
- GUI mock no longer returns non-standard `userMessageId` or `session/set_mode` response `modeId`.
- Relevant tests now assert schema-aligned prompt and set-mode behavior:
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionPromptTypesTests.cs`
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionSetModeTypesTests.cs`
  - `tests/SalmonEgg.Infrastructure.Tests/Serialization/MessageParserTests.cs`
  - `tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs`

Verification already run for the P0 cleanup:

```powershell
dotnet test tests\SalmonEgg.Domain.Tests\SalmonEgg.Domain.Tests.csproj --filter "FullyQualifiedName~SessionPromptTypesTests|FullyQualifiedName~SessionSetModeTypesTests" --no-restore
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~MessageParserTests|FullyQualifiedName~AcpClientTests.SetSessionModeAsync_WhenAgentReturnsStandardEmptyObject_UpdatesTrackedSessionFromRequest" --no-restore
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpChatCoordinatorTests|FullyQualifiedName~OutgoingUserMessageProjectorTests|FullyQualifiedName~ChatViewModelTests.SendPromptAsync_WhenPromptResponseCompletes_DoesNotAssignProtocolMessageIdFromResponse|FullyQualifiedName~ChatViewModelTests.SendPromptAsync_UsesCanonicalUuidFormat_ForPromptMessageId" --no-restore
git diff --check
```

## Remaining Task Index

1. P1: Finish protocol serialization test contract cleanup.
2. P1: Remove DI-stage synchronous app-settings IO for ACP eviction options.
3. P1: Rework shell gamepad / directional input to stop overriding native focus semantics.
4. P2: Remove XAML user-visible hardcoded strings and enforce `.resw` as the text source.
5. P2: Remove or debug-gate diagnostic logging and temp transport tracing.

---

## Task 1: Finish Protocol Serialization Test Contract Cleanup

**Status:** Partially done during the P0 cleanup. Wrong `messageId` / `userMessageId` root-field expectations are gone, but many protocol tests still use direct runtime `JsonSerializer` calls or `parser.Options` instead of explicit source-generated `TypeInfo` or a public production parser path.

**Risk If Left Unfixed:** Tests can pass while AOT / trimming / source-generated serialization contracts are broken. Protocol DTOs can drift from the production serialization path.

**Original Evidence To Revalidate:**

- Production source-generated context: `src/SalmonEgg.Infrastructure/Serialization/AcpJsonContext.cs`
- Production parser binding: `src/SalmonEgg.Infrastructure/Serialization/MessageParser.cs`
- Test project access boundary: `src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj` currently exposes internals only to `SalmonEgg.Infrastructure.Tests`.
- `tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj` references `SalmonEgg.Infrastructure`, but cannot access internal `AcpJsonContext` without changing visibility.
- Current high-volume direct serialization sites include:
  - `tests/SalmonEgg.Domain.Tests/Protocol/InitializeTypesTests.cs`
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionNewTypesTests.cs`
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionLoadTypesTests.cs`
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionUpdateTypesTests.cs`
  - `tests/SalmonEgg.Domain.Tests/Protocol/SessionUpdatePolymorphismTests.cs`
  - `tests/SalmonEgg.Infrastructure.Tests/Serialization/MessageParserTests.cs`

**Desired End State:**

- Tests that claim to verify wire protocol use either:
  - `AcpJsonContext.Default.<TypeName>` directly from `SalmonEgg.Infrastructure.Tests`, or
  - `MessageParser` / `AcpMessageParser` public APIs where the production parser behavior is the subject.
- `SalmonEgg.Domain.Tests` should keep pure domain DTO behavior tests only if they do not claim production wire-path coverage.
- No test should reintroduce non-standard root fields for standard ACP request/response DTOs.

**Implementation Checklist:**

- [ ] Before editing, report to the user which protocol test files will be changed.
- [ ] Run this inventory command and paste the meaningful count into the completion report:

```powershell
rg -n "JsonSerializer\.(Serialize|Deserialize|SerializeToElement)" tests\SalmonEgg.Domain.Tests\Protocol tests\SalmonEgg.Infrastructure.Tests\Serialization -S
```

- [ ] In `tests/SalmonEgg.Infrastructure.Tests/Serialization/MessageParserTests.cs`, replace remaining protocol DTO round-trip calls that use `parser.Options` with one of:
  - `JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionUpdateParams)`
  - `JsonSerializer.Serialize(value, AcpJsonContext.Default.SessionUpdateParams)`
  - a public parser method when testing parser dispatch or JSON-RPC envelope behavior.
- [ ] For `tests/SalmonEgg.Domain.Tests/Protocol/InitializeTypesTests.cs`, either move wire-shape tests into `tests/SalmonEgg.Infrastructure.Tests/Serialization/` or add a narrowly scoped `InternalsVisibleTo` entry for `SalmonEgg.Domain.Tests`. Prefer moving wire-shape tests, because source-generated serialization is an Infrastructure concern.
- [ ] Add a source-generation coverage test in `tests/SalmonEgg.Infrastructure.Tests/Serialization/AcpJsonContextTests.cs` that serializes and deserializes the standard protocol DTO groups touched by the review:

```csharp
JsonSerializer.Serialize(new SessionPromptResponse(StopReason.EndTurn), AcpJsonContext.Default.SessionPromptResponse);
JsonSerializer.Serialize(new SessionSetModeResponse(), AcpJsonContext.Default.SessionSetModeResponse);
JsonSerializer.Serialize(ClientCapabilityDefaults.Create(), AcpJsonContext.Default.ClientCapabilities);
```

- [ ] Add a guard test that serializes `SessionPromptParams`, `SessionPromptResponse`, and `SessionSetModeResponse` through `AcpJsonContext` and asserts the disallowed root fields are absent:
  - `maxTokens`
  - `stopSequences`
  - `messageId` on `session/prompt` request
  - `userMessageId`
  - response `modeId` on `session/set_mode`
- [ ] Do not change production DTOs unless a test exposes a real source-generation gap.

**Verification Gates:**

```powershell
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Serialization" --no-restore
dotnet test tests\SalmonEgg.Domain.Tests\SalmonEgg.Domain.Tests.csproj --filter "FullyQualifiedName~Protocol" --no-restore
git diff --check
```

**Completion Report Must Include:**

- Remaining `JsonSerializer` inventory after cleanup.
- Whether any generic serializer calls intentionally remain, and why.
- Confirmation that P0 root fields were not reintroduced.

---

## Task 2: Remove DI-stage Synchronous App-settings IO for ACP Eviction Options

**Status:** Fixed as of 2026-07-01. Kept here as historical tracker context; do not treat this item as active without a new code scan.

**Original Risk:** `AcpConnectionEvictionOptionsLoader.Load` blocked on async disk/settings IO during DI singleton construction. This could cause UI startup stalls or deadlocks and violated the repository rule that constructors and DI registration should bind dependencies only.

**Closure Evidence:**

- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionEvictionOptionsLoader.cs`
  - Current loader exposes `LoadEnvironmentDefaults` and reads only environment variables.
- `SalmonEgg/SalmonEgg/DependencyInjection.cs`
  - DI registration calls `AcpConnectionEvictionOptionsLoader.LoadEnvironmentDefaults(...)`.
- `SalmonEgg/SalmonEgg/App.xaml.cs`
  - App settings are read asynchronously after launch, outside DI singleton construction.
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpConnectionEvictionOptionsLoaderTests.cs`
  - Covers no-IO environment defaults.
- `tests/SalmonEgg.Presentation.Core.Tests/NavigationCoreTests.cs`
  - Guards DI registration against app-settings service access in the loader section.

**Regression Gates To Preserve:**

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpConnectionEvictionOptionsLoaderTests|FullyQualifiedName~NavigationCoreTests" --no-restore
git diff --check
```

**If This Item Is Reopened, Report Must Include:**

- Exact startup/DI path after the change.
- How persisted preferences still refresh runtime options.
- Confirmation that no DI factory blocks on async IO.

---

## Task 3: Rework Shell Gamepad / Directional Input to Preserve Native Focus Semantics

**Status:** Not fixed. This item still needs a focused product-code remediation or a fresh gamepad validation pass; do not infer closure from unrelated XAML/localization compliance work.

**Risk If Left Unfixed:** Physical or synthetic gamepad input can enter both native WinUI/Uno focus handling and app-side fallback handling. This can create double dispatch, value-control edits without explicit engagement, and divergence between real hardware and synthetic smoke tests.

**Original Evidence To Revalidate:**

- `SalmonEgg/SalmonEgg/MainPage.xaml.cs`
  - Attaches polling gamepad service and platform bridge.
  - Dispatches intents through shell code.
  - Programmatically calls `Focus(FocusState.Programmatic)` in shell paths.
- `SalmonEgg/SalmonEgg/Platforms/Windows/MainPage.Windows.cs`
  - Maps `Windows.System.VirtualKey.Gamepad*` into custom intents and marks handled in some paths.
- `SalmonEgg/SalmonEgg/Presentation/Services/Input/MainShellGamepadNavigationDispatcher.cs`
  - Walks focused consumers and falls back to native input bridge or shell back.
- `SalmonEgg/SalmonEgg/Platforms/Windows/WindowsGamepadNativeInputBridge.cs`
  - Uses `SendInput` fallback to synthesize keyboard input.
- Existing audit docs to consult:
  - `docs/audit/gamepad-native-navigation-boundary.md`
  - `docs/audit/gamepad-validation-chain.md`

**Desired End State:**

- Standard controls own their keyboard, DPad, focus, selection, activation, and value-editing semantics.
- App-level gamepad services collect input facts and power diagnostics, but do not globally synthesize native focus movement or activation.
- Any remaining compensation is local, documented, and backed by real-device or diagnostic evidence for a specific WinUI/Uno gap.
- Cross-region navigation is expressed declaratively with real focusable controls and `XYFocus*` properties where needed.

**Implementation Checklist:**

- [ ] Before editing, report the exact shell input paths to be removed or retained.
- [ ] Add a source contract test in `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs` or a new focused test file that fails if shell-level code reintroduces global `SendInput`, `FocusManager.TryMoveFocus`, AutomationPeer invoke/toggle/expand, or `SelectedItem` mutation for gamepad navigation.
- [ ] Remove `WindowsGamepadNativeInputBridge` from the default navigation path unless a specific, documented control gap remains.
- [ ] Keep `WindowsGamepadInputService` only as an input fact source and diagnostics feed. Polling should not dispatch global focus/activation intents into the shell.
- [ ] Replace shell handoff focus paths with native XAML focus relationships where a real cross-region relation is needed. The implementation must use actual focusable controls, not hidden proxy elements.
- [ ] Update `MainShellGamepadNavigationDispatcher` so it no longer owns a global focus/activation fallback path. If a page-specific consumer remains, it must be opt-in and not overlap with native control behavior.
- [ ] Update tests that currently assert synthetic gamepad fallback behavior so they assert native focus reachability or diagnostic fact collection instead.

**Verification Gates:**

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~XamlComplianceTests|FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~AcpChatCoordinatorTests" --no-restore
dotnet build SalmonEgg.sln --no-restore
git diff --check
```

If GUI smoke is run for this item, it must record:

- build artifact path,
- app instance source,
- focused element AutomationId / ControlType before and after one directional input,
- whether one physical or synthetic input caused exactly one visible behavior.

**Completion Report Must Include:**

- Which global fallback paths were removed.
- Which native `XYFocus*` or control semantics now own the behavior.
- Any retained compensation and the evidence for it.

---

## Task 4: Remove XAML User-visible Hardcoded Strings and Enforce `.resw`

**Status:** Not fixed, but the stale broad evidence below must be revalidated before implementation. A 2026-07-01 quick scan narrows the current visible Chinese XAML literals to:

- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`
  - `ToolTipService.ToolTip="更多"`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/McpSettingsPage.xaml`
  - `AutomationProperties.Name="删除"`
  - `ToolTipService.ToolTip="删除"`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/DiagnosticsSettingsPage.xaml`
  - `Text="Agent："`

Chinese comments remain in some XAML files and are not user-visible localization defects by themselves.

**Risk If Left Unfixed:** XAML and `.resw` remain competing sources of truth. Missing `x:Uid` or missing resource entries can surface Chinese fallback strings in non-Chinese locales, while existing local values make reviews and translation maintenance unreliable.

**Original Evidence To Revalidate:**

The original review called out these files:

- `SalmonEgg/SalmonEgg/Presentation/Views/GeneralSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/McpSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/DiagnosticsSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`

The original broad scan also reported hardcoded Chinese in additional settings/start pages, including:

- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AboutPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AppearanceSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/DataStorageSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/ShortcutsSettingsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml`

Resource files already exist:

- `SalmonEgg/SalmonEgg/Strings/en/Resources.resw`
- `SalmonEgg/SalmonEgg/Strings/en-US/Resources.resw`
- `SalmonEgg/SalmonEgg/Strings/zh-Hans/Resources.resw`

**Desired End State:**

- User-visible XAML text uses `x:Uid` / `.resw`; XAML does not retain Chinese fallback text in `Text`, `Content`, `Header`, `OnContent`, `OffContent`, `Title`, `Message`, tooltip, or accessible name properties.
- Allowed non-user-visible probes are centralized in a test whitelist with rationale.
- Attached properties such as `AutomationProperties.Name` use `.resw` property-name syntax where they are user-visible accessibility text.

**Implementation Checklist:**

- [ ] Before editing, report the first batch of XAML files to localize. Do not attempt every XAML file in one unreviewed batch if the diff becomes large.
- [ ] Add a contract test in `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs` that scans `SalmonEgg/SalmonEgg/Presentation/Views/**/*.xaml` for Chinese characters in these attributes:
  - `Text`
  - `Content`
  - `Header`
  - `OnContent`
  - `OffContent`
  - `Title`
  - `Message`
  - `ToolTipService.ToolTip`
  - `AutomationProperties.Name`
- [ ] Maintain a small whitelist only for comments or non-user-visible diagnostic probes. The whitelist entry must include the file path and reason.
- [ ] For each XAML element with a local Chinese value, either:
  - remove the local text and keep an existing `x:Uid`, or
  - add a new stable `x:Uid` and corresponding `.resw` entries for `en`, `en-US`, and `zh-Hans`.
- [ ] When adding `.resw` entries, use property names such as:

```text
General_PageTitle.Text
General_AutoStartToggle.OnContent
Mcp_DeleteButton.AutomationProperties.Name
```

- [ ] Do not alter layout, visual styling, or binding behavior as part of this localization cleanup.

**Verification Gates:**

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~XamlComplianceTests" --no-restore
dotnet build SalmonEgg.sln --no-restore
git diff --check
```

**Completion Report Must Include:**

- List of localized XAML files.
- Count of new or changed `.resw` entries by locale.
- Remaining whitelist entries, if any.

---

## Task 5: Remove or Debug-gate Diagnostic Logging and Temp Transport Tracing

**Status:** Fixed as of 2026-07-01. Kept here as historical tracker context; do not treat this item as active without a new source scan.

**Original Risk:** Release builds could create temp debug trace files, write full ACP TX/RX payloads, or construct expensive/interpolated debug strings even when debug output is disabled. This risk included leaking prompts, paths, stderr, or protocol payloads.

**Closure Evidence:**

- `src/SalmonEgg.Infrastructure.Desktop/Transport/StdioTransport.cs`
  - Current source scan has no `_debugFileWriter` payload trace writer.
- `tests/SalmonEgg.Infrastructure.Tests/Transport/StdioTransportSourceTests.cs`
  - Guards against default payload trace files and full TX/RX/STDERR protocol payload logging.

**Regression Gates To Preserve:**

```powershell
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~StdioTransport" --no-restore
git diff --check
```

---

## Final Validation After All Tasks

Run these after the final remaining item is fixed:

```powershell
dotnet test tests\SalmonEgg.Domain.Tests\SalmonEgg.Domain.Tests.csproj --no-restore
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --no-restore
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore
dotnet build SalmonEgg.sln --no-restore
git diff --check
```

If any task changes XAML resources or platform UI behavior, add the relevant GUI smoke gate before declaring the full chain clean.

## Non-goals

- Do not recreate the deleted 2026-06-27 review report verbatim in this file.
- Do not reintroduce compatibility for deleted ACP root fields.
- Do not batch gamepad input redesign together with localization or logging cleanup.
- Do not treat broad formatting churn as remediation.
