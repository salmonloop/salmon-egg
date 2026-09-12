using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace SalmonEgg.GuiTests.Windows;

public sealed class SystemLanguageSmokeTests
{
    [Fact]
    public void Language_ExplicitThenSystem_ReloadsTheCurrentInstalledShell()
    {
        // Arrange: the original no-language seed selects System, including first launch.
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var app = WindowsGuiAppSession.LaunchFresh();
        if (app.MainWindow.Patterns.Window.IsSupported)
            app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        app.ClickElement(app.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(15)));
        app.ClickElement(app.FindByAutomationId("SettingsNav.General", TimeSpan.FromSeconds(15)));
        Assert.True(app.WaitUntilOnscreen("GeneralSettings.Language", TimeSpan.FromSeconds(15)));

        // Act: both selections use the native ComboBox and the normal language service/reload chain.
        SelectLanguage(app, "Simplified Chinese");
        Assert.True(app.WaitUntil(() => app.GetVisibleTexts().Any(text => text.Contains("简体中文", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(15)), "The explicit language did not reach the reloaded page.");
        SelectLanguage(app, "跟随系统");

        // Assert: hosted Windows runs English; the System choice and page are loaded afresh.
        Assert.True(app.WaitUntil(() => app.GetVisibleTexts().Any(text => text == "UI language"),
            TimeSpan.FromSeconds(15)), "Switching back to System did not restore the system-language page.");
        Assert.True(app.WaitUntilEnabled("GeneralSettings.Language", TimeSpan.FromSeconds(10)));
    }

    private static void SelectLanguage(WindowsGuiAppSession app, string name)
    {
        var selector = app.FindByAutomationId("GeneralSettings.Language", TimeSpan.FromSeconds(10));
        Assert.True(selector.IsEnabled);
        app.ClickElement(selector);
        var text = app.FindVisibleTextAnywhere(name, TimeSpan.FromSeconds(10));
        Assert.NotNull(text);
        AutomationElement? item = text;
        for (var index = 0; index < 8 && item is not null; index++, item = item.Parent)
        {
            if (!item.Patterns.SelectionItem.IsSupported) continue;
            item.Patterns.SelectionItem.Pattern.Select();
            return;
        }
        throw new InvalidOperationException("The real language option has no native selection provider.");
    }
}
