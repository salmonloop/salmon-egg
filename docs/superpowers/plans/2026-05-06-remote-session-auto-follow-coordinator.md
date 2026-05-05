# Remote Session Auto-Follow Coordinator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace fragile View-side auto-follow orchestration with a Core-owned coordinator so remote session hydration/replay reliably settles to bottom without false user-detach.

**Architecture:** Introduce a `TranscriptViewportCoordinator` in `Presentation.Core` as the only policy owner. `ChatView`/`MiniChatView` become thin adapters that publish viewport facts and execute native scroll commands. Session/hydration authority remains in `ChatViewModel`, which emits lifecycle signals into the coordinator.

**Tech Stack:** .NET 10, C#, WinUI 3/Uno, xUnit, FlaUI GUI smoke tests, existing `TranscriptScrollSettler` and `ScrollViewerViewportMonitor`.

---

## Scope Check

This plan targets one subsystem only: transcript viewport auto-follow policy and adapters.  
It does not include ACP protocol changes, overlay copy/style redesign, or connection ownership refactors.

## File Structure (Locked Before Tasking)

### Create

1. `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs`  
   Single owner for viewport policy state machine and command generation.
2. `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportContracts.cs`  
   Shared immutable contracts: events, facts, commands, states, reasons.
3. `tests/SalmonEgg.Presentation.Core.Tests/Utilities/TranscriptViewportCoordinatorTests.cs`  
   Table-driven state transition tests and stale-generation safety tests.
4. `tests/SalmonEgg.GuiTests.Windows/Viewport/ViewportNoIntentDetachSmokeTests.cs`  
   GUI regressions: viewport jitter without user intent must not detach auto-follow.

### Modify

1. `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs`  
   Replace direct policy decisions with coordinator integration and adapter command handling.
2. `SalmonEgg/SalmonEgg/Presentation/Views/MiniWindow/MiniChatView.xaml.cs`  
   Align mini-window behavior with same coordinator semantics.
3. `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`  
   Keep existing probe; update projected automation state source to coordinator semantics.
4. `tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs`  
   Wire existing slow-replay tests to assert coordinator-projected viewport states.
5. `tests/SalmonEgg.Presentation.Core.Tests/NavigationCoreTests.cs`  
   Static guardrails: no synchronous layout forcing and no deprecated detachment heuristic.

---

