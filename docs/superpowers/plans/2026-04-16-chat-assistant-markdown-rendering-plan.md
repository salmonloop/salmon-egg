# Assistant Chat Markdown Rendering Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render assistant text messages as Markdown (GFM subset) inside existing chat bubbles with stable streaming behavior and guaranteed plain-text fallback.

**Architecture:** Keep conversation message truth as `TextContent` and introduce a small render-policy state machine (`PlainStreaming`, `MarkdownReady`, `FallbackPlain`) in `ChatMessageViewModel`. UI keeps the existing bubble containers and switches only the assistant body area between `TextBlock` and `MarkdownTextBlock` by `x:Bind` state projection.

**Tech Stack:** Uno Platform + WinUI XAML, CommunityToolkit.Labs MarkdownTextBlock, CommunityToolkit.Mvvm, xUnit.

---

**Spec Reference:** `docs/superpowers/specs/2026-04-16-chat-markdown-rendering-design.md`

## File Structure and Responsibilities

### Create

- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderMode.cs`
  - Enum describing assistant text render projection mode.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderPolicy.cs`
  - Pure policy helper for behavior-driven mode resolution (including unclosed fenced-code detection).
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMarkdownRenderPolicyTests.cs`
  - Behavior tests for policy transitions; no private implementation assertions.
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMessageViewModelMarkdownTests.cs`
  - Behavior tests for message-level state transitions via public properties/methods.
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatStylesMarkdownXamlTests.cs`
  - Contract tests that validate bubble preservation + markdown/plain switching hooks in XAML.

### Modify

- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMessageViewModel.cs`
  - Add render-mode state, recompute hooks, fallback API, and projection properties.
- `SalmonEgg/Directory.Packages.props`
  - Add CPM versions for markdown control packages.
- `SalmonEgg/SalmonEgg/SalmonEgg.csproj`
  - Add conditional package references:
  - Windows target: `CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock`
  - non-Windows targets: `CommunityToolkit.Labs.Uwp.Controls.MarkdownTextBlock`
- `SalmonEgg/SalmonEgg/Styles/ChatStyles.xaml`
  - Keep bubble containers and replace incoming assistant body text slot with dual renderer host (`TextBlock` + `MarkdownTextBlock`).

### Existing Tests to Re-run

- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

---

## Chunk 1: Core Render Behavior (Policy + ViewModel)

### Task 1: Add Render Policy (TDD First)

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderMode.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderPolicy.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMarkdownRenderPolicyTests.cs`

- [ ] **Step 1: Write failing behavior tests for render-mode policy**

```csharp
[Theory]
[InlineData("text", false, "hello", ChatMarkdownRenderMode.MarkdownReady)]
[InlineData("text", true, "hello", ChatMarkdownRenderMode.PlainStreaming)]
[InlineData("tool_call", false, "hello", ChatMarkdownRenderMode.PlainStreaming)]
public void ResolveMode_AppliesMessageScopeRules(...) { ... }

[Fact]
public void ResolveMode_UnclosedFence_StaysPlainStreaming() { ... }

[Fact]
public void ResolveMode_ClosedFence_SwitchesToMarkdownReady() { ... }

[Fact]
public void ResolveMode_WhenFallbackSticky_RemainsFallbackPlain() { ... }
```

- [ ] **Step 2: Run tests to verify failure**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatMarkdownRenderPolicyTests
```

Expected: FAIL with missing types/members.

- [ ] **Step 3: Implement minimal policy + enum**

```csharp
public enum ChatMarkdownRenderMode
{
    PlainStreaming,
    MarkdownReady,
    FallbackPlain
}

public static class ChatMarkdownRenderPolicy
{
    public static ChatMarkdownRenderMode Resolve(string? contentType, bool isOutgoing, string? text, bool isFallbackSticky)
    {
        if (isFallbackSticky) return ChatMarkdownRenderMode.FallbackPlain;
        if (isOutgoing || !string.Equals(contentType, "text", StringComparison.Ordinal))
            return ChatMarkdownRenderMode.PlainStreaming;
        return HasUnclosedFence(text) ? ChatMarkdownRenderMode.PlainStreaming : ChatMarkdownRenderMode.MarkdownReady;
    }
}
```

