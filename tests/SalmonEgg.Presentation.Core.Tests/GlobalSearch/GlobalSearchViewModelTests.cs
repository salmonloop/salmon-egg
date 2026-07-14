using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Search;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Search;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Core.Resources;
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
        var preferences = CreatePreferencesWithProject();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-1",
            DisplayName = "Worktrees",
            RemotePath = "/remote/worktrees"
        });

        var presenter = new ConversationCatalogPresenter();
        presenter.SetLoading(false);
        presenter.Refresh(
        [
            new ConversationCatalogItem(
                "session-1",
                "Remote Session",
                "/remote/worktrees",
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
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        await viewModel.SelectResultCommand.ExecuteAsync(new SearchResultItem
        {
            Id = "session-1",
            Title = "Remote Session",
            Kind = SearchResultKind.Session
        });

        navigationCoordinator.Verify(
            coordinator => coordinator.ActivateSessionAsync("session-1", NavigationProjectIds.Unclassified),
            Times.Once);
    }

    [Fact]
    public void QueryChange_EntersLoading_AndOpensPanelImmediately()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var pipeline = new ControlledSearchPipeline();
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            Mock.Of<INavigationCoordinator>(),
            presenter,
            new ProjectAffinityResolver(),
            pipeline,
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "abc";

        Assert.Equal(GlobalSearchViewState.Loading, viewModel.ViewState);
        Assert.True(viewModel.IsSearching);
        Assert.Contains(viewModel.SuggestionEntries, entry => entry.Kind == SearchSuggestionEntryKind.Status);
    }

    [Fact]
    public async Task LanguageChanged_RerunsActiveSearchProjection()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var pipeline = new ControlledSearchPipeline();
        var languageService = new Mock<IAppLanguageService>();
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            Mock.Of<INavigationCoordinator>(),
            presenter,
            new ProjectAffinityResolver(),
            pipeline,
            Mock.Of<IStringLocalizer<CoreStrings>>(),
            Mock.Of<ILogger<GlobalSearchViewModel>>(),
            languageService.Object);

        viewModel.Query = "abc";
        await WaitForConditionAsync(() => pipeline.CallCount == 1);

        languageService.Raise(service => service.LanguageChanged += null, EventArgs.Empty);

        await WaitForConditionAsync(() => pipeline.CallCount == 2);
        Assert.Equal("abc", viewModel.Query);
    }

    [Fact]
    public async Task SubmitQueryAsync_WhenNoChosenSuggestion_ActivatesFirstActionableSuggestion()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        navigationCoordinator.Setup(coordinator => coordinator.ActivateSettingsAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            navigationCoordinator.Object,
            presenter,
            new ProjectAffinityResolver(),
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
        Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "acp";
        await WaitForConditionAsync(() => viewModel.SuggestionEntries.Any(entry => entry.Kind == SearchSuggestionEntryKind.Result));

        await viewModel.SubmitQueryAsync("acp");

        navigationCoordinator.Verify(
            coordinator => coordinator.ActivateSettingsAsync(SettingsSectionCatalog.AgentAcpKey),
            Times.Once);
    }

    [Fact]
    public async Task SubmitQueryAsync_WhenNoActionableSuggestion_IsNoOp()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        navigationCoordinator.Setup(coordinator => coordinator.ActivateSettingsAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            navigationCoordinator.Object,
            presenter,
            new ProjectAffinityResolver(),
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = string.Empty;

        await viewModel.SubmitQueryAsync(string.Empty);

        navigationCoordinator.Verify(
            coordinator => coordinator.ActivateSettingsAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ActivateSuggestionAsync_WhenResultSuggestionChosen_UsesNativeSuggestionActivationPath()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        navigationCoordinator.Setup(coordinator => coordinator.ActivateSettingsAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            navigationCoordinator.Object,
            presenter,
            new ProjectAffinityResolver(),
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
        Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "acp";
        await WaitForConditionAsync(() => viewModel.SuggestionEntries.Any(entry => entry.Kind == SearchSuggestionEntryKind.Result));

        var suggestion = Assert.Single(viewModel.SuggestionEntries, entry => entry.Kind == SearchSuggestionEntryKind.Result);

        await viewModel.ActivateSuggestionAsync(suggestion);

        navigationCoordinator.Verify(
            coordinator => coordinator.ActivateSettingsAsync(SettingsSectionCatalog.AgentAcpKey),
            Times.Once);
    }

    [Fact]
    public async Task ActivateSuggestionAsync_WhenHistorySuggestionChosen_RestoresHistoryQuery()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        navigationCoordinator.Setup(coordinator => coordinator.ActivateSettingsAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            navigationCoordinator.Object,
            presenter,
            new ProjectAffinityResolver(),
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
        Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "acp";
        await WaitForConditionAsync(() => viewModel.SuggestionEntries.Any(entry => entry.Kind == SearchSuggestionEntryKind.Result));
        await viewModel.SelectResultCommand.ExecuteAsync(new SearchResultItem
        {
            Id = SettingsSectionCatalog.AgentAcpKey,
            Title = "ACP 配置",
            Kind = SearchResultKind.Setting
        });

        viewModel.Query = string.Empty;

        var suggestion = Assert.Single(viewModel.SuggestionEntries, entry => entry.Kind == SearchSuggestionEntryKind.History);

        await viewModel.ActivateSuggestionAsync(suggestion);

        Assert.Equal("acp", viewModel.Query);
    }

    [Fact]
    public async Task StaleFailure_DoesNotOverrideLatestSuccessfulResult()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var pipeline = new ScriptedSearchPipeline(async query =>
        {
            if (string.Equals(query, "alpha", StringComparison.Ordinal))
            {
                await Task.Delay(300);
                throw new InvalidOperationException("stale error");
            }

            await Task.Delay(40);
            return new GlobalSearchSnapshot(
                ImmutableArray.Create(
                    new GlobalSearchGroupSnapshot(
                        Name: "sessions",
                        Title: "会话",
                        Priority: 100,
                        Items: ImmutableArray.Create(
                            new GlobalSearchItemSnapshot(
                                Id: "latest",
                                Title: "latest",
                                Subtitle: null,
                                Kind: SearchResultKind.Session,
                                IconGlyph: "\uE8BD",
                                Tag: null)))));
        });

        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            Mock.Of<INavigationCoordinator>(),
            presenter,
            new ProjectAffinityResolver(),
            pipeline,
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "alpha";
        await Task.Delay(40, TestContext.Current.CancellationToken);
        viewModel.Query = "beta";
        await WaitForConditionAsync(() => viewModel.ViewState == GlobalSearchViewState.Results);

        Assert.Equal(GlobalSearchViewState.Results, viewModel.ViewState);
        Assert.False(viewModel.IsError);
        Assert.True(viewModel.HasResults);
    }

    [Fact]
    public async Task SelectResultAsync_SettingUsesCanonicalSettingsKey()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            navigationCoordinator.Object,
            presenter,
            new ProjectAffinityResolver(),
            new DefaultGlobalSearchPipeline(Mock.Of<IStringLocalizer<CoreStrings>>()),
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        await viewModel.SelectResultCommand.ExecuteAsync(new SearchResultItem
        {
            Id = SettingsSectionCatalog.AgentAcpKey,
            Title = "ACP 配置",
            Kind = SearchResultKind.Setting
        });

        navigationCoordinator.Verify(
            coordinator => coordinator.ActivateSettingsAsync(SettingsSectionCatalog.AgentAcpKey),
            Times.Once);
    }

    [Fact]
    public async Task QueryChange_AfterError_CanRecoverToResults()
    {
        var preferences = CreatePreferencesWithProject();
        var presenter = new ConversationCatalogPresenter();
        var pipeline = new ScriptedSearchPipeline(query =>
        {
            if (string.Equals(query, "boom", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("search failed");
            }

            return Task.FromResult(new GlobalSearchSnapshot(
                ImmutableArray.Create(
                    new GlobalSearchGroupSnapshot(
                        Name: "sessions",
                        Title: "会话",
                        Priority: 100,
                        Items: ImmutableArray.Create(
                            new GlobalSearchItemSnapshot(
                                Id: "ok-1",
                                Title: "ok",
                                Subtitle: null,
                                Kind: SearchResultKind.Session,
                                IconGlyph: "\uE8BD",
                                Tag: null))))));
        });

        using var navigationViewModel = CreateNavigationViewModel(preferences, presenter);
        using var viewModel = new GlobalSearchViewModel(
            navigationViewModel,
            preferences,
            Mock.Of<INavigationCoordinator>(),
            presenter,
            new ProjectAffinityResolver(),
            pipeline,
            Mock.Of<IStringLocalizer<CoreStrings>>(),
                Mock.Of<ILogger<GlobalSearchViewModel>>());

        viewModel.Query = "boom";
        await WaitForConditionAsync(() => viewModel.ViewState == GlobalSearchViewState.Error);
        Assert.Equal(GlobalSearchViewState.Error, viewModel.ViewState);
        Assert.True(viewModel.IsError);

        viewModel.Query = "ok";
        await WaitForConditionAsync(() => viewModel.ViewState == GlobalSearchViewState.Results);
        Assert.Equal(GlobalSearchViewState.Results, viewModel.ViewState);
        Assert.False(viewModel.IsError);
        Assert.True(viewModel.HasResults);
    }

    private static async Task WaitForConditionAsync(
        Func<bool> predicate,
        int timeoutMilliseconds = 5000,
        int delayMilliseconds = 10)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMilliseconds);
        }

        Assert.True(predicate(), "Timed out waiting for expected asynchronous search state.");
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        AppPreferencesViewModel preferences,
        ConversationCatalogPresenter presenter)
    {
        return new MainNavigationViewModel(
            Mock.Of<IConversationCatalog>(),
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IUiInteractionService>(),
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<MainNavigationViewModel>>(),
            new FakeNavigationPaneState(),
            Mock.Of<IShellLayoutMetricsSink>(),
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            new ShellNavigationRuntimeStateStore(),
            presenter,
            new ProjectAffinityResolver(),
            new ImmediateUiDispatcher(),
            Mock.Of<IStringLocalizer<CoreStrings>>());
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
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Demo",
            RootPath = @"C:\repo\demo"
        });

        return preferences;
    }

    private sealed class ControlledSearchPipeline : IGlobalSearchPipeline
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<GlobalSearchSnapshot> SearchAsync(
            string query,
            GlobalSearchSourceSnapshot source,
            CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _callCount);
            return Task.FromResult(new GlobalSearchSnapshot(ImmutableArray<GlobalSearchGroupSnapshot>.Empty));
        }
    }

    private sealed class ScriptedSearchPipeline : IGlobalSearchPipeline
    {
        private readonly Func<string, Task<GlobalSearchSnapshot>> _search;

        public ScriptedSearchPipeline(Func<string, Task<GlobalSearchSnapshot>> search)
        {
            _search = search ?? throw new ArgumentNullException(nameof(search));
        }

        public Task<GlobalSearchSnapshot> SearchAsync(
            string query,
            GlobalSearchSourceSnapshot source,
            CancellationToken cancellationToken)
            => _search(query);
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