### Task 1: Introduce Coordinator Contracts and Red Tests

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportContracts.cs`
- Create: `tests/SalmonEgg.Presentation.Core.Tests/Utilities/TranscriptViewportCoordinatorTests.cs`

- [ ] **Step 1: Write failing contract tests**

```csharp
using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptViewportCoordinatorTests
{
    [Fact]
    public void NonBottomWithoutUserIntent_DoesNotDetachAutoFollow()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", generation: 1));
        sut.Handle(new TranscriptViewportEvent.TranscriptAppended("conv-1", generation: 1, addedCount: 20));

        sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            generation: 1,
            new TranscriptViewportFact(hasItems: true, isReady: true, isAtBottom: false, isProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportState.Settling, sut.State);
        Assert.True(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void UserIntentScroll_DetachesAutoFollow()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", generation: 1));

        sut.Handle(new TranscriptViewportEvent.UserIntentScroll("conv-1", generation: 1));

        Assert.Equal(TranscriptViewportState.DetachedByUser, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~TranscriptViewportCoordinatorTests"`  

Expected: FAIL with missing `TranscriptViewportCoordinator` / contract types.

- [ ] **Step 3: Add contract definitions**

```csharp
namespace SalmonEgg.Presentation.Utilities;

public enum TranscriptViewportState { Idle, Settling, Following, DetachedByUser, Suspended }

public readonly record struct TranscriptViewportFact(
    bool HasItems,
    bool IsReady,
    bool IsAtBottom,
    bool IsProgrammaticScrollInFlight);

public abstract record TranscriptViewportEvent(string ConversationId, int Generation)
{
    public sealed record SessionActivated(string ConversationId, int Generation) : TranscriptViewportEvent(ConversationId, Generation);
    public sealed record ConversationContextInvalidated(string ConversationId, int Generation) : TranscriptViewportEvent(ConversationId, Generation);
    public sealed record UserIntentScroll(string ConversationId, int Generation) : TranscriptViewportEvent(ConversationId, Generation);
    public sealed record TranscriptAppended(string ConversationId, int Generation, int AddedCount) : TranscriptViewportEvent(ConversationId, Generation);
    public sealed record ViewportFactChanged(string ConversationId, int Generation, TranscriptViewportFact Fact) : TranscriptViewportEvent(ConversationId, Generation);
}

public enum TranscriptViewportCommandKind
{
    None,
    IssueScrollToBottom,
    StopProgrammaticScroll,
    MarkAutoFollowAttached,
    MarkAutoFollowDetached
}

public readonly record struct TranscriptViewportCommand(
    TranscriptViewportCommandKind Kind,
    string ConversationId,
    int Generation,
    string? Reason = null);
```

- [ ] **Step 4: Re-run test to verify compile still fails only on coordinator implementation**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~TranscriptViewportCoordinatorTests"`  

Expected: FAIL on missing coordinator implementation only.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportContracts.cs tests/SalmonEgg.Presentation.Core.Tests/Utilities/TranscriptViewportCoordinatorTests.cs
git commit -m "test: add viewport coordinator contract and red tests"
```

---

### Task 2: Implement Coordinator State Machine (Green Core Tests)

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Utilities/TranscriptViewportCoordinatorTests.cs`

- [ ] **Step 1: Expand tests to cover required transitions and stale generation**

```csharp
[Fact]
public void SessionActivated_ThenReadyBottom_TransitionsToFollowing_AndIssuesAttach()
{
    var sut = new TranscriptViewportCoordinator();
    sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 1));

    var cmd = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
        "conv-1",
        1,
        new TranscriptViewportFact(true, true, true, false)));

    Assert.Equal(TranscriptViewportState.Following, sut.State);
    Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowAttached, cmd.Kind);
}

[Fact]
public void StaleGenerationEvent_IsIgnored()
{
    var sut = new TranscriptViewportCoordinator();
    sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 3));

    var cmd = sut.Handle(new TranscriptViewportEvent.UserIntentScroll("conv-1", 2));

    Assert.Equal(TranscriptViewportCommandKind.None, cmd.Kind);
    Assert.Equal(TranscriptViewportState.Settling, sut.State);
}
```

- [ ] **Step 2: Run tests and capture failing assertions**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~TranscriptViewportCoordinatorTests"`  

Expected: FAIL on unimplemented transitions.

- [ ] **Step 3: Implement minimal coordinator**

```csharp
namespace SalmonEgg.Presentation.Utilities;

public sealed class TranscriptViewportCoordinator
{
    private string? _conversationId;
    private int _generation;

    public TranscriptViewportState State { get; private set; } = TranscriptViewportState.Idle;
    public bool IsAutoFollowAttached { get; private set; } = true;

    public TranscriptViewportCommand Handle(TranscriptViewportEvent evt)
    {
        if (evt is TranscriptViewportEvent.SessionActivated activated)
        {
            _conversationId = activated.ConversationId;
            _generation = activated.Generation;
            State = TranscriptViewportState.Settling;
            IsAutoFollowAttached = true;
            return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, activated.ConversationId, activated.Generation);
        }

        if (!string.Equals(_conversationId, evt.ConversationId, StringComparison.Ordinal) || evt.Generation < _generation)
        {
            return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, evt.ConversationId, evt.Generation, "StaleOrMismatchedContext");
        }

        return evt switch
        {
            TranscriptViewportEvent.UserIntentScroll e => DetachByUser(e),
            TranscriptViewportEvent.ViewportFactChanged e => HandleFact(e),
            TranscriptViewportEvent.ConversationContextInvalidated e => Suspend(e),
            _ => new TranscriptViewportCommand(TranscriptViewportCommandKind.None, evt.ConversationId, evt.Generation)
        };
    }

    private TranscriptViewportCommand HandleFact(TranscriptViewportEvent.ViewportFactChanged e)
    {
        if (State == TranscriptViewportState.DetachedByUser)
        {
            if (e.Fact.IsAtBottom)
            {
                State = TranscriptViewportState.Following;
                IsAutoFollowAttached = true;
                return new TranscriptViewportCommand(TranscriptViewportCommandKind.MarkAutoFollowAttached, e.ConversationId, e.Generation, "UserReturnedToBottom");
            }

            return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, e.ConversationId, e.Generation);
        }

        if (e.Fact.HasItems && e.Fact.IsReady && e.Fact.IsAtBottom)
        {
            State = TranscriptViewportState.Following;
            IsAutoFollowAttached = true;
            return new TranscriptViewportCommand(TranscriptViewportCommandKind.MarkAutoFollowAttached, e.ConversationId, e.Generation, "BottomConfirmed");
        }

        if (e.Fact.HasItems && e.Fact.IsReady && !e.Fact.IsAtBottom && IsAutoFollowAttached)
        {
            return new TranscriptViewportCommand(TranscriptViewportCommandKind.IssueScrollToBottom, e.ConversationId, e.Generation, "SettleOrFollow");
        }

        return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, e.ConversationId, e.Generation);
    }

    private TranscriptViewportCommand DetachByUser(TranscriptViewportEvent.UserIntentScroll e)
    {
        State = TranscriptViewportState.DetachedByUser;
        IsAutoFollowAttached = false;
        return new TranscriptViewportCommand(TranscriptViewportCommandKind.MarkAutoFollowDetached, e.ConversationId, e.Generation, "ExplicitUserIntent");
    }

    private TranscriptViewportCommand Suspend(TranscriptViewportEvent.ConversationContextInvalidated e)
    {
        State = TranscriptViewportState.Suspended;
        IsAutoFollowAttached = false;
        return new TranscriptViewportCommand(TranscriptViewportCommandKind.StopProgrammaticScroll, e.ConversationId, e.Generation, "ContextInvalidated");
    }
}
```

- [ ] **Step 4: Re-run tests and ensure green**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~TranscriptViewportCoordinatorTests"`  

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs tests/SalmonEgg.Presentation.Core.Tests/Utilities/TranscriptViewportCoordinatorTests.cs
git commit -m "feat: add transcript viewport coordinator state machine"
```

---

### Task 3: Integrate Coordinator Into ChatView Adapter (Keep Rollback Switch)

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/NavigationCoreTests.cs`

- [ ] **Step 1: Add static guard test for removed false-detach heuristic**

```csharp
[Fact]
public void ChatViewCodeBehind_DoesNotUseLegacyViewportDriftDetachHeuristic()
{
    var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
    Assert.DoesNotContain("_lastObservedViewportAtBottom is true && !_transcriptScrollSettler.HasPendingWork", code, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run guard test to verify it fails before refactor**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ChatViewCodeBehind_DoesNotUseLegacyViewportDriftDetachHeuristic"`  

Expected: FAIL because legacy condition still exists.

- [ ] **Step 3: Refactor ChatView event handlers to coordinator commands**

```csharp
private readonly TranscriptViewportCoordinator _viewportCoordinator = new();
private const bool EnableViewportCoordinator = true;

private void OnMessagesListViewportChanged(DependencyObject sender, DependencyProperty dp)
{
    if (!EnableViewportCoordinator || !_isViewLoaded || MessagesList is null || ViewModel.MessageHistory.Count <= 0)
    {
        return;
    }

    var cmd = _viewportCoordinator.Handle(new TranscriptViewportEvent.ViewportFactChanged(
        ViewModel.CurrentSessionId ?? string.Empty,
        _scrollScheduleGeneration,
        new TranscriptViewportFact(
            hasItems: ViewModel.MessageHistory.Count > 0,
            isReady: HasLastItemContainerGenerated(ViewModel.MessageHistory.Count),
            isAtBottom: IsListViewportAtBottom(),
            isProgrammaticScrollInFlight: _suspendAutoScrollTracking || _scrollToBottomScheduled)));

    ApplyViewportCommand(cmd);
}

private void OnMessagesListPointerWheelChanged(object sender, PointerRoutedEventArgs e)
{
    if (EnableViewportCoordinator)
    {
        var cmd = _viewportCoordinator.Handle(new TranscriptViewportEvent.UserIntentScroll(
            ViewModel.CurrentSessionId ?? string.Empty,
            _scrollScheduleGeneration));
        ApplyViewportCommand(cmd);
        return;
    }

    _manualScrollIntentPending = true;
    StopInitialScrollForManualInteraction();
}

private void ApplyViewportCommand(TranscriptViewportCommand cmd)
{
    switch (cmd.Kind)
    {
        case TranscriptViewportCommandKind.IssueScrollToBottom:
            ScheduleScrollToBottom();
            break;
        case TranscriptViewportCommandKind.StopProgrammaticScroll:
            _scrollToBottomScheduled = false;
            _activeTranscriptScrollGeneration = -1;
            break;
    }
}
```

- [ ] **Step 4: Re-run guard test and targeted chat tests**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ChatViewCodeBehind_DoesNotUseLegacyViewportDriftDetachHeuristic|FullyQualifiedName~ChatViewCodeBehind_DoesNotForceSynchronousListLayoutDuringTranscriptSettle"`  

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs tests/SalmonEgg.Presentation.Core.Tests/NavigationCoreTests.cs
git commit -m "refactor: route chat viewport auto-follow through coordinator"
```

---

### Task 4: Align MiniChatView With Same Coordinator Semantics

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/MiniWindow/MiniChatView.xaml.cs`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`

- [ ] **Step 1: Add regression assertion that mini view does not maintain separate detach policy branch**

```csharp
[Fact]
public void MiniChatView_UsesCoordinatorBasedViewportPolicy()
{
    var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
    Assert.Contains("TranscriptViewportCoordinator", code, StringComparison.Ordinal);
    Assert.DoesNotContain("_userScrolledUp = !IsListViewportAtBottom()", code, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run regression assertion and confirm failure**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~MiniChatView_UsesCoordinatorBasedViewportPolicy"`  

Expected: FAIL before mini view migration.

- [ ] **Step 3: Port mini view to coordinator command flow**

```csharp
private readonly TranscriptViewportCoordinator _viewportCoordinator = new();

private void OnMessagesListLayoutUpdated(object? sender, object e)
{
    if (!_isLoaded || !_isMessagesListLoaded)
    {
        return;
    }

    var cmd = _viewportCoordinator.Handle(new TranscriptViewportEvent.ViewportFactChanged(
        ViewModel.CurrentSessionId ?? string.Empty,
        _scrollScheduleGeneration,
        new TranscriptViewportFact(
            hasItems: ViewModel.MessageHistory.Count > 0,
            isReady: HasLastItemContainerGenerated(ViewModel.MessageHistory.Count),
            isAtBottom: IsListViewportAtBottom(),
            isProgrammaticScrollInFlight: _suspendAutoScrollTracking || _scrollToBottomScheduled)));

    if (cmd.Kind == TranscriptViewportCommandKind.IssueScrollToBottom)
    {
        ScheduleScrollToBottom();
    }
}
```

- [ ] **Step 4: Run targeted mini-view compliance tests**

Run:  
`dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~MiniChatView_UsesCoordinatorBasedViewportPolicy|FullyQualifiedName~MiniChatView_UsesNativeTranscriptInteractionWithoutWholePageLifecycleHack"`  

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SalmonEgg/SalmonEgg/Presentation/Views/MiniWindow/MiniChatView.xaml.cs tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs
git commit -m "refactor: align mini chat viewport behavior with coordinator"
```

---

### Task 5: Add GUI Regression for "No User Intent, No Detach"

**Files:**
- Create: `tests/SalmonEgg.GuiTests.Windows/Viewport/ViewportNoIntentDetachSmokeTests.cs`
- Modify: `tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj`
- Modify: `tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs`

- [ ] **Step 1: Add failing GUI test for jitter without manual input**

```csharp
[SkippableFact]
public void RemoteSlowReplay_ViewportJitterWithoutUserIntent_DoesNotDetachFromBottom()
{
    using var scope = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(cachedMessageCount: 1, replayMessageCount: 40);
    using var session = WindowsGuiAppSession.LaunchFresh();

    var item = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
    session.ActivateElement(item);

    Assert.True(session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30)));
    Assert.True(WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10)));

    // No pointer/wheel/key input here: only passive wait while replay settles.
    Thread.Sleep(1200);
    var state = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200));
    Assert.NotEqual("not_bottom", state);
}
```

- [ ] **Step 2: Run the new GUI test and verify failure before full integration**

Run:  
`$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~RemoteSlowReplay_ViewportJitterWithoutUserIntent_DoesNotDetachFromBottom"`  