- [ ] **Step 4: Re-run policy tests**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatMarkdownRenderPolicyTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderMode.cs src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMarkdownRenderPolicy.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMarkdownRenderPolicyTests.cs
git commit -m "test(chat): add markdown render policy behavior coverage"
```

### Task 2: Integrate Message ViewModel Render State (Behavior-Focused)

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMessageViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMessageViewModelMarkdownTests.cs`

- [ ] **Step 1: Write failing behavior tests against public VM surface**

```csharp
[Fact]
public void AssistantText_WithClosedFence_UsesMarkdownReady() { ... }

[Fact]
public void AssistantText_WithUnclosedFence_UsesPlainStreaming() { ... }

[Fact]
public void OutgoingText_AlwaysUsesPlainStreaming() { ... }

[Fact]
public void MarkMarkdownRenderFailed_MakesFallbackSticky() { ... }
```

- [ ] **Step 2: Run tests to verify failure**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatMessageViewModelMarkdownTests
```

Expected: FAIL due missing render-state properties/API.

- [ ] **Step 3: Implement VM state and recompute hooks**

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(ShouldRenderMarkdown))]
[NotifyPropertyChangedFor(nameof(ShouldRenderPlainText))]
private ChatMarkdownRenderMode _markdownRenderMode = ChatMarkdownRenderMode.PlainStreaming;

[ObservableProperty]
private bool _isMarkdownFallbackSticky;

public bool ShouldRenderMarkdown => MarkdownRenderMode == ChatMarkdownRenderMode.MarkdownReady;
public bool ShouldRenderPlainText => !ShouldRenderMarkdown;

public void MarkMarkdownRenderFailed()
{
    IsMarkdownFallbackSticky = true;
    RefreshMarkdownRenderMode();
}
```

Also add `partial void OnTextContentChanged`, `OnContentTypeChanged`, `OnIsOutgoingChanged`, `OnIsMarkdownFallbackStickyChanged` to call `RefreshMarkdownRenderMode()`.

- [ ] **Step 4: Re-run VM markdown tests**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatMessageViewModelMarkdownTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatMessageViewModel.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatMessageViewModelMarkdownTests.cs
git commit -m "feat(chat): add assistant markdown render state in message viewmodel"
```

---

## Chunk 2: UI Projection + Packaging + Behavior Gates

### Task 3: Wire Markdown Control Dependencies Per Target

**Files:**
- Modify: `SalmonEgg/Directory.Packages.props`
- Modify: `SalmonEgg/SalmonEgg/SalmonEgg.csproj`

- [ ] **Step 1: Add failing build checkpoint (document baseline)**

Run:
```bash
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-desktop
```

Expected: PASS before markdown namespace usage change.

- [ ] **Step 2: Add CPM package versions**

```xml
<PackageVersion Include="CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock" Version="0.1.251217-build.2433" />
<PackageVersion Include="CommunityToolkit.Labs.Uwp.Controls.MarkdownTextBlock" Version="0.1.251217-build.2433" />
```

- [ ] **Step 3: Add conditional package references in app csproj**

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.26100.0'">
  <PackageReference Include="CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock" />
</ItemGroup>
<ItemGroup Condition="'$(TargetFramework)' != 'net10.0-windows10.0.26100.0'">
  <PackageReference Include="CommunityToolkit.Labs.Uwp.Controls.MarkdownTextBlock" />
</ItemGroup>
```

- [ ] **Step 4: Build both required targets**

Run:
```bash
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-desktop
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-browserwasm
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SalmonEgg/Directory.Packages.props SalmonEgg/SalmonEgg/SalmonEgg.csproj
git commit -m "chore(chat): add markdown text block packages for winui and uno targets"
```

