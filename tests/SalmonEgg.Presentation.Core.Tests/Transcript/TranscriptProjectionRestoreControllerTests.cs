using System.Threading.Channels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Tests.Transcript;

public sealed class TranscriptProjectionRestoreControllerTests
{
    private static readonly TranscriptProjectionRestoreToken RestoreToken = new("conv-1", "msg:1");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveMaxAttempts_Throws(int maxAttempts)
    {
        // Act
        var exception = Record.Exception(() => new TranscriptProjectionRestoreController(maxAttempts));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Theory]
    [InlineData("conv-2", 7)]
    [InlineData("conv-1", 8)]
    public void TryApply_ContextOrGenerationChanged_ReturnsAbandoned(
        string currentConversationId,
        int currentGeneration)
    {
        // Arrange
        var controller = CreateController();
        var host = new FakeTranscriptViewportHost();
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var result = controller.TryApply(
            host,
            messageCount: 4,
            currentConversationId,
            currentGeneration,
            _ => 1);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Abandoned, result.Kind);
        Assert.Equal(RestoreToken.ConversationId, result.ConversationId);
        Assert.Equal(7, result.Generation);
        Assert.Equal("RestoreContextChanged", result.Reason);
        Assert.False(controller.HasPending);
    }

    [Fact]
    public void TryApply_ProjectionItemMissing_ReturnsUnavailable()
    {
        // Arrange
        var controller = CreateController();
        var host = new FakeTranscriptViewportHost();
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var result = controller.TryApply(
            host,
            messageCount: 4,
            RestoreToken.ConversationId,
            currentGeneration: 7,
            _ => -1);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Unavailable, result.Kind);
        Assert.Equal(RestoreToken.ConversationId, result.ConversationId);
        Assert.Equal(7, result.Generation);
        Assert.Equal("ProjectionItemMissing", result.Reason);
        Assert.Empty(host.ScrollRequests);
        Assert.False(controller.HasPending);
    }

    [Fact]
    public void TryApply_ItemNeverMaterializes_ReturnsUnavailableAfterMaxAttempts()
    {
        // Arrange
        var controller = CreateController(maxAttempts: 2);
        var host = new FakeTranscriptViewportHost();
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var firstResult = Apply(controller, host, _ => 2);
        var finalResult = Apply(controller, host, _ => 2);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Retry, firstResult.Kind);
        Assert.Equal(TranscriptProjectionRestoreResultKind.Unavailable, finalResult.Kind);
        Assert.Equal("ProjectionItemNotMaterialized", finalResult.Reason);
        Assert.Equal([(2, TranscriptItemScrollAlignment.Leading)], host.ScrollRequests);
        Assert.False(controller.HasPending);
    }

    [Fact]
    public void TryApply_RealizedAnchorNeverBecomesFirstVisible_ReturnsUnavailableAfterMaxAttempts()
    {
        // Arrange
        var controller = CreateController(maxAttempts: 2);
        var host = new FakeTranscriptViewportHost
        {
            HasRealizedItemHandler = index => index == 2,
            FirstVisibleIndex = 1
        };
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var firstResult = Apply(controller, host, _ => 2);
        var finalResult = Apply(controller, host, _ => 2);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Retry, firstResult.Kind);
        Assert.Equal(TranscriptProjectionRestoreResultKind.Unavailable, finalResult.Kind);
        Assert.Equal("ProjectionAnchorNotRestored", finalResult.Reason);
        Assert.Equal([(2, TranscriptItemScrollAlignment.Leading)], host.ScrollRequests);
        Assert.False(controller.HasPending);
    }

    [Fact]
    public void TryApply_AnchorRestored_ReturnsConfirmedResult()
    {
        // Arrange
        var controller = CreateController();
        var host = new FakeTranscriptViewportHost
        {
            HasRealizedItemHandler = index => index == 2,
            FirstVisibleIndex = 2
        };
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var result = Apply(controller, host, _ => 2);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Confirmed, result.Kind);
        Assert.Equal(RestoreToken, result.Token);
        Assert.Equal(RestoreToken.ConversationId, result.ConversationId);
        Assert.Equal(7, result.Generation);
        Assert.Null(result.Reason);
        Assert.Empty(host.ScrollRequests);
        Assert.False(controller.HasPending);
    }

    [Fact]
    public void TryApply_ProjectionReordered_ReResolvesItemIndex()
    {
        // Arrange
        var controller = CreateController(maxAttempts: 3);
        var currentIndex = 1;
        var resolveCallCount = 0;
        var host = new FakeTranscriptViewportHost
        {
            FirstVisibleIndexProvider = () => currentIndex
        };
        controller.Queue(RestoreToken, generation: 7);

        int ResolveIndex(TranscriptProjectionRestoreToken token)
        {
            Assert.Equal(RestoreToken, token);
            resolveCallCount++;
            return currentIndex;
        }

        host.HasRealizedItemHandler = _ => false;
        var firstResult = Apply(controller, host, ResolveIndex);

        // Act
        currentIndex = 2;
        host.HasRealizedItemHandler = index => index == currentIndex;
        var finalResult = Apply(controller, host, ResolveIndex);

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Retry, firstResult.Kind);
        Assert.Equal(TranscriptProjectionRestoreResultKind.Confirmed, finalResult.Kind);
        Assert.Equal(2, resolveCallCount);
        Assert.Equal([(1, TranscriptItemScrollAlignment.Leading)], host.ScrollRequests);
    }

    [Fact]
    public async Task TryScheduleRetry_AlreadyScheduled_DeduplicatesAndForwardsCallback()
    {
        // Arrange
        var controller = CreateController();
        var queuedCallback = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherQueue = new DispatcherQueue(callback => queuedCallback.TrySetResult(callback));
        var retryCount = 0;
        controller.Queue(RestoreToken, generation: 7);

        // Act
        var firstScheduled = controller.TryScheduleRetry(dispatcherQueue, () => retryCount++);
        var secondScheduled = controller.TryScheduleRetry(dispatcherQueue, () => retryCount++);
        var retry = await queuedCallback.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        retry();

        // Assert
        Assert.True(firstScheduled);
        Assert.False(secondScheduled);
        Assert.Equal(1, retryCount);
    }

    [Fact]
    public async Task TryScheduleRetry_ApplyBeforeQueuedCallbackRuns_KeepsRetryDeduplicated()
    {
        // Arrange
        var controller = CreateController();
        var host = new FakeTranscriptViewportHost();
        var queuedCallback = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherQueue = new DispatcherQueue(callback => queuedCallback.TrySetResult(callback));
        controller.Queue(RestoreToken, generation: 7);
        Assert.True(controller.TryScheduleRetry(dispatcherQueue, () => { }));
        _ = await queuedCallback.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        // Act
        var applyResult = Apply(controller, host, _ => 2);
        var duplicateScheduled = controller.TryScheduleRetry(dispatcherQueue, () => { });

        // Assert
        Assert.Equal(TranscriptProjectionRestoreResultKind.Retry, applyResult.Kind);
        Assert.False(duplicateScheduled);
    }

    [Fact]
    public async Task TryScheduleRetry_QueuedCallback_ReleasesSlotBeforeForwarding()
    {
        // Arrange
        var controller = CreateController();
        var queuedCallbacks = Channel.CreateUnbounded<Action>();
        var dispatcherQueue = new DispatcherQueue(queuedCallbacks.Writer.TryWrite);
        var rescheduled = false;
        controller.Queue(RestoreToken, generation: 7);
        Assert.True(controller.TryScheduleRetry(
            dispatcherQueue,
            () => rescheduled = controller.TryScheduleRetry(dispatcherQueue, () => { })));
        var queuedCallback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        // Act
        queuedCallback();

        // Assert
        Assert.True(rescheduled);
    }

    [Fact]
    public async Task Queue_NewRestore_InvalidatesQueuedRetryWithoutDisturbingNewSchedule()
    {
        // Arrange
        var controller = CreateController();
        var queuedCallbacks = Channel.CreateUnbounded<Action>();
        var dispatcherQueue = new DispatcherQueue(queuedCallbacks.Writer.TryWrite);
        var staleRetryCount = 0;
        var currentRetryCount = 0;
        controller.Queue(RestoreToken, generation: 7);
        Assert.True(controller.TryScheduleRetry(dispatcherQueue, () => staleRetryCount++));
        var staleCallback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        var currentToken = new TranscriptProjectionRestoreToken("conv-2", "msg:2");
        controller.Queue(currentToken, generation: 8);
        Assert.True(controller.TryScheduleRetry(dispatcherQueue, () => currentRetryCount++));
        var currentCallback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        // Act
        staleCallback();
        var duplicateScheduled = controller.TryScheduleRetry(dispatcherQueue, () => currentRetryCount++);
        currentCallback();

        // Assert
        Assert.Equal(0, staleRetryCount);
        Assert.False(duplicateScheduled);
        Assert.Equal(1, currentRetryCount);
    }

    private static TranscriptProjectionRestoreController CreateController(int maxAttempts = 3)
        => new(maxAttempts);

    private static async Task<Action> ReadQueuedCallbackAsync(ChannelReader<Action> reader)
        => await reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    private static TranscriptProjectionRestoreResult Apply(
        TranscriptProjectionRestoreController controller,
        ITranscriptViewportHost host,
        Func<TranscriptProjectionRestoreToken, int> resolveIndex)
        => controller.TryApply(
            host,
            messageCount: 4,
            RestoreToken.ConversationId,
            currentGeneration: 7,
            resolveIndex);

    private sealed class FakeTranscriptViewportHost : ITranscriptViewportHost
    {
        public Func<int, bool> HasRealizedItemHandler { get; set; } = _ => false;

        public Func<int>? FirstVisibleIndexProvider { get; set; }

        public int? FirstVisibleIndex { get; set; }

        public List<(int Index, TranscriptItemScrollAlignment Alignment)> ScrollRequests { get; } = [];

        public event EventHandler? ViewportChanged
        {
            add { }
            remove { }
        }

        public bool HasRealizedItem(int index)
            => HasRealizedItemHandler(index);

        public bool TryGetFirstVisibleIndex(int itemCount, out int index)
        {
            var resolvedIndex = FirstVisibleIndexProvider?.Invoke() ?? FirstVisibleIndex;
            index = resolvedIndex ?? -1;
            return resolvedIndex.HasValue;
        }

        public void ScrollItemIntoView(
            int index,
            TranscriptItemScrollAlignment alignment = TranscriptItemScrollAlignment.Default)
            => ScrollRequests.Add((index, alignment));

        public bool TryFocusItem(int index, FocusState focusState)
            => false;

        public bool TryScrollByItems(int itemDelta)
            => false;

        public bool TryScrollByPages(int pageDelta)
            => false;

        public bool TryFocusViewport(FocusState focusState)
            => false;

        public void ScrollToEnd()
        {
        }

        public bool IsAtBottom(int itemCount, double bottomThreshold, double bottomGeometryTolerance)
            => false;

        public void Dispose()
        {
        }
    }
}