Expected: FAIL in current drift-detach behavior.

- [ ] **Step 3: Wire test into project and align existing slow replay checks with coordinator states**

```xml
<Compile Include="Viewport\ViewportNoIntentDetachSmokeTests.cs" />
```

```csharp
// In ChatSkeletonSmokeTests: keep bottom assertion tied to coordinator-projected probe state.
Assert.True(
    WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10)),
    $"Transcript viewport did not settle to coordinator bottom state. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
```

- [ ] **Step 4: Re-run targeted GUI tests**

Run:  
`$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~RemoteSlowReplay_ViewportJitterWithoutUserIntent_DoesNotDetachFromBottom|FullyQualifiedName~SelectRemoteSessionWithSlowReplay_ViewportStateReportsBottomAfterHydration|FullyQualifiedName~SelectRemoteSessionWithSlowReplay_PageUpDetachesViewportAfterHydration"`  

Expected: PASS (or explicit skip if GUI gate disabled by environment policy).

- [ ] **Step 5: Commit**

```bash
git add tests/SalmonEgg.GuiTests.Windows/Viewport/ViewportNoIntentDetachSmokeTests.cs tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs
git commit -m "test: add gui regression for no-intent viewport detach"
```

---

### Task 6: Observability, Cleanup, and Full Verification

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`

- [ ] **Step 1: Add structured transition logging fields to coordinator path**

```csharp
public readonly record struct TranscriptViewportTransition(
    TranscriptViewportState FromState,
    TranscriptViewportState ToState,
    string ConversationId,
    int Generation,
    string EventName,
    string Reason);
