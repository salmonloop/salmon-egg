using SalmonEgg.Application.Common.Shell;

namespace SalmonEgg.Presentation.Core.Tests.Shell;

public sealed class ShellPanePolicyTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void ShouldCancelClosing_OnlyBlocksExpandedPaneDrift(bool desiredPaneOpen, bool isExpandedMode, bool expected)
    {
        var shouldCancel = ShellPanePolicy.ShouldCancelClosing(desiredPaneOpen, isExpandedMode);

        Assert.Equal(expected, shouldCancel);
    }
}
