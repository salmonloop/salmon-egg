using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace SalmonEgg.Presentation.Core.Tests.ViewModels.Settings;

public class AcpProfilesViewModelTests
{
    private static async Task<AppPreferencesViewModel> CreatePreferencesAsync()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(10);
        return preferences;
    }

    private static AcpProfilesViewModel CreateViewModel(
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        IAcpConnectionSessionRegistry? registry = null,
        IAcpConnectionSessionEvents? events = null,
        ISettingsAcpConnectionCommands? commands = null)
    {
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();

        if (registry != null && events != null && commands != null)
        {
            return new AcpProfilesViewModel(
                configurationService,
                preferences,
                logger.Object,
                registry,
                events,
                commands,
                NullLoggerFactory.Instance,
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());
        }

        return new AcpProfilesViewModel(
            configurationService,
            preferences,
            logger.Object,
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());
    }

    [Fact]
    public async Task RefreshAsync_LoadsProfilesFromConfigurationService()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
            new ServerConfiguration { Id = "1", Name = "Beta" },
            new ServerConfiguration { Id = "2", Name = "Alpha" }
        };

        var configServiceMock = new Mock<IConfigurationService>();
        configServiceMock.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(configurations);

        var viewModel = CreateViewModel(configServiceMock.Object, preferences);

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Equal("Alpha", viewModel.Profiles[0].Name); // Order by name
        Assert.Equal("Beta", viewModel.Profiles[1].Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfileLocallyAndRemotely()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profileToDelete = new ServerConfiguration { Id = "1", Name = "Alpha" };
        var configurations = new[] { profileToDelete, new ServerConfiguration { Id = "2", Name = "Beta" } };

        var configServiceMock = new Mock<IConfigurationService>();
        configServiceMock.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(configurations);

        var viewModel = CreateViewModel(configServiceMock.Object, preferences);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Profiles.Count);

        // Act
        await viewModel.DeleteCommand.ExecuteAsync(profileToDelete);

        // Assert
        configServiceMock.Verify(s => s.DeleteConfigurationAsync("1"), Times.Once);
        Assert.Single(viewModel.Profiles);
        Assert.Equal("Beta", viewModel.Profiles[0].Name);
    }

    [Fact]
    public async Task SaveNewAsync_GeneratesIdAndSaves()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var configServiceMock = new Mock<IConfigurationService>();
        configServiceMock.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(Array.Empty<ServerConfiguration>());

        var viewModel = CreateViewModel(configServiceMock.Object, preferences);

        var newProfile = new ServerConfiguration { Name = "Gamma" };

        // Act
        await viewModel.SaveNewAsync(newProfile);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(newProfile.Id));
        configServiceMock.Verify(s => s.SaveConfigurationAsync(newProfile), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WithDependencies_PopulatesProfileItems()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
            new ServerConfiguration { Id = "1", Name = "Alpha" }
        };

        var configServiceMock = new Mock<IConfigurationService>();
        configServiceMock.Setup(s => s.ListConfigurationsAsync()).ReturnsAsync(configurations);

        var registryMock = new Mock<IAcpConnectionSessionRegistry>();

        AcpConnectionSession dummySession = null!;
        registryMock.Setup(r => r.TryGetByProfile(It.IsAny<string>(), out dummySession)).Returns(false);
        var eventsMock = new Mock<IAcpConnectionSessionEvents>();
        var commandsMock = new Mock<ISettingsAcpConnectionCommands>();

        var viewModel = CreateViewModel(configServiceMock.Object, preferences, registryMock.Object, eventsMock.Object, commandsMock.Object);

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(viewModel.ProfileItems);
        Assert.Equal("1", viewModel.ProfileItems[0].ProfileId);
    }
}
