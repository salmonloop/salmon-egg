using System.Collections.Generic;
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

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class ShortcutsSettingsViewModelTests
{
    [Fact]
    public async Task Constructor_SeedsOnlySupportedEditableActions()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings());

        var viewModel = new ShortcutsSettingsViewModel(preferences);

        Assert.Collection(
            viewModel.Shortcuts,
            first =>
            {
                Assert.Equal("new_session", first.ActionId);
                Assert.Equal("Ctrl+N", first.DefaultGesture);
            },
            second =>
            {
                Assert.Equal("search", second.ActionId);
                Assert.Equal("Ctrl+K", second.DefaultGesture);
            });
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "toggle_right_pane");
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "focus_input");
    }

    [Fact]
    public async Task Constructor_AppliesSavedOverridesForSupportedActionsOnly()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                ["search"] = "Alt+K",
                ["toggle_right_pane"] = "Ctrl+\\"
            }
        });

        var viewModel = new ShortcutsSettingsViewModel(preferences);

        var searchShortcut = Assert.Single(viewModel.Shortcuts.Where(shortcut => shortcut.ActionId == "search"));
        Assert.Equal("Alt+K", searchShortcut.Gesture);
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "toggle_right_pane");
        Assert.Null(preferences.GetKeyBinding("toggle_right_pane"));
    }

    private static async Task<AppPreferencesViewModel> CreatePreferencesAsync(AppSettings settings)
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(service => service.LoadAsync()).ReturnsAsync(settings);

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(service => service.IsSupported).Returns(false);

        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(service => service.SupportsLaunchOnStartup).Returns(false);
        capabilities.SetupGet(service => service.SupportsTray).Returns(false);
        capabilities.SetupGet(service => service.SupportsLanguageOverride).Returns(false);

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            Mock.Of<IAppLanguageService>(),
            capabilities.Object,
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await Task.Delay(100);
        return preferences;
    }
}
