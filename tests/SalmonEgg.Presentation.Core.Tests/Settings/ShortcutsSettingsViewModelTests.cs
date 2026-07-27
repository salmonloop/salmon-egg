using SalmonEgg.Presentation.Core.Resources;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class ShortcutsSettingsViewModelTests
{
    [Fact]
    public async Task Activate_SeedsOnlySupportedEditableActions()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings());

        var localizer = new TestCoreStringLocalizer();
        var viewModel = new ShortcutsSettingsViewModel(preferences, localizer);
        viewModel.Activate();

        Assert.Collection(
            viewModel.Shortcuts,
            first =>
            {
                Assert.Equal("new_session", first.ActionId);
                Assert.Equal(localizer["ShortcutAction_NewSession"], first.Name);
                Assert.Equal("Ctrl+N", first.DefaultGesture);
            },
            second =>
            {
                Assert.Equal("search", second.ActionId);
                Assert.Equal(localizer["ShortcutAction_Search"], second.Name);
                Assert.Equal("Ctrl+K", second.DefaultGesture);
            });
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "toggle_right_pane");
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "focus_input");
    }

    [Fact]
    public async Task Activate_AppliesSavedOverridesForSupportedActionsOnly()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                ["search"] = "Alt+K",
                ["toggle_right_pane"] = "Ctrl+\\"
            }
        });

        var viewModel = new ShortcutsSettingsViewModel(preferences, new TestCoreStringLocalizer());
        viewModel.Activate();

        var searchShortcut = Assert.Single(viewModel.Shortcuts, shortcut => shortcut.ActionId == "search");
        Assert.Equal("Alt+K", searchShortcut.Gesture);
        Assert.DoesNotContain(viewModel.Shortcuts, shortcut => shortcut.ActionId == "toggle_right_pane");
        Assert.Null(preferences.GetKeyBinding("toggle_right_pane"));
    }

    [Fact]
    public async Task ShortcutEntry_ExposesStableRecorderAutomationId()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings());

        var viewModel = new ShortcutsSettingsViewModel(preferences, new TestCoreStringLocalizer());
        viewModel.Activate();

        var searchShortcut = Assert.Single(viewModel.Shortcuts, shortcut => shortcut.ActionId == "search");
        Assert.Equal("Shortcuts.Record.search", searchShortcut.RecorderAutomationId);
    }

    [Fact]
    public async Task RestoreDefaults_ClearsSavedOverridesAtPreferenceOwner()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                ["search"] = "Alt+K"
            }
        });

        var viewModel = new ShortcutsSettingsViewModel(preferences, new TestCoreStringLocalizer());
        viewModel.Activate();

        viewModel.RestoreDefaultsCommand.Execute(null);

        var searchShortcut = Assert.Single(viewModel.Shortcuts, shortcut => shortcut.ActionId == "search");
        Assert.Equal("Ctrl+K", searchShortcut.Gesture);
        Assert.Null(preferences.GetKeyBinding("search"));
        Assert.Empty(preferences.KeyBindings);
    }

    [Fact]
    public async Task KeyboardShortcutsEnabled_ProjectsPreferenceWithoutClearingBindings()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                ["search"] = "Alt+K"
            }
        });

        var viewModel = new ShortcutsSettingsViewModel(preferences, new TestCoreStringLocalizer());
        viewModel.Activate();

        viewModel.Preferences.KeyboardShortcutsEnabled = false;

        Assert.False(preferences.KeyboardShortcutsEnabled);
        Assert.Equal("Alt+K", preferences.GetKeyBinding("search"));
    }


    [Fact]
    public async Task LanguageChanged_ReprojectsActionDisplayNames()
    {
        var preferences = await CreatePreferencesAsync(new AppSettings());
        var languageService = new RecordingAppLanguageService();
        var localizer = new MutableCoreStringLocalizer(new Dictionary<string, string>
        {
            ["ShortcutAction_NewSession"] = "New session",
            ["ShortcutAction_Search"] = "Search",
            ["Shortcuts_InvalidGestureMessage"] = "Invalid gesture",
            ["Shortcuts_ConflictMessage"] = "Conflict: {0}",
            ["Shortcuts_ConflictSeparator"] = ", "
        });

        var viewModel = new ShortcutsSettingsViewModel(preferences, localizer, languageService);
        viewModel.Activate();

        Assert.Equal("New session", viewModel.Shortcuts.Single(s => s.ActionId == "new_session").Name);
        Assert.Equal("Search", viewModel.Shortcuts.Single(s => s.ActionId == "search").Name);

        localizer.Set("ShortcutAction_NewSession", "新建会话");
        localizer.Set("ShortcutAction_Search", "搜索");
        languageService.RaiseLanguageChanged();

        Assert.Equal("新建会话", viewModel.Shortcuts.Single(s => s.ActionId == "new_session").Name);
        Assert.Equal("搜索", viewModel.Shortcuts.Single(s => s.ActionId == "search").Name);
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await preferences.InitializeAsync();
        return preferences;
    }

    private sealed class RecordingAppLanguageService : IAppLanguageService
    {
        public bool IsSupported => true;
        public string CurrentLanguageTag => "en";
        public event EventHandler? LanguageChanged;

        public Task ApplyLanguageOverrideAsync(string languageTag) => Task.CompletedTask;

        public void RaiseLanguageChanged() => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MutableCoreStringLocalizer : IStringLocalizer<CoreStrings>
    {
        private readonly Dictionary<string, string> _values;

        public MutableCoreStringLocalizer(Dictionary<string, string> values)
        {
            _values = values;
        }

        public void Set(string key, string value) => _values[key] = value;

        public LocalizedString this[string name]
            => _values.TryGetValue(name, out var value)
                ? new LocalizedString(name, value, resourceNotFound: false)
                : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var localized = this[name];
                if (localized.ResourceNotFound)
                {
                    return localized;
                }

                return new LocalizedString(name, string.Format(localized.Value, arguments), resourceNotFound: false);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => _values.Select(pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false));
    }
}
