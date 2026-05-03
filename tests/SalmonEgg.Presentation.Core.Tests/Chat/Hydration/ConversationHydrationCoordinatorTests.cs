using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.ViewModels.Chat.Hydration;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Hydration;

public sealed class ConversationHydrationCoordinatorTests
{
    [Fact]
    public async Task AwaitRemoteReplayProjectionAsync_WhenReplayAndProjectionObserved_TransitionsThroughReplayPhases()
    {
        var phases = new List<ConversationHydrationPhase>();
        long replayCount = 0;
        long projectionCount = 0;
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(20),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));

        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = (_, _, phase) =>
            {
                phases.Add(phase);
                return Task.CompletedTask;
            },
            GetSessionUpdateObservationCount = _ => replayCount,
            GetTranscriptProjectionObservationCount = _ => projectionCount,
            GetSessionUpdateLastObservedAtUtc = _ => DateTime.UtcNow - TimeSpan.FromMilliseconds(50),
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = static () => Task.CompletedTask,
            WaitForAdapterDrainAsync = static (_, _) => Task.CompletedTask,
            WaitForPendingSessionUpdatesAsync = static () => Task.CompletedTask
        };

        var task = coordinator.AwaitRemoteReplayProjectionAsync(
            context,
            conversationId: "conv-1",
            activationVersion: 7,
            remoteSessionId: "remote-1",
            replayBaseline: 0,
            transcriptProjectionBaseline: 0,
            hydrationAttemptId: 11,
            CancellationToken.None);

        await Task.Delay(30);
        replayCount = 1;
        await Task.Delay(30);
        projectionCount = 1;

        await task;

        Assert.Equal(
            [
                ConversationHydrationPhase.AwaitingReplayStart,
                ConversationHydrationPhase.ReplayingSessionUpdates,
                ConversationHydrationPhase.ProjectingTranscript,
                ConversationHydrationPhase.SettlingReplay,
                ConversationHydrationPhase.FinalizingProjection
            ],
            phases);
    }

    [Fact]
    public async Task AwaitKnownTranscriptGrowthRequirementAsync_WhenTranscriptCountGrows_CompletesBeforeDeadline()
    {
        int projectedTranscriptCount = 0;
        var awaitBufferedCalls = 0;
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(20),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));

        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 0,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = static _ => null,
            AwaitBufferedReplayProjectionAsync = (_, _) =>
            {
                awaitBufferedCalls++;
                if (awaitBufferedCalls >= 2)
                {
                    projectedTranscriptCount = 3;
                }

                return Task.CompletedTask;
            },
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(projectedTranscriptCount),
            YieldToUiAsync = static () => Task.CompletedTask,
            WaitForAdapterDrainAsync = static (_, _) => Task.CompletedTask,
            WaitForPendingSessionUpdatesAsync = static () => Task.CompletedTask
        };

        await coordinator.AwaitKnownTranscriptGrowthRequirementAsync(
            context,
            conversationId: "conv-1",
            transcriptBaselineCount: 0,
            graceDeadlineUtc: DateTime.UtcNow + TimeSpan.FromMilliseconds(200),
            hydrationAttemptId: 4,
            cancellationToken: CancellationToken.None);

        Assert.True(awaitBufferedCalls >= 2);
        Assert.Equal(3, projectedTranscriptCount);
    }

    [Fact]
    public async Task AwaitRemoteReplaySettleQuietPeriodAsync_WhenReplayAlreadyQuiet_CompletesImmediately()
    {
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(40),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));
        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 2,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = static _ => DateTime.UtcNow - TimeSpan.FromMilliseconds(80),
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = static () => Task.CompletedTask,
            WaitForAdapterDrainAsync = static (_, _) => Task.CompletedTask,
            WaitForPendingSessionUpdatesAsync = static () => Task.CompletedTask
        };

        var stopwatch = Stopwatch.StartNew();
        await coordinator.AwaitRemoteReplaySettleQuietPeriodAsync(
            context,
            remoteSessionId: "remote-1",
            replayBaseline: 1,
            cancellationToken: CancellationToken.None);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task AwaitRemoteReplaySettleQuietPeriodAsync_WhenReplayWasRecent_WaitsForQuietWindow()
    {
        var observedAtUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(10);
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(50),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));
        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 2,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = _ => observedAtUtc,
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = static () => Task.CompletedTask,
            WaitForAdapterDrainAsync = static (_, _) => Task.CompletedTask,
            WaitForPendingSessionUpdatesAsync = static () => Task.CompletedTask
        };

        var stopwatch = Stopwatch.StartNew();
        await coordinator.AwaitRemoteReplaySettleQuietPeriodAsync(
            context,
            remoteSessionId: "remote-1",
            replayBaseline: 1,
            cancellationToken: CancellationToken.None);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task AwaitRemoteReplayProjectionAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromSeconds(1),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(20),
                PollDelay: TimeSpan.FromMilliseconds(10),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));
        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 0,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = static _ => null,
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = static () => Task.CompletedTask,
            WaitForAdapterDrainAsync = static (_, _) => Task.CompletedTask,
            WaitForPendingSessionUpdatesAsync = static () => Task.CompletedTask
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.AwaitRemoteReplayProjectionAsync(
                context,
                conversationId: "conv-1",
                activationVersion: 1,
                remoteSessionId: "remote-1",
                replayBaseline: 0,
                transcriptProjectionBaseline: 0,
                hydrationAttemptId: null,
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task AwaitBufferedReplayProjectionAsync_WhenAdapterDrainAndPendingUpdatesExist_WaitsInExpectedOrder()
    {
        var calls = new List<string>();
        var pendingTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(20),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));
        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 0,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = static _ => null,
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = () =>
            {
                calls.Add("ui");
                return Task.CompletedTask;
            },
            WaitForAdapterDrainAsync = (attemptId, _) =>
            {
                calls.Add($"adapter:{attemptId}");
                return Task.CompletedTask;
            },
            WaitForPendingSessionUpdatesAsync = () =>
            {
                calls.Add("pending");
                return pendingTcs.Task;
            }
        };

        var waitTask = coordinator.AwaitBufferedReplayProjectionAsync(
            context,
            hydrationAttemptId: 7,
            cancellationToken: CancellationToken.None);

        Assert.False(waitTask.IsCompleted);
        pendingTcs.SetResult(null);
        await waitTask;

        Assert.Equal(
            ["ui", "adapter:7", "ui", "pending", "ui"],
            calls);
    }

    [Fact]
    public async Task AwaitBufferedReplayProjectionAsync_WhenNoAdapterAttempt_SkipsAdapterDrainAndStillYieldsUi()
    {
        var calls = new List<string>();
        var coordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: TimeSpan.FromMilliseconds(300),
                ReplaySettleQuietPeriod: TimeSpan.FromMilliseconds(20),
                PollDelay: TimeSpan.FromMilliseconds(5),
                ReplayDrainTimeout: TimeSpan.FromMilliseconds(200)));
        var context = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = static (_, _, _) => Task.CompletedTask,
            GetSessionUpdateObservationCount = static _ => 0,
            GetTranscriptProjectionObservationCount = static _ => 0,
            GetSessionUpdateLastObservedAtUtc = static _ => null,
            AwaitBufferedReplayProjectionAsync = (_, _) => Task.CompletedTask,
            GetProjectedTranscriptCountAsync = _ => Task.FromResult(0),
            YieldToUiAsync = () =>
            {
                calls.Add("ui");
                return Task.CompletedTask;
            },
            WaitForAdapterDrainAsync = (attemptId, _) =>
            {
                calls.Add($"adapter:{attemptId}");
                return Task.CompletedTask;
            },
            WaitForPendingSessionUpdatesAsync = () =>
            {
                calls.Add("pending");
                return Task.CompletedTask;
            }
        };

        await coordinator.AwaitBufferedReplayProjectionAsync(
            context,
            hydrationAttemptId: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["ui", "pending", "ui"], calls);
    }
}
