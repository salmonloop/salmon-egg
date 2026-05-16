using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Core.Resources;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public class AcpProfilesViewModelTests
{
    private static async Task<AppPreferencesViewModel> CreatePreferencesAsync(string lastSelectedServerId = "")
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings { LastSelectedServerId = string.IsNullOrEmpty(lastSelectedServerId) ? null : lastSelectedServerId });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await Task.Delay(100);
        return vm;
    }

    [Fact]
    public async Task RefreshAsync_PopulatesAndOrdersProfilesFromConfigurationService()
    {
        // Arrange
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(new List<ServerConfiguration>
            {
                new() { Id = "2", Name = "Zebra" },
                new() { Id = "1", Name = "Apple" }
            });

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        // Act
        await viewModel.RefreshAsync();

        // Assert
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Equal("1", viewModel.Profiles[0].Id); // Apple should be first
        Assert.Equal("2", viewModel.Profiles[1].Id); // Zebra should be second
        Assert.Null(viewModel.SelectedProfile);
    }

    [Fact]
    public async Task RefreshAsync_RestoresLastSelectedProfile()
    {
        // Arrange
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(new List<ServerConfiguration>
            {
                new() { Id = "1", Name = "Apple" },
                new() { Id = "2", Name = "Zebra" }
            });

        var preferences = await CreatePreferencesAsync(lastSelectedServerId: "2");
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        // Act
        await viewModel.RefreshAsync();

        // Assert
        Assert.NotNull(viewModel.SelectedProfile);
        Assert.Equal("2", viewModel.SelectedProfile.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfileFromListAndServiceAndClearsSelection()
    {
        // Arrange
        var profileToDelete = new ServerConfiguration { Id = "2", Name = "Zebra" };
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(new List<ServerConfiguration>
            {
                new() { Id = "1", Name = "Apple" },
                profileToDelete
            });

        var preferences = await CreatePreferencesAsync(lastSelectedServerId: "2");
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        await viewModel.RefreshAsync();
        Assert.Equal("2", viewModel.SelectedProfile?.Id); // Verify it's initially selected

        // Act
        await viewModel.DeleteAsync(profileToDelete);

        // Assert
        configurationService.Verify(s => s.DeleteConfigurationAsync("2"), Times.Once);
        Assert.Single(viewModel.Profiles);
        Assert.Equal("1", viewModel.Profiles[0].Id);
        Assert.Null(viewModel.SelectedProfile); // Selection should be cleared
    }

    [Fact]
    public async Task SaveAsync_SavesConfigurationAndSelectsIt()
    {
        // Arrange
        var newProfile = new ServerConfiguration { Id = "3", Name = "Banana" };
        var listConfigs = new List<ServerConfiguration>
        {
            new() { Id = "1", Name = "Apple" },
            new() { Id = "2", Name = "Zebra" }
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(() => listConfigs);
        configurationService.Setup(s => s.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()))
            .Returns(Task.CompletedTask)
            .Callback<ServerConfiguration>(c => listConfigs.Add(c)); // Simulate saving by adding to list

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        await viewModel.RefreshAsync();

        // Act
        await viewModel.SaveAsync(newProfile);

        // Assert
        configurationService.Verify(s => s.SaveConfigurationAsync(newProfile), Times.Once);
        Assert.Equal(3, viewModel.Profiles.Count);
        Assert.Equal("3", viewModel.SelectedProfile?.Id); // Should select the newly saved profile
    }

    [Fact]
    public async Task SaveNewAsync_GeneratesIdIfMissingAndSaves()
    {
        // Arrange
        var newProfile = new ServerConfiguration { Name = "New Profile" };
        var listConfigs = new List<ServerConfiguration>();

        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(() => listConfigs);
        configurationService.Setup(s => s.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()))
            .Returns(Task.CompletedTask)
            .Callback<ServerConfiguration>(c => listConfigs.Add(c));

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        // Act
        await viewModel.SaveNewAsync(newProfile);

        // Assert
        Assert.NotNull(newProfile.Id);
        Assert.NotEmpty(newProfile.Id);
        configurationService.Verify(s => s.SaveConfigurationAsync(newProfile), Times.Once);
        Assert.Single(viewModel.Profiles);
        Assert.Equal(newProfile.Id, viewModel.SelectedProfile?.Id);
    }

    [Fact]
    public async Task RefreshIfEmptyAsync_DoesNotRefreshIfProfilesExist()
    {
        // Arrange
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(new List<ServerConfiguration> { new() { Id = "1", Name = "A" } });

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        await viewModel.RefreshAsync();
        Assert.Single(viewModel.Profiles);
        configurationService.Invocations.Clear();

        // Act
        await viewModel.RefreshIfEmptyAsync();

        // Assert
        configurationService.Verify(s => s.ListConfigurationsAsync(), Times.Never);
    }

    [Fact]
    public async Task RefreshIfEmptyAsync_RefreshesIfProfilesAreEmpty()
    {
        // Arrange
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(new List<ServerConfiguration> { new() { Id = "1", Name = "A" } });

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        Assert.Empty(viewModel.Profiles);

        // Act
        await viewModel.RefreshIfEmptyAsync();

        // Assert
        configurationService.Verify(s => s.ListConfigurationsAsync(), Times.Once);
        Assert.Single(viewModel.Profiles);
    }

    [Fact]
    public async Task MarkLastConnected_UpdatesPreferences()
    {
        // Arrange
        var profile = new ServerConfiguration { Id = "profile-123" };
        var preferences = await CreatePreferencesAsync();
        var configurationService = new Mock<IConfigurationService>();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            dispatcher);

        // Act
        viewModel.MarkLastConnected(profile);

        // Assert
        Assert.Equal("profile-123", preferences.LastSelectedServerId);
    }

    [Fact]
    public async Task FullConstructor_CreatesAndRemovesProfileItemsDuringRefreshAndDeletes()
    {
        // Arrange
        var profile1 = new ServerConfiguration { Id = "1", Name = "Apple" };
        var profileToDelete = new ServerConfiguration { Id = "2", Name = "Zebra" };
        var listConfigs = new List<ServerConfiguration> { profile1, profileToDelete };

        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(() => listConfigs);

        var preferences = await CreatePreferencesAsync();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var dispatcher = new ImmediateUiDispatcher();

        var sessionRegistry = new Mock<IAcpConnectionSessionRegistry>();
        var sessionEvents = new Mock<IAcpConnectionSessionEvents>();
        var connectionCommands = new Mock<ISettingsAcpConnectionCommands>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        var localizer = new Mock<IStringLocalizer<CoreStrings>>();

        using var viewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            sessionRegistry.Object,
            sessionEvents.Object,
            connectionCommands.Object,
            loggerFactory.Object,
            dispatcher,
            localizer.Object);

        // Act - Refresh
        await viewModel.RefreshAsync();

        // Assert - ProfileItems created
        Assert.Equal(2, viewModel.ProfileItems.Count);
        Assert.Equal("1", viewModel.ProfileItems[0].ProfileId);
        Assert.Equal("2", viewModel.ProfileItems[1].ProfileId);

        // Act - Delete
        listConfigs.Remove(profileToDelete); // To simulate successful delete from source
        await viewModel.DeleteAsync(profileToDelete);

        // Assert - ProfileItems updated
        Assert.Single(viewModel.ProfileItems);
        Assert.Equal("1", viewModel.ProfileItems[0].ProfileId);
    }
}
