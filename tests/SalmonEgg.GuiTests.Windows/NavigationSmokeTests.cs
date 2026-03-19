using System;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed class NavigationSmokeTests
{
    [SkippableFact]
    public void Launch_ShowsMainNav_AndStartIsSelected()
    {
        using var session = WindowsGuiAppSession.LaunchOrAttach();

        var mainNav = session.FindByAutomationId("MainNavView");
        var startItem = session.FindByAutomationId("MainNav.Start");
        var startTitle = session.FindByAutomationId("StartView.Title");

        Assert.NotNull(mainNav);
        Assert.NotNull(startTitle);

        var selectionItem = startItem.Patterns.SelectionItem.Pattern;
        Assert.True(selectionItem.IsSelected.Value);
    }

    [SkippableFact]
    public void ToggleSidebar_KeepsStartSelected()
    {
        using var session = WindowsGuiAppSession.LaunchOrAttach();

        session.InvokeButton("TitleBar.ToggleSidebar");
        session.InvokeButton("TitleBar.ToggleSidebar");

        var startItem = session.FindByAutomationId("MainNav.Start");
        var selectionItem = startItem.Patterns.SelectionItem.Pattern;

        Assert.True(selectionItem.IsSelected.Value);
    }

    [SkippableFact]
    public void SelectFirstSession_WhenAvailable_UpdatesNavAndChatHeader()
    {
        using var session = WindowsGuiAppSession.LaunchOrAttach();

        var sessionItem = session.FindFirstByAutomationIdPrefix("MainNav.Session.");
        Skip.If(sessionItem is null, "No existing sessions are available for this local MSIX smoke run.");

        session.ActivateElement(sessionItem!);

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));
        var selectionItem = sessionItem!.Patterns.SelectionItem.Pattern;

        Assert.NotNull(chatHeader);
        Assert.True(selectionItem.IsSelected.Value);
    }
}
