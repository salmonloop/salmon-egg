using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class InitialLayoutLoadingPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, false)]
    public void ShouldKeepLoading_TracksOnlyActiveHydration(
        bool isSessionActive,
        bool isHydrating,
        bool isRemoteHydrationPending,
        bool expected)
    {
        // Act
        var actual = InitialLayoutLoadingPolicy.ShouldKeepLoading(
            isSessionActive,
            isHydrating,
            isRemoteHydrationPending);

        // Assert
        Assert.Equal(expected, actual);
    }
}
