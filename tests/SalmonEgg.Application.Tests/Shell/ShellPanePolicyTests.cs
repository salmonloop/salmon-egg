using SalmonEgg.Application.Common.Shell;

namespace SalmonEgg.Application.Tests.Shell;

public sealed class ShellPanePolicyTests
{
    [Fact]
    public void Default_Is_Open()
    {
        var policy = new ShellPanePolicy();
        Assert.True(policy.DefaultIsOpen);
    }

    [Fact]
    public void Toggle_Close_Allows_Exactly_One_Close_When_Not_Minimal()
    {
        var policy = new ShellPanePolicy();

        var next = policy.Toggle(currentIsOpen: true);
        Assert.False(next);

        // First close is user-initiated -> allow.
        Assert.False(policy.ShouldCancelClosing(isMinimalMode: false));

        // Subsequent closes without a user toggle should be cancelled.
        Assert.True(policy.ShouldCancelClosing(isMinimalMode: false));
        Assert.True(policy.ShouldCancelClosing(isMinimalMode: false));
    }

    [Fact]
    public void Minimal_Mode_Always_Allows_Close_And_Resets_OneShot_Close()
    {
        var policy = new ShellPanePolicy();

        _ = policy.Toggle(currentIsOpen: true); // request close

        // Minimal mode should not be prevented.
        Assert.False(policy.ShouldCancelClosing(isMinimalMode: true));

        // One-shot close should be reset by the minimal mode path.
        Assert.True(policy.ShouldCancelClosing(isMinimalMode: false));
    }
}

