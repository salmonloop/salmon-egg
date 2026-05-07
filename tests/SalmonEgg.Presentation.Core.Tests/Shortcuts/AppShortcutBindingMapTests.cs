using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.Shortcuts;

namespace SalmonEgg.Presentation.Core.Tests.Shortcuts;

public sealed class AppShortcutBindingMapTests
{
    [Fact]
    public void Create_UsesSavedBindingOverridesToResolveActions()
    {
        var bindings = new Dictionary<string, string>
        {
            ["search"] = "Alt+K"
        };

        var map = AppShortcutBindingMap.Create(bindings);

        Assert.True(AppShortcutGesture.TryParse("Alt+K", out var overriddenGesture));
        Assert.True(map.TryResolveActionId(overriddenGesture, out var actionId));
        Assert.Equal(AppShortcutActionIds.Search, actionId);

        Assert.True(AppShortcutGesture.TryParse("Ctrl+K", out var defaultGesture));
        Assert.False(map.TryResolveActionId(defaultGesture, out _));
    }

    [Fact]
    public void Create_IgnoresUnsupportedActionsAndConflictingGestures()
    {
        var bindings = new Dictionary<string, string>
        {
            ["search"] = "Ctrl+N",
            ["toggle_right_pane"] = "Ctrl+L"
        };

        var map = AppShortcutBindingMap.Create(bindings);

        Assert.True(AppShortcutGesture.TryParse("Ctrl+L", out var removedGesture));
        Assert.False(map.TryResolveActionId(removedGesture, out _));

        Assert.True(AppShortcutGesture.TryParse("Ctrl+N", out var conflictedGesture));
        Assert.False(map.TryResolveActionId(conflictedGesture, out _));
    }
}