```

```csharp
// ChatView consumption
if (cmd.Reason is not null)
{
    _ = ViewModel.Dispatcher.Enqueue(() =>
        System.Diagnostics.Debug.WriteLine($"ViewportCmd kind={cmd.Kind} conv={cmd.ConversationId} gen={cmd.Generation} reason={cmd.Reason}"));
}
```

- [ ] **Step 2: Update probe projection mapping to coordinator semantic states**

```csharp
private string ResolveTranscriptViewportAutomationState()
{
    return _viewportCoordinator.State switch
    {
        TranscriptViewportState.Idle => "inactive",
        TranscriptViewportState.Settling => "pending",
        TranscriptViewportState.Following => "bottom",
        TranscriptViewportState.DetachedByUser => "not_bottom",
        TranscriptViewportState.Suspended => "loading",
        _ => "untracked"
    };
}
```

- [ ] **Step 3: Run full targeted validation set**

Run:

1. `dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~TranscriptViewportCoordinatorTests|FullyQualifiedName~ChatViewCodeBehind_DoesNotUseLegacyViewportDriftDetachHeuristic|FullyQualifiedName~MiniChatView_UsesCoordinatorBasedViewportPolicy"`  
Expected: PASS

2. `$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~SelectRemoteSessionWithSlowReplay_ViewportStateReportsBottomAfterHydration|FullyQualifiedName~SelectRemoteSessionWithSlowReplay_PageUpDetachesViewportAfterHydration|FullyQualifiedName~RemoteSlowReplay_ViewportJitterWithoutUserIntent_DoesNotDetachFromBottom"`  
Expected: PASS/Skip only when GUI gate is intentionally disabled.

- [ ] **Step 4: Run build gate for changed scope**

Run:  
`dotnet build SalmonEgg.sln -m:1 -nr:false`  

Expected: Build succeeds with no new warnings introduced by this change set.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Utilities/TranscriptViewportCoordinator.cs SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml.cs SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml
git commit -m "feat: project transcript viewport state from coordinator semantics"
```

---

## Spec-to-Plan Coverage Map

1. **Core coordinator owner + contracts:** Tasks 1-2
2. **ChatView adapter migration:** Task 3
3. **MiniChatView parity:** Task 4
4. **No-intent no-detach regression guard:** Task 5
5. **Observability + semantic probe state:** Task 6
6. **Phase migration with fallback switch:** Task 3 introduces switch; Task 6 finalizes semantics.

## Placeholder Scan Result

Manual scan result: no unresolved placeholders remain.

## Type Consistency Check

Checked consistency for:

1. `TranscriptViewportCoordinator`
2. `TranscriptViewportEvent` variants
3. `TranscriptViewportFact`
4. `TranscriptViewportState`
5. `TranscriptViewportCommandKind`

Names and signatures are consistent across tasks.
