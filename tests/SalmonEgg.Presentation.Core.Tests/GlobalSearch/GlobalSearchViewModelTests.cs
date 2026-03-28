using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Models.Search;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.GlobalSearch;

[Collection("NonParallel")]
public sealed class GlobalSearchViewModelTests
{
    [Fact]
    public async Task SelectResultAsync_SessionUsesResolverDerivedProjectId()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            preferences.ProjectPathMappings.Add(new ProjectPathMapping
            {
                ProfileId = "profile-1",
                RemoteRootPath = "/remote/worktrees",
                LocalRootPath = @"C:\repo"
            });

            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-1",
                    "Remote Session",
                    "/remote/worktrees/demo/feature",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    RemoteSessionId: "remote-1",
                    BoundProfileId: "profile-1",
                    ProjectAffinityOverrideProjectId: null)
            ]);

            var navigationCoordinator = new Mock<INavigationCoordinator>();
            using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
            using var viewModel = new GlobalSearchViewModel(
                navigationViewModel,
                preferences,
                navigationCoordinator.Object,
                presenter,
                new ProjectAffinityResolver(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

            await viewModel.SelectResultCommand.ExecuteAsync(new SearchResultItem
            {
                Id = "session-1",
                Title = "Remote Session",
                Kind = SearchResultKind.Session
            });

            navigationCoordinator.Verify(
                coordinator => coordinator.ActivateSessionAsync("session-1", "project-1"),
                Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SourceFile_DoesNotContainInlineProjectPrefixMatcher()
    {
        var source = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\GlobalSearchViewModel.cs");

        Assert.DoesNotContain("NavTimeFormatter.NormalizePathForPrefixMatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith(projectRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProjectId(", source, StringComparison.Ordinal);
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        AppPreferencesViewModel preferences,
        ConversationCatalogPresenter presenter)
    {
        return new MainNavigationViewModel(
            Mock.Of<IConversationCatalog>(),
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IUiInteractionService>(),
            Mock.Of<IShellNavigationService>(),
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<MainNavigationViewModel>>(),
            new FakeNavigationPaneState(),
            Mock.Of<IShellLayoutMetricsSink>(),
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            presenter,
            new ProjectAffinityResolver());
    }

    private static AppPreferencesViewModel CreatePreferencesWithProject()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(service => service.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(service => service.IsSupported).Returns(false);

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<ILogger<AppPreferencesViewModel>>());

        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Demo",
            RootPath = @"C:\repo\demo"
        });

        return preferences;
    }

    private static string LoadFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen => true;

        public event EventHandler? PaneStateChanged
        {
            add { }
            remove { }
        }
    }
}
