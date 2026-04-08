using SalmonEgg.Application.Common.Shell;

namespace SalmonEgg.Application.Tests.Shell;

public sealed class ShellPanePolicyTests
{
    [Fact]
    public void Closing_Is_Cancelled_When_Shell_Still_Wants_Pane_Open()
    {
        var cancel = ShellPanePolicy.ShouldCancelClosing(
            desiredPaneOpen: true,
            isExpandedMode: true);

        Assert.True(cancel);
    }

    [Fact]
    public void Closing_Is_Allowed_When_Shell_Already_Requested_Close()
    {
        var cancel = ShellPanePolicy.ShouldCancelClosing(
            desiredPaneOpen: false,
            isExpandedMode: true);

        Assert.False(cancel);
    }

    [Fact]
    public void Minimal_Mode_Always_Allows_Close()
    {
        var cancel = ShellPanePolicy.ShouldCancelClosing(
            desiredPaneOpen: true,
            isExpandedMode: false);

        Assert.False(cancel);
    }

    [Fact]
    public void Compact_Mode_Allows_Close_WhenShellStillWantsOpen()
    {
        var cancel = ShellPanePolicy.ShouldCancelClosing(
            desiredPaneOpen: true,
            isExpandedMode: false);

        Assert.False(cancel);
    }
}
