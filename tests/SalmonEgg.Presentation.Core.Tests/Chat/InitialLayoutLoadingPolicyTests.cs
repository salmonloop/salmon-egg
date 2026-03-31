using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class InitialLayoutLoadingPolicyTests
{
    [Theory]
    [InlineData(true, 3, true, false, false, false, true)]
    [InlineData(true, 3, false, false, false, false, false)]
    [InlineData(true, 3, true, true, false, false, false)]
    [InlineData(true, 0, true, false, false, false, false)]
    [InlineData(false, 3, true, false, false, false, false)]
    [InlineData(true, 0, true, false, true, false, true)] // Hydrating should keep loading
    [InlineData(true, 0, true, false, false, true, true)] // Remote hydration pending should keep loading
    public void ShouldKeepLoading_UsesPendingInitialScrollAsTheFallbackExitGate(
        bool isSessionActive,
        int messageCount,
        bool hasPendingInitialScroll,
        bool lastItemContainerGenerated,
        bool isHydrating,
        bool isRemoteHydrationPending,
        bool expected)
    {
        var actual = InitialLayoutLoadingPolicy.ShouldKeepLoading(
            isSessionActive,
            messageCount,
            hasPendingInitialScroll,
            lastItemContainerGenerated,
            isHydrating,
            isRemoteHydrationPending);

        Assert.Equal(expected, actual);
    }
}
