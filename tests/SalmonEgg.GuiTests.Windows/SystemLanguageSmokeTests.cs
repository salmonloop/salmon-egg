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
        var originalChinese = app.GetVisibleTexts().Contains("选择界面语言", StringComparer.Ordinal);
        var originalLabel = originalChinese ? "选择界面语言" : "UI language";
        Assert.Contains(originalLabel, app.GetVisibleTexts());

        // Act: both selections use the native ComboBox and the normal language service/reload chain.
        SelectLanguage(app, originalChinese ? "English" : "Simplified Chinese");
        var explicitLabel = originalChinese ? "UI language" : "选择界面语言";
        Assert.True(app.WaitUntil(() => app.GetVisibleTexts().Contains(explicitLabel, StringComparer.Ordinal),
            TimeSpan.FromSeconds(15)), "The explicit language did not reach the reloaded page.");
        GuiAcceptanceDiagnostics.Record("Language: explicit page reloaded");
        var explicitTag = originalChinese ? "en-US" : "zh-Hans";
        Assert.True(app.WaitUntil(() => appData.ReadAppYaml().Contains("language: " + explicitTag, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10)), "The explicit choice was not persisted before the next preference change.");
        SelectLanguage(app, originalChinese ? "System" : "跟随系统");

        // Assert: System restores the observed native language, without assuming the runner is English.
        var systemRestored = app.WaitUntil(() => app.GetVisibleTexts().Contains(originalLabel, StringComparer.Ordinal),
            TimeSpan.FromSeconds(15));
        if (!systemRestored) app.CaptureAcceptanceFailure("system-language-not-restored");
        Assert.True(systemRestored, "Switching back to System did not restore the system-language page. Visible: "
                + string.Join(" | ", app.GetVisibleTexts()) + Environment.NewLine + appData.ReadBootLogTail()
                + Environment.NewLine + appData.ReadLatestAppLogTail());
        Assert.True(app.WaitUntilEnabled("GeneralSettings.Language", TimeSpan.FromSeconds(10)));
        var selector = app.FindByAutomationId("GeneralSettings.Language", TimeSpan.FromSeconds(10));
        Assert.Equal(originalChinese ? "跟随系统" : "System", selector.AsComboBox().SelectedItem?.Name);
    }

    private static void SelectLanguage(WindowsGuiAppSession app, string name)
    {
        Assert.True(app.WaitUntilEnabled("GeneralSettings.Language", TimeSpan.FromSeconds(10)));
        var selector = app.FindByAutomationId("GeneralSettings.Language", TimeSpan.FromSeconds(10));
        selector.AsComboBox().Expand();
        var text = app.FindVisibleElementByNameAnywhere(name, TimeSpan.FromSeconds(10));
        Assert.NotNull(text);
        AutomationElement? item = text;
        for (var index = 0; index < 8 && item is not null; index++, item = item.Parent)
        {
            if (!item.Patterns.SelectionItem.IsSupported) continue;
            item.Patterns.SelectionItem.Pattern.Select();
            GuiAcceptanceDiagnostics.Record("Language: native selected " + name);
            return;
        }
        throw new InvalidOperationException("The real language option has no native selection provider.");
    }
}