### Task 4: Preserve Bubble Template and Add Dual Renderer Host

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Styles/ChatStyles.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatStylesMarkdownXamlTests.cs`

- [ ] **Step 1: Add failing XAML behavior contract tests**

```csharp
[Fact]
public void MessageTemplate_KeepsIncomingAndOutgoingBubbleBorders() { ... }

[Fact]
public void IncomingAssistantBody_ContainsMarkdownAndPlainTextRenderSlots() { ... }

[Fact]
public void IncomingAssistantBody_UsesXBindStateSwitchingOnly() { ... }
```

Assertions focus on observable layout contracts (bubble containers still present, markdown/plain host present, no code-behind business hooks).

- [ ] **Step 2: Run tests to verify failure**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatStylesMarkdownXamlTests
```

Expected: FAIL before template changes.

- [ ] **Step 3: Update `ChatStyles.xaml` assistant body**

```xml
xmlns:md="using:CommunityToolkit.WinUI.Controls"

<Grid Visibility="{x:Bind HasTextContent, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
  <TextBlock Text="{x:Bind TextContent, Mode=OneWay}"
             Visibility="{x:Bind ShouldRenderPlainText, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
             TextWrapping="Wrap" />
  <md:MarkdownTextBlock Text="{x:Bind TextContent, Mode=OneWay}"
                        Visibility="{x:Bind ShouldRenderMarkdown, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
                        UsePipeTables="True"
                        UseTaskLists="True"
                        UseEmphasisExtras="True"
                        UseAutoLinks="True"
                        DisableLinks="False" />
</Grid>
```

Keep all existing bubble `Border` and timestamp nodes unchanged.

- [ ] **Step 4: Re-run XAML contract tests**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatStylesMarkdownXamlTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SalmonEgg/SalmonEgg/Styles/ChatStyles.xaml tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatStylesMarkdownXamlTests.cs
git commit -m "feat(chat): render assistant markdown inside existing message bubbles"
```

### Task 5: End-to-End Behavior Gate (No Implementation-Detail Assertions)

**Files:**
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs` (only if needed for replay behavior)
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs` (only if needed for guardrails)

- [ ] **Step 1: Add behavior coverage for replay determinism and isolation**

```csharp
[Fact]
public async Task ReplaySameTranscript_ProducesSameRenderModes() { ... }

[Fact]
public async Task MessageFallback_DoesNotContaminateSiblingMessages() { ... }
```

- [ ] **Step 2: Run targeted markdown behavior suite**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~Markdown|FullyQualifiedName~ChatStylesMarkdownXamlTests"
```

Expected: PASS.

- [ ] **Step 3: Run mandatory build gates**

Run:
```bash
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-desktop
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-browserwasm
```

Expected: PASS both.

- [ ] **Step 4: Run broader chat regression subset**

Run:
```bash
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatViewModelTests
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~ChatViewXamlTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs
git commit -m "test(chat): add markdown rendering behavior regression gates"
```

---

## Behavior Test Principles (Hard Gate)

1. Test user-observable outcomes (what is rendered, whether fallback happens, whether message remains readable).
2. Do not assert private method names, internal helper call counts, or exact implementation strings.
3. For XAML contracts, assert durable structural outcomes (bubble preserved, markdown/plain host exists), not fragile formatting trivia.
4. Every new markdown behavior must be covered by at least one failing-first test before implementation.

## Verification Checklist Before Completion

- [ ] Assistant messages with plain prose render via markdown view.
- [ ] Assistant messages with unclosed fenced code remain plain until closed.
- [ ] Outgoing messages remain plain text.
- [ ] Markdown failure path falls back to plain text and stays readable.
- [ ] Existing bubble appearance and timestamp projection remain intact.
- [ ] `dotnet build` passes for `net10.0-desktop` and `net10.0-browserwasm`.
- [ ] Targeted and regression test suites pass.

Plan complete and saved to `docs/superpowers/plans/2026-04-16-chat-assistant-markdown-rendering-plan.md`. Ready to execute?
