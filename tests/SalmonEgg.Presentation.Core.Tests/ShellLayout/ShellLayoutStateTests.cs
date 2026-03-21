using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutStateTests
{
    [Fact]
    public void ShellLayoutState_Defaults_AreStable()
    {
        var state = ShellLayoutState.Default;
        Assert.True(state.WindowMetrics.Width > 0);
        Assert.True(state.RightPanelPreferredWidth > 0);
        Assert.True(state.TitleBarInsetsHeight > 0);
        Assert.Null(state.UserNavOpenIntent);
        Assert.Equal(RightPanelMode.None, state.DesiredRightPanelMode);
        Assert.Equal(BottomPanelMode.None, state.DesiredBottomPanelMode);
    }
}
