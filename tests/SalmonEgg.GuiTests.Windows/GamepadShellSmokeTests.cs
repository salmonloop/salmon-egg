using System;
using System.Threading;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ShellFocusedActivationSmokeTests
{
    [SkippableFact]
    public void DiscoverSessions_CanBeReached_AndActivated_ThroughFocusedNativeActivationPath()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.FocusElement(discoverItem);
        Thread.Sleep(150);
        session.PressEnter();

        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible through focused native activation.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }
}
