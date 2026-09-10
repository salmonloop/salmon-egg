using System.ComponentModel;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionNotification_CancellationWriteFails_ShowsNeutralExplicitCancellationRetry(bool peerCancels)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "Permission_Cancelled", "此请求已取消，请重试取消。");
        await using var fixture = CreateViewModel(dispatcher, localizer: localizer);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var prompt = fixture.ViewModel.PendingPermissionRequest!;
        var obsoleteAllow = prompt.Options[0];
        var wireRequest = Assert.Single(peer.Requests);
        var retryProjected = NewPermissionSignal();
        var changedOffUi = false;
        PropertyChangedEventHandler projectionObserver = (_, args) =>
        {
            if (args.PropertyName != nameof(PermissionRequestViewModel.Title)) return;
            if (!dispatcher.HasThreadAccess) changedOffUi = true;
            if (wireRequest.IsCancellationRequested) retryProjected.TrySetResult(true);
        };
        prompt.PropertyChanged += projectionObserver;
        var attempts = 0;
        peer.ResponseSend = (_, _) => { attempts++; return Task.FromResult(false); };
        try
        {
            // Act
            if (peerCancels) peer.CancelPermission("permission");
            else await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(null));
            await AwaitPermissionUiSignalAsync(dispatcher, retryProjected.Task);

            // Assert: the existing SDK request owns a visible retry without a store refresh.
            Assert.Same(prompt, fixture.ViewModel.PendingPermissionRequest);
            Assert.Same(prompt, fixture.ViewModel.StandalonePermissionRequest);
            Assert.Single(prompt.Options);
            Assert.False(prompt.Options[0].IsAllow);
            Assert.Equal("此请求已取消，请重试取消。", prompt.Description);
            Assert.False(changedOffUi);
            Assert.Equal(1, attempts);
            Assert.Empty(peer.Responses);
            peer.ResponseSend = null;
            await AwaitWithSynchronizationContextAsync(dispatcher, obsoleteAllow.SelectCommand.ExecuteAsync(null));
            var response = Assert.Single(peer.Responses);
            if (peerCancels) Assert.Equal(JsonRpcErrorCode.Cancelled, response.GetProperty("error").GetProperty("code").GetInt32());
            else Assert.Equal("cancelled", response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        }
        finally
        {
            prompt.PropertyChanged -= projectionObserver;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionNotification_SelectionInFlightThenPeerCancellation_AdvancesAfterDeliveryWithoutDoubleResponse(bool writeSucceeds)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("first", "remote-1", "tool-one");
        peer.Request("second", "remote-1", "tool-two");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        peer.ResponseSend = (response, token) =>
        {
            if (response.GetProperty("id").GetString() != "first" || !response.TryGetProperty("result", out _))
                return Task.FromResult(true);
            started.TrySetResult(true);
            return release.Task.WaitAsync(token);
        };
        var answer = first.RespondCommand.ExecuteAsync(first.Options[0]);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await dispatcher.RunUntilIdleAsync();
            Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
            Assert.True(first.RespondCommand.IsRunning);

            // Act
            peer.CancelPermission("first");
            await dispatcher.RunUntilIdleAsync();
            Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
            release.TrySetResult(writeSucceeds);
            await AwaitPermissionUiSignalAsync(dispatcher, answer);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            var terminal = Assert.Single(peer.Responses);
            Assert.Equal("first", terminal.GetProperty("id").GetString());
            Assert.Equal(!writeSucceeds, terminal.TryGetProperty("error", out _));
            Assert.Equal("second", fixture.ViewModel.PendingPermissionRequest!.MessageId.ToString());
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.ViewModel.PendingPermissionRequest.RespondCommand.ExecuteAsync(null));
            Assert.Equal(2, peer.Responses.Count);
            Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        }
        finally
        {
            release.TrySetResult(writeSucceeds);
            await AwaitPermissionUiSignalAsync(dispatcher, answer);
        }
    }

    [Fact]
    public async Task PermissionNotification_OriginalClientDisconnects_RemovesPromptWithoutStoreProjection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var removed = NewPermissionSignal();
        PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewModel.PendingPermissionRequest)
                && fixture.ViewModel.PendingPermissionRequest is null) removed.TrySetResult(true);
        };
        fixture.ViewModel.PropertyChanged += observer;
        try
        {
            // Act
            await peer.DisconnectClientAsync();
            await AwaitPermissionUiSignalAsync(dispatcher, removed.Task);

            // Assert
            Assert.Null(fixture.ViewModel.StandalonePermissionRequest);
            Assert.False(fixture.ViewModel.ShowPermissionDialog);
            Assert.Empty(peer.Responses);
        }
        finally
        {
            fixture.ViewModel.PropertyChanged -= observer;
        }
    }

    [Fact]
    public async Task PermissionNotification_QueuedBeforeServiceReplacement_DoesNotChangeOldOrReplacementPrompt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        oldPeer.Request("permission", "remote-1", "old-tool");
        await dispatcher.RunUntilIdleAsync();
        var oldPrompt = fixture.ViewModel.PendingPermissionRequest!;
        var oldTitle = oldPrompt.Title;
        var oldOption = oldPrompt.Options[0];
        var request = Assert.Single(oldPeer.Requests);
        var queued = NewPermissionSignal();
        var settled = NewPermissionSignal();
        EventHandler observer = (_, _) =>
        {
            if (request.IsCancellationRequested) queued.TrySetResult(true);
            if (!request.CanRespond) settled.TrySetResult(true);
        };
        request.Changed += observer;
        oldPeer.ResponseSend = (_, _) => Task.FromResult(false);
        try
        {
            // The SDK invokes this observer after the host's subscriber queued its UI work.
            oldPeer.CancelPermission("permission");
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(dispatcher.PendingCount > 0);

            // Act: replace on the UI thread before pumping the already queued old notification.
            RunPermissionUiAction(dispatcher, () => fixture.ViewModel.ReplaceChatService(currentPeer.Service));
            Assert.Null(oldPrompt.UnsubscribeRequestChanges);
            currentPeer.Request("permission", "remote-1", "new-tool");
            await dispatcher.RunUntilIdleAsync();
            var currentPrompt = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
            oldPeer.ResponseSend = null;
            Assert.True(await request.TryRespondAsync("cancelled"));
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await dispatcher.RunUntilIdleAsync();

            // Assert: neither the captured notification nor a later SDK change may revive the old UI.
            Assert.Equal(oldTitle, oldPrompt.Title);
            Assert.Same(oldOption, Assert.Single(oldPrompt.Options));
            Assert.NotSame(oldPrompt, currentPrompt);
            Assert.Contains("new-tool", currentPrompt.ToolCallJson);
            Assert.Same(currentPrompt, fixture.ViewModel.PendingPermissionRequest);
            Assert.Empty(currentPeer.Responses);
            await AwaitWithSynchronizationContextAsync(dispatcher,
                currentPrompt.RespondCommand.ExecuteAsync(currentPrompt.Options[0]));
            Assert.Equal("allow", Assert.Single(currentPeer.Responses).GetProperty("result")
                .GetProperty("outcome").GetProperty("optionId").GetString());
        }
        finally
        {
            request.Changed -= observer;
        }
    }

    [Fact]
    public async Task PermissionNotification_QueuedBeforeDisposal_DetachesAndDoesNotMutateDisposedProjection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var prompt = fixture.ViewModel.PendingPermissionRequest!;
        var title = prompt.Title;
        var option = prompt.Options[0];
        Assert.NotNull(prompt.UnsubscribeRequestChanges);
        var request = Assert.Single(peer.Requests);
        var queued = NewPermissionSignal();
        var settled = NewPermissionSignal();
        EventHandler observer = (_, _) =>
        {
            if (request.IsCancellationRequested) queued.TrySetResult(true);
            if (!request.CanRespond) settled.TrySetResult(true);
        };
        request.Changed += observer;
        peer.ResponseSend = (_, _) => Task.FromResult(false);
        try
        {
            peer.CancelPermission("permission");
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(dispatcher.PendingCount > 0);

            // Act: disposal retires the UI subscriber before its captured callback can run.
            RunPermissionUiAction(dispatcher, fixture.ViewModel.Dispose);
            Assert.Null(prompt.UnsubscribeRequestChanges);
            await dispatcher.RunUntilIdleAsync();
            peer.ResponseSend = null;
            Assert.True(await request.TryRespondAsync("cancelled"));
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await dispatcher.RunUntilIdleAsync();

            // Assert: the old projection stays unchanged while the original SDK owner settles.
            Assert.Equal(title, prompt.Title);
            Assert.Same(option, Assert.Single(prompt.Options));
            Assert.Null(prompt.UnsubscribeRequestChanges);
            Assert.Equal(JsonRpcErrorCode.Cancelled,
                Assert.Single(peer.Responses).GetProperty("error").GetProperty("code").GetInt32());
        }
        finally
        {
            request.Changed -= observer;
        }
    }

    private static void RunPermissionUiAction(QueueingSynchronizationContext dispatcher, Action action)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(dispatcher);
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static TaskCompletionSource<bool> NewPermissionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task AwaitPermissionUiSignalAsync(QueueingSynchronizationContext dispatcher, Task signal)
        => AwaitWithSynchronizationContextAsync(dispatcher,
            signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
}
