using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public class ShortcutsSettingsViewModelTests
{
    private AppPreferencesViewModel CreatePreferencesViewModel()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsLaunchOnStartup).Returns(false);
        capabilities.SetupGet(c => c.SupportsTray).Returns(false);
        capabilities.SetupGet(c => c.SupportsLanguageOverride).Returns(false);

        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object,
            new ImmediateUiDispatcher());
    }

    [Fact]
    public void Constructor_SeedsDefaults()
    {
        var prefs = CreatePreferencesViewModel();
        var vm = new ShortcutsSettingsViewModel(prefs);

        Assert.NotEmpty(vm.Shortcuts);
        Assert.Contains(vm.Shortcuts, s => s.ActionId == "new_session" && s.Gesture == "Ctrl+N");
    }

    [Fact]
    public void Constructor_AppliesSavedOverrides()
    {
        var prefs = CreatePreferencesViewModel();
        prefs.SetKeyBinding("new_session", "Ctrl+Shift+N");

        var vm = new ShortcutsSettingsViewModel(prefs);

        var shortcut = vm.Shortcuts.First(s => s.ActionId == "new_session");
        Assert.Equal("Ctrl+Shift+N", shortcut.Gesture);
    }

    [Theory]
    [InlineData("Ctrl+N", true)]
    [InlineData("Ctrl+Shift+X", true)]
    [InlineData("Alt+F4", true)]
    [InlineData("Win+D", true)]
    [InlineData("N", false)]
    [InlineData("Ctrl+", false)]
    [InlineData("Invalid+X", false)]
    [InlineData("Ctrl+Alt+Shift+Win+X", true)]
    [InlineData("Ctrl+A+B", false)]
    public void IsGestureValid_ValidationRules(string gesture, bool expectedValid)
    {
        var vm = new ShortcutEntryViewModel("test", "Test", "Ctrl+T");
        vm.Gesture = gesture;

        Assert.Equal(expectedValid, vm.IsGestureValid);
    }

    [Fact]
    public void ConflictDetection_WhenInvalidGesture_HasInvalidTrueAndMessageSet()
    {
        var prefs = CreatePreferencesViewModel();
        var vm = new ShortcutsSettingsViewModel(prefs);

        var shortcut = vm.Shortcuts.First();
        shortcut.Gesture = "Invalid";

        Assert.True(vm.HasInvalid);
        Assert.Equal("存在无效快捷键格式，请修正后保存。", vm.ConflictMessage);
    }

    [Fact]
    public void ConflictDetection_WhenDuplicateGestures_HasConflictsTrueAndMessageSet()
    {
        var prefs = CreatePreferencesViewModel();
        var vm = new ShortcutsSettingsViewModel(prefs);

        var s1 = vm.Shortcuts[0];
        var s2 = vm.Shortcuts[1];

        s1.Gesture = "Ctrl+X";
        s2.Gesture = "Ctrl+X";

        Assert.True(vm.HasConflicts);
        Assert.False(vm.HasInvalid);
        Assert.Contains("存在冲突：Ctrl+X", vm.ConflictMessage);
    }

    [Fact]
    public void GestureChange_UpdatesPreferences()
    {
        var prefs = CreatePreferencesViewModel();
        var vm = new ShortcutsSettingsViewModel(prefs);

        var shortcut = vm.Shortcuts.First(s => s.ActionId == "new_session");
        shortcut.Gesture = "Ctrl+M";

        Assert.Equal("Ctrl+M", prefs.GetKeyBinding("new_session"));

        // Set to empty should remove binding
        shortcut.Gesture = "";
        Assert.Null(prefs.GetKeyBinding("new_session"));
    }

    [Fact]
    public void RestoreDefaults_RevertsGestures()
    {
        var prefs = CreatePreferencesViewModel();
        var vm = new ShortcutsSettingsViewModel(prefs);

        foreach (var shortcut in vm.Shortcuts)
        {
            shortcut.Gesture = "Ctrl+X";
        }

        vm.RestoreDefaultsCommand.Execute(null);

        foreach (var shortcut in vm.Shortcuts)
        {
            Assert.Equal(shortcut.DefaultGesture, shortcut.Gesture);
        }
    }
}
