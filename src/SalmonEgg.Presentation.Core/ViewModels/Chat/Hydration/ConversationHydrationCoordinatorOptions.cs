using System;

namespace SalmonEgg.Presentation.ViewModels.Chat.Hydration;

internal sealed record ConversationHydrationCoordinatorOptions(
    TimeSpan ReplayStartTimeout,
    TimeSpan ReplaySettleQuietPeriod,
    TimeSpan PollDelay,
    TimeSpan MinimumVisibleDuration,
    TimeSpan ReplayDrainTimeout);
