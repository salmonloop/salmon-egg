using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.TestDoubles;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class RemoteDirectoryRegistrarTests
{
    [Fact]
    public void EnsureRegistered_NewPath_AddsDirectoryAndNavigationMembership()
    {
        var preferences = CreatePreferences();

        var directoryId = CreateRegistrar(preferences).EnsureRegistered("/home/user/project-a");

        Assert.False(string.IsNullOrWhiteSpace(directoryId));
        var directory = Assert.Single(preferences.AgentRemoteDirectories);
        Assert.Equal(directoryId, directory.DirectoryId);
        Assert.Equal("/home/user/project-a", directory.RemotePath);
        Assert.Equal(string.Empty, directory.DisplayName);
        Assert.Contains(directoryId, preferences.NavigationRemoteDirectoryIds);
    }

    [Fact]
    public void EnsureRegistered_EquivalentPath_ReusesExistingDirectory()
    {
        var preferences = CreatePreferences();
        var existing = new AgentRemoteDirectory
        {
            DirectoryId = "existing-id",
            DisplayName = "Project A",
            RemotePath = "/home/user/project-a/"
        };
        preferences.AgentRemoteDirectories.Add(existing);
        var directoryCountBefore = preferences.AgentRemoteDirectories.Count;

        var directoryId = CreateRegistrar(preferences).EnsureRegistered("/home/user/project-a");

        Assert.Equal("existing-id", directoryId);
        Assert.Equal(directoryCountBefore, preferences.AgentRemoteDirectories.Count);
        Assert.Equal("Project A", preferences.AgentRemoteDirectories.Single().DisplayName);
        Assert.Contains("existing-id", preferences.NavigationRemoteDirectoryIds);
    }

    [Fact]
    public void EnsureRegistered_ExistingDirectoryNotInNavigation_AddsMembershipOnly()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "existing-id",
            DisplayName = "Project A",
            RemotePath = "/home/user/project-a"
        });

        var directoryId = CreateRegistrar(preferences).EnsureRegistered("/home/user/project-a");

        Assert.Equal("existing-id", directoryId);
        Assert.Contains("existing-id", preferences.NavigationRemoteDirectoryIds);
    }

    [Fact]
    public void EnsureRegistered_DuplicateCall_IsIdempotent()
    {
        var preferences = CreatePreferences();
        var registrar = CreateRegistrar(preferences);

        var firstId = registrar.EnsureRegistered("/home/user/project-a");
        var secondId = registrar.EnsureRegistered("/home/user/project-a");

        Assert.Equal(firstId, secondId);
        Assert.Single(preferences.AgentRemoteDirectories);
        Assert.Single(preferences.NavigationRemoteDirectoryIds);
    }

    [Fact]
    public void EnsureRegistered_WindowsPath_CaseInsensitiveMatchReusesExisting()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "windows-id",
            RemotePath = "C:/Users/user/project"
        });

        var directoryId = CreateRegistrar(preferences).EnsureRegistered(@"c:\Users\user\project");

        Assert.Equal("windows-id", directoryId);
        Assert.Single(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public void EnsureRegistered_RelativePath_IsRejected()
    {
        var preferences = CreatePreferences();

        var directoryId = CreateRegistrar(preferences).EnsureRegistered("relative/path");

        Assert.Null(directoryId);
        Assert.Empty(preferences.AgentRemoteDirectories);
        Assert.Empty(preferences.NavigationRemoteDirectoryIds);
    }

    [Fact]
    public void EnsureRegistered_NullOrWhitespace_IsRejected()
    {
        var preferences = CreatePreferences();
        var registrar = CreateRegistrar(preferences);

        Assert.Null(registrar.EnsureRegistered(null!));
        Assert.Null(registrar.EnsureRegistered("   "));
        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    private static RemoteDirectoryRegistrar CreateRegistrar(AppPreferencesViewModel preferences)
        => new(preferences);

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
            new ImmediateUiDispatcher(),
            TestSystemNotificationService.Instance);
    }
}
