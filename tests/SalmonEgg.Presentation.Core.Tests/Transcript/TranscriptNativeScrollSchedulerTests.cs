using System.Threading.Channels;
using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Tests.Transcript;

public sealed class TranscriptNativeScrollSchedulerTests
{
    [Fact]
    public async Task Schedule_WhileCallbackPending_CoalescesLatestToken()
    {
        // Arrange
        var scheduler = new TranscriptNativeScrollScheduler();
        var queuedCallback = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueueCount = 0;
        var dispatcherQueue = new DispatcherQueue(callback =>
        {
            enqueueCount++;
            return queuedCallback.TrySetResult(callback);
        });
        var receivedTokens = new List<TranscriptScrollRequestToken>();

        // Act
        var firstResult = scheduler.Schedule(dispatcherQueue, Token(1), receivedTokens.Add);
        var secondResult = scheduler.Schedule(dispatcherQueue, Token(2), receivedTokens.Add);
        var callback = await queuedCallback.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        callback();

        // Assert
        Assert.Equal(TranscriptNativeScrollScheduleResult.Scheduled, firstResult);
        Assert.Equal(TranscriptNativeScrollScheduleResult.Coalesced, secondResult);
        Assert.Equal(1, enqueueCount);
        Assert.Equal([Token(2)], receivedTokens);
    }

    [Fact]
    public async Task Clear_OldCallbackAfterNewSchedule_DoesNotConsumeNewSchedule()
    {
        // Arrange
        var scheduler = new TranscriptNativeScrollScheduler();
        var queuedCallbacks = Channel.CreateUnbounded<Action>();
        var dispatcherQueue = new DispatcherQueue(queuedCallbacks.Writer.TryWrite);
        var receivedTokens = new List<TranscriptScrollRequestToken>();
        Assert.Equal(
            TranscriptNativeScrollScheduleResult.Scheduled,
            scheduler.Schedule(dispatcherQueue, Token(1), receivedTokens.Add));
        var staleCallback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        scheduler.Clear();
        Assert.Equal(
            TranscriptNativeScrollScheduleResult.Scheduled,
            scheduler.Schedule(dispatcherQueue, Token(2), receivedTokens.Add));
        var currentCallback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        // Act
        staleCallback();
        var tokensAfterStaleCallback = receivedTokens.ToArray();
        currentCallback();

        // Assert
        Assert.Empty(tokensAfterStaleCallback);
        Assert.Equal([Token(2)], receivedTokens);
    }

    [Fact]
    public void Schedule_EnqueueRejected_AllowsSubsequentSchedule()
    {
        // Arrange
        var scheduler = new TranscriptNativeScrollScheduler();
        var rejectingDispatcherQueue = new DispatcherQueue(_ => false);
        var acceptingDispatcherQueue = new DispatcherQueue(_ => true);

        // Act
        var rejectedResult = scheduler.Schedule(rejectingDispatcherQueue, Token(1), _ => { });
        var scheduledResult = scheduler.Schedule(acceptingDispatcherQueue, Token(2), _ => { });

        // Assert
        Assert.Equal(TranscriptNativeScrollScheduleResult.Rejected, rejectedResult);
        Assert.Equal(TranscriptNativeScrollScheduleResult.Scheduled, scheduledResult);
    }

    [Fact]
    public async Task Schedule_CallbackRuns_ReleasesSlotBeforeForwarding()
    {
        // Arrange
        var scheduler = new TranscriptNativeScrollScheduler();
        var queuedCallbacks = Channel.CreateUnbounded<Action>();
        var dispatcherQueue = new DispatcherQueue(queuedCallbacks.Writer.TryWrite);
        var nestedResult = TranscriptNativeScrollScheduleResult.Rejected;
        Assert.Equal(
            TranscriptNativeScrollScheduleResult.Scheduled,
            scheduler.Schedule(
                dispatcherQueue,
                Token(1),
                _ => nestedResult = scheduler.Schedule(dispatcherQueue, Token(2), _ => { })));
        var callback = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);

        // Act
        callback();

        // Assert
        Assert.Equal(TranscriptNativeScrollScheduleResult.Scheduled, nestedResult);
        _ = await ReadQueuedCallbackAsync(queuedCallbacks.Reader);
    }

    private static TranscriptScrollRequestToken Token(long requestGeneration)
        => new(ActivationGeneration: 1, requestGeneration, ConversationId: "conv-1");

    private static async Task<Action> ReadQueuedCallbackAsync(ChannelReader<Action> reader)
        => await reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
}
