using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class RemoteProjectSelectionViewModelTests
{
    [Fact]
    public void NoConfiguredDirectories_ReportsEmptyState()
    {
        var preferences = CreatePreferences();
        using var viewModel = CreateViewModel(preferences);

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasProjects);
        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void ConfiguredDirectories_AreProjectedAsSelectableItems()
    {
        var preferences = CreatePreferences();
        AddDirectory(preferences, "Repo B", "/remote/b");
        AddDirectory(preferences, "Repo A", "/remote/a");
        using var viewModel = CreateViewModel(preferences);

        Assert.True(viewModel.HasProjects);
        Assert.False(viewModel.IsEmpty);
        Assert.Equal(2, viewModel.Items.Count);
        // Ordered by display name.
        Assert.Equal("Repo A", viewModel.Items[0].DisplayName);
        Assert.Equal("Repo B", viewModel.Items[1].DisplayName);
    }

    [Fact]
    public void DoesNotAutoSelectFirstItem_SoConfirmStartsDisabled()
    {
        var preferences = CreatePreferences();
        AddDirectory(preferences, "Repo", "/remote/repo");
        using var viewModel = CreateViewModel(preferences);

        Assert.Null(viewModel.SelectedDirectoryId);
        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void SelectingAKnownDirectory_EnablesConfirm()
    {
        var preferences = CreatePreferences();
        var id = AddDirectory(preferences, "Repo", "/remote/repo");
        using var viewModel = CreateViewModel(preferences);

        viewModel.SelectedDirectoryId = id;

        Assert.True(viewModel.CanConfirm);
    }

    [Fact]
    public void SelectingAnUnknownDirectory_DoesNotEnableConfirm()
    {
        var preferences = CreatePreferences();
        AddDirectory(preferences, "Repo", "/remote/repo");
        using var viewModel = CreateViewModel(preferences);

        viewModel.SelectedDirectoryId = "not-a-real-id";

        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void RemovingSelectedDirectory_DropsStaleSelection()
    {
        var preferences = CreatePreferences();
        var id = AddDirectory(preferences, "Repo", "/remote/repo");
        using var viewModel = CreateViewModel(preferences);
        viewModel.SelectedDirectoryId = id;
        Assert.True(viewModel.CanConfirm);

        preferences.AgentRemoteDirectories.Clear();

        Assert.Null(viewModel.SelectedDirectoryId);
        Assert.False(viewModel.CanConfirm);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public void RefreshingDirectories_PreservesStillValidSelection()
    {
        var preferences = CreatePreferences();
        var keptId = AddDirectory(preferences, "Kept", "/remote/kept");
        using var viewModel = CreateViewModel(preferences);
        viewModel.SelectedDirectoryId = keptId;

        // Adding another directory rebuilds the projection; the valid selection survives.
        AddDirectory(preferences, "Added", "/remote/added");

        Assert.Equal(keptId, viewModel.SelectedDirectoryId);
        Assert.True(viewModel.CanConfirm);
    }

    private static RemoteProjectSelectionViewModel CreateViewModel(AppPreferencesViewModel preferences)
        => new(new NavigationProjectPreferencesAdapter(preferences), new ImmediateUiDispatcher());

    private static string AddDirectory(AppPreferencesViewModel preferences, string displayName, string remotePath)
    {
        var id = Guid.NewGuid().ToString("N");
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = id,
            DisplayName = displayName,
            RemotePath = remotePath
        });
        return id;
    }

    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            prefsLogger.Object,
            new ImmediateUiDispatcher());
    }
}
