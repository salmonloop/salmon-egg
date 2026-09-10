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
    public async Task PermissionNotification_SelectionInFlightThenPeerCancellation_AdvancesQueueWithoutDoubleResponse(bool writeSucceeds)
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
        var nextVisible = NewPermissionSignal();
        PropertyChangedEventHandler projectionObserver = (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewModel.PendingPermissionRequest)
                && fixture.ViewModel.PendingPermissionRequest?.MessageId.ToString() == "second")
                nextVisible.TrySetResult(true);
        };
        fixture.ViewModel.PropertyChanged += projectionObserver;
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
            await AwaitPermissionUiSignalAsync(dispatcher, nextVisible.Task);

            // Act
            peer.CancelPermission("first");
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
            fixture.ViewModel.PropertyChanged -= projectionObserver;
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

    private static TaskCompletionSource<bool> NewPermissionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task AwaitPermissionUiSignalAsync(QueueingSynchronizationContext dispatcher, Task signal)
        => AwaitWithSynchronizationContextAsync(dispatcher,
            signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
}
