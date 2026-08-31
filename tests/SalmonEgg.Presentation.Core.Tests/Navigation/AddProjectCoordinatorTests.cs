using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class AddProjectCoordinatorTests
{
    [Fact]
    public void AddProject_LocalFolder_AddsProjectAndReturnsAddedWithId()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);
        var path = Path.Combine(Path.GetTempPath(), $"salmonegg-add-{Guid.NewGuid():N}");

        var outcome = coordinator.AddProject(new ProjectSourceSelection.LocalFolder(path));

        Assert.Equal(AddProjectStatus.Added, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.ProjectId));
        Assert.Contains(preferences.Projects, project =>
            string.Equals(project.ProjectId, outcome.ProjectId, StringComparison.Ordinal)
            && string.Equals(project.Name, Path.GetFileName(path), StringComparison.Ordinal));
    }

    [Fact]
    public void AddProject_LocalFolder_WhenAlreadyPresent_ReturnsAlreadyExistsWithExistingId()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);
        var path = Path.Combine(Path.GetTempPath(), $"salmonegg-dup-{Guid.NewGuid():N}");

        var first = coordinator.AddProject(new ProjectSourceSelection.LocalFolder(path));
        var second = coordinator.AddProject(new ProjectSourceSelection.LocalFolder(path));

        Assert.Equal(AddProjectStatus.Added, first.Status);
        Assert.Equal(AddProjectStatus.AlreadyExists, second.Status);
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Single(preferences.Projects, project =>
            string.Equals(project.ProjectId, first.ProjectId, StringComparison.Ordinal));
    }

    [Fact]
    public void AddProject_LocalFolder_DedupIsCaseInsensitiveOnPath()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);
        var root = Path.Combine(Path.GetTempPath(), $"salmonegg-case-{Guid.NewGuid():N}");

        var first = coordinator.AddProject(new ProjectSourceSelection.LocalFolder(root.ToLowerInvariant()));
        var second = coordinator.AddProject(new ProjectSourceSelection.LocalFolder(root.ToUpperInvariant()));

        Assert.Equal(AddProjectStatus.Added, first.Status);
        Assert.Equal(AddProjectStatus.AlreadyExists, second.Status);
        Assert.Equal(first.ProjectId, second.ProjectId);
    }

    [Fact]
    public void AddProject_LocalFolder_EmptyPath_ReturnsInvalid()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);

        var outcome = coordinator.AddProject(new ProjectSourceSelection.LocalFolder("   "));

        Assert.Equal(AddProjectStatus.Invalid, outcome.Status);
        Assert.Null(outcome.ProjectId);
        Assert.Empty(preferences.Projects);
    }

    [Fact]
    public void AddProject_RemoteDirectory_KnownId_AddsMembershipAndReturnsRemoteNodeId()
    {
        var preferences = CreatePreferences();
        var directoryId = Guid.NewGuid().ToString("N");
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = directoryId,
            DisplayName = "Repo",
            RemotePath = "/remote/repo"
        });
        var coordinator = CreateCoordinator(preferences);

        var outcome = coordinator.AddProject(new ProjectSourceSelection.RemoteDirectory(directoryId));

        Assert.Equal(AddProjectStatus.Added, outcome.Status);
        Assert.Equal(ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId(directoryId), outcome.ProjectId);
        Assert.Contains(preferences.NavigationRemoteDirectoryIds, id =>
            string.Equals(id, directoryId, StringComparison.Ordinal));
    }

    [Fact]
    public void AddProject_RemoteDirectory_UnknownId_IsRejectedAndNeverTreatedAsLocalPath()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);

        var outcome = coordinator.AddProject(new ProjectSourceSelection.RemoteDirectory("does-not-exist"));

        Assert.Equal(AddProjectStatus.RejectedUnknownRemote, outcome.Status);
        Assert.Null(outcome.ProjectId);
        Assert.Empty(preferences.NavigationRemoteDirectoryIds);
        Assert.Empty(preferences.Projects);
    }

    [Fact]
    public void AddProject_RemoteDirectory_AlreadyMember_ReturnsAlreadyExistsWithoutDuplicate()
    {
        var preferences = CreatePreferences();
        var directoryId = Guid.NewGuid().ToString("N");
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = directoryId,
            DisplayName = "Repo",
            RemotePath = "/remote/repo"
        });
        var coordinator = CreateCoordinator(preferences);

        var first = coordinator.AddProject(new ProjectSourceSelection.RemoteDirectory(directoryId));
        var second = coordinator.AddProject(new ProjectSourceSelection.RemoteDirectory(directoryId));

        Assert.Equal(AddProjectStatus.Added, first.Status);
        Assert.Equal(AddProjectStatus.AlreadyExists, second.Status);
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Single(preferences.NavigationRemoteDirectoryIds, id =>
            string.Equals(id, directoryId, StringComparison.Ordinal));
    }

    [Fact]
    public void AddProject_RemoteDirectory_EmptyId_ReturnsInvalid()
    {
        var preferences = CreatePreferences();
        var coordinator = CreateCoordinator(preferences);

        var outcome = coordinator.AddProject(new ProjectSourceSelection.RemoteDirectory("  "));

        Assert.Equal(AddProjectStatus.Invalid, outcome.Status);
        Assert.Empty(preferences.NavigationRemoteDirectoryIds);
    }

    private static AddProjectCoordinator CreateCoordinator(AppPreferencesViewModel preferences)
        => new(new NavigationProjectPreferencesAdapter(preferences), NullLogger<AddProjectCoordinator>.Instance);

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
