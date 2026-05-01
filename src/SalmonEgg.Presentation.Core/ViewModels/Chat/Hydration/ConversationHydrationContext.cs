using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.ViewModels.Chat.Hydration;

internal sealed class ConversationHydrationContext
{
    public required Func<string, long?, ConversationHydrationPhase, Task> SetHydrationPhaseAsync { get; init; }

    public required Func<string, long> GetSessionUpdateObservationCount { get; init; }

    public required Func<string, long> GetTranscriptProjectionObservationCount { get; init; }

    public required Func<string, DateTime?> GetSessionUpdateLastObservedAtUtc { get; init; }

    public required Func<CancellationToken, long?, Task> AwaitBufferedReplayProjectionAsync { get; init; }

    public required Func<string, Task<int>> GetProjectedTranscriptCountAsync { get; init; }

    public required Func<Task> YieldToUiAsync { get; init; }

    public required Func<long, CancellationToken, Task> WaitForAdapterDrainAsync { get; init; }

    public required Func<Task> WaitForPendingSessionUpdatesAsync { get; init; }
}
