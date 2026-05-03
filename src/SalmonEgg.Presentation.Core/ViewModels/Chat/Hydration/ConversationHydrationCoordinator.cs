using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.ViewModels.Chat.Hydration;

internal sealed class ConversationHydrationCoordinator
{
    private readonly ConversationHydrationCoordinatorOptions _options;

    public ConversationHydrationCoordinator(ConversationHydrationCoordinatorOptions options)
    {
        _options = options;
    }

    public async Task AwaitRemoteReplayProjectionAsync(
        ConversationHydrationContext context,
        string conversationId,
        long? activationVersion,
        string remoteSessionId,
        long replayBaseline,
        long transcriptProjectionBaseline,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();
        await context.SetHydrationPhaseAsync(
                conversationId,
                activationVersion,
                ConversationHydrationPhase.AwaitingReplayStart)
            .ConfigureAwait(false);

        var replayStartTimeoutAt = DateTime.UtcNow + _options.ReplayStartTimeout;
        while (context.GetSessionUpdateObservationCount(remoteSessionId) <= replayBaseline
            && DateTime.UtcNow < replayStartTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.PollDelay, cancellationToken).ConfigureAwait(false);
        }

        if (context.GetSessionUpdateObservationCount(remoteSessionId) > replayBaseline)
        {
            await context.SetHydrationPhaseAsync(
                    conversationId,
                    activationVersion,
                    ConversationHydrationPhase.ReplayingSessionUpdates)
                .ConfigureAwait(false);
        }

        var transcriptTimeoutAt = DateTime.UtcNow + _options.ReplayStartTimeout;
        while (context.GetTranscriptProjectionObservationCount(remoteSessionId) <= transcriptProjectionBaseline
            && DateTime.UtcNow < transcriptTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.PollDelay, cancellationToken).ConfigureAwait(false);
        }

        if (context.GetTranscriptProjectionObservationCount(remoteSessionId) > transcriptProjectionBaseline)
        {
            await context.SetHydrationPhaseAsync(
                    conversationId,
                    activationVersion,
                    ConversationHydrationPhase.ProjectingTranscript)
                .ConfigureAwait(false);
            await context.SetHydrationPhaseAsync(
                    conversationId,
                    activationVersion,
                    ConversationHydrationPhase.SettlingReplay)
                .ConfigureAwait(false);
            await AwaitRemoteReplaySettleQuietPeriodAsync(context, remoteSessionId, replayBaseline, cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SetHydrationPhaseAsync(
                conversationId,
                activationVersion,
                ConversationHydrationPhase.FinalizingProjection)
            .ConfigureAwait(false);
        await context.AwaitBufferedReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
    }

    public async Task AwaitKnownTranscriptGrowthRequirementAsync(
        ConversationHydrationContext context,
        string conversationId,
        int transcriptBaselineCount,
        DateTime graceDeadlineUtc,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (DateTime.UtcNow < graceDeadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await context.AwaitBufferedReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
            var projectedTranscriptCount = await context.GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
            if (projectedTranscriptCount > transcriptBaselineCount)
            {
                return;
            }

            var remaining = graceDeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = remaining < _options.PollDelay
                ? remaining
                : _options.PollDelay;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task AwaitBufferedReplayProjectionAsync(
        ConversationHydrationContext context,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        await context.YieldToUiAsync().ConfigureAwait(false);
        if (hydrationAttemptId.HasValue)
        {
            await context.WaitForAdapterDrainAsync(hydrationAttemptId.Value, cancellationToken)
                .WaitAsync(_options.ReplayDrainTimeout, cancellationToken)
                .ConfigureAwait(false);

            await context.YieldToUiAsync().ConfigureAwait(false);
        }

        var pendingUpdatesTask = context.WaitForPendingSessionUpdatesAsync();
        if (!pendingUpdatesTask.IsCompleted)
        {
            await pendingUpdatesTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await context.YieldToUiAsync().ConfigureAwait(false);
    }

    public async Task AwaitRemoteReplaySettleQuietPeriodAsync(
        ConversationHydrationContext context,
        string remoteSessionId,
        long replayBaseline,
        CancellationToken cancellationToken)
    {
        if (context.GetSessionUpdateObservationCount(remoteSessionId) <= replayBaseline)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastObservedAtUtc = context.GetSessionUpdateLastObservedAtUtc(remoteSessionId);
            if (!lastObservedAtUtc.HasValue)
            {
                return;
            }

            var quietRemaining = (lastObservedAtUtc.Value + _options.ReplaySettleQuietPeriod) - DateTime.UtcNow;
            if (quietRemaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = quietRemaining < _options.PollDelay
                ? quietRemaining
                : _options.PollDelay;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
