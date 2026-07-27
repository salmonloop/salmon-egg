using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Start;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;
using SalmonEgg.Acp.Client;
using SalmonEgg.Application.Services.Acp;

namespace SalmonEgg.Presentation.Core.Tests.Start;

[Collection("NonParallel")]
public sealed class StartViewModelTests
{
    private static readonly TimeSpan PreviousFixedDraftIdentityWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DraftIdentityWaitRegressionBuffer = TimeSpan.FromMilliseconds(150);
    private const string RemoteModeProjectPrompt = "Select a remote project first";
    private const string RemoteProjectPrompt = "Select a remote project";

    [Fact]
    public async Task StartSessionAndSendAsync_DoesNotInvokeWorkflow_WhenPromptIsBlank()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            var startLogger = new Mock<ILogger<StartViewModel>>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object, startLogger.Object);

            startViewModel.StartPrompt = "   ";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_DelegatesTrimmedPromptToWorkflow()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow
                .Setup(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChatLaunchCompletion.PromptDispatched);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);

            startViewModel.StartPrompt = "  hello  ";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync(
                It.Is<ChatLaunchRequest>(request =>
                    request.PromptText == "hello"
                    && request.ProjectId == NavigationProjectIds.Unclassified),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_LocalProfile_DoesNotLeakPersistedRemoteDirectoryCwd()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-remote",
                DisplayName = "Remote",
                RemotePath = "/remote/workspace"
            });
            preferences.LastSelectedProjectId = "remote-directory:dir-remote";
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            ChatLaunchRequest? capturedRequest = null;
            workflow
                .Setup(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ChatLaunchRequest, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(ChatLaunchCompletion.PromptDispatched);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);

            startViewModel.StartPrompt = "hello";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            Assert.NotNull(capturedRequest);
            Assert.Equal(NavigationProjectIds.Unclassified, capturedRequest!.ProjectId);
            Assert.Null(capturedRequest.Cwd);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_ResetsBusyState_WhenWorkflowThrows()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);

            startViewModel.StartPrompt = "hello";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            Assert.False(startViewModel.IsStarting);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ExecuteSuggestion_UpdatesSharedChatPromptDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            chat.ViewModel.CurrentPrompt = "chat draft";
            startViewModel.OnComposerLoaded();

            var suggestion = startViewModel.Suggestions[0];
            startViewModel.ExecuteSuggestionCommand.Execute(suggestion);

            Assert.Equal(suggestion.Prompt, chat.ViewModel.CurrentPrompt);
            Assert.Equal(suggestion.Prompt, startViewModel.StartPrompt);
            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task QuickSuggestions_ResolveUserVisibleTextFromCoreStrings()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);

            var localizer = new TestCoreStringLocalizer();
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                localizer: localizer);

            Assert.Collection(
                startViewModel.Suggestions,
                suggestion =>
                {
                    Assert.Equal("StartView.Suggestion.AnalyzeCodebase", suggestion.AutomationId);
                    Assert.Equal(localizer["StartSuggestion_AnalyzeCodebaseTitle"], suggestion.Title);
                    Assert.Equal(localizer["StartSuggestion_AnalyzeCodebaseSubtitle"], suggestion.Subtitle);
                    Assert.Equal(localizer["StartSuggestion_AnalyzeCodebasePrompt"], suggestion.Prompt);
                },
                suggestion =>
                {
                    Assert.Equal("StartView.Suggestion.RecommendTasks", suggestion.AutomationId);
                    Assert.Equal(localizer["StartSuggestion_RecommendTasksTitle"], suggestion.Title);
                    Assert.Equal(localizer["StartSuggestion_RecommendTasksSubtitle"], suggestion.Subtitle);
                    Assert.Equal(localizer["StartSuggestion_RecommendTasksPrompt"], suggestion.Prompt);
                },
                suggestion =>
                {
                    Assert.Equal("StartView.Suggestion.ResolveErrors", suggestion.AutomationId);
                    Assert.Equal(localizer["StartSuggestion_ResolveErrorsTitle"], suggestion.Title);
                    Assert.Equal(localizer["StartSuggestion_ResolveErrorsSubtitle"], suggestion.Subtitle);
                    Assert.Equal(localizer["StartSuggestion_ResolveErrorsPrompt"], suggestion.Prompt);
                });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LanguageChanged_RebuildsCachedQuickSuggestions()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var languageService = new Mock<IAppLanguageService>();
            var languagePrefix = "zh";
            var localizer = new Mock<IStringLocalizer<CoreStrings>>();
            localizer
                .Setup(service => service[It.IsAny<string>()])
                .Returns((string key) => new LocalizedString(key, $"{languagePrefix}:{key}"));

            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                Mock.Of<IChatLaunchWorkflow>(),
                localizer: localizer.Object,
                languageService: languageService.Object);

            Assert.Equal("zh:StartSuggestion_AnalyzeCodebaseTitle", startViewModel.Suggestions[0].Title);

            languagePrefix = "en";
            languageService.Raise(service => service.LanguageChanged += null, EventArgs.Empty);

            Assert.Equal(3, startViewModel.Suggestions.Count);
            Assert.Equal("en:StartSuggestion_AnalyzeCodebaseTitle", startViewModel.Suggestions[0].Title);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LanguageChanged_ReprojectsOpenStartLaunchFailureMessage()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var languageService = new Mock<IAppLanguageService>();
            var languagePrefix = "zh";
            var localizer = new Mock<IStringLocalizer<CoreStrings>>();
            localizer
                .Setup(service => service[It.IsAny<string>()])
                .Returns((string key) => new LocalizedString(key, $"{languagePrefix}:{key}"));

            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow
                .Setup(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChatLaunchCompletion.Failed);

            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                localizer: localizer.Object,
                languageService: languageService.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);

            startViewModel.StartPrompt = "hello";
            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("zh:Start_SessionLaunchFailed", startViewModel.StartSessionDraftErrorMessage);

            languagePrefix = "en";
            languageService.Raise(service => service.LanguageChanged += null, EventArgs.Empty);

            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("en:Start_SessionLaunchFailed", startViewModel.StartSessionDraftErrorMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }


    [Fact]
    public void QuickSuggestion_AllowsRuntimeProjectionUpdates()
    {
        var suggestion = new QuickSuggestionViewModel(
            "StartView.Suggestion.Initial",
            "\uE10F",
            "Initial title",
            "Initial subtitle",
            "Initial prompt",
            new RelayCommand(() => { }));
        var changedProperties = new List<string?>();
        suggestion.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        suggestion.AutomationId = "StartView.Suggestion.Updated";
        suggestion.Icon = "\uE11B";
        suggestion.Title = "Updated title";
        suggestion.Subtitle = "Updated subtitle";
        suggestion.Prompt = "Updated prompt";

        Assert.Equal("StartView.Suggestion.Updated", suggestion.AutomationId);
        Assert.Equal("\uE11B", suggestion.Icon);
        Assert.Equal("Updated title", suggestion.Title);
        Assert.Equal("Updated subtitle", suggestion.Subtitle);
        Assert.Equal("Updated prompt", suggestion.Prompt);
        Assert.Contains(nameof(QuickSuggestionViewModel.AutomationId), changedProperties);
        Assert.Contains(nameof(QuickSuggestionViewModel.Icon), changedProperties);
        Assert.Contains(nameof(QuickSuggestionViewModel.Title), changedProperties);
        Assert.Contains(nameof(QuickSuggestionViewModel.Subtitle), changedProperties);
        Assert.Contains(nameof(QuickSuggestionViewModel.Prompt), changedProperties);
    }

    [Fact]
    public async Task StartPromptChanged_UpdatesSharedChatPromptDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-codex",
                Name = "Codex",
                Transport = TransportType.Stdio,
                StdioCommand = "codex"
            });
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.OnComposerLoaded();
            startViewModel.StartPrompt = "prefilled draft";

            Assert.Equal("prefilled draft", chat.ViewModel.CurrentPrompt);
            Assert.Equal("prefilled draft", startViewModel.StartPrompt);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_DefaultsToUnclassifiedOption()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            var option = Assert.Single(startViewModel.StartProjectOptions);

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
            Assert.Equal(NavigationProjectIds.Unclassified, option.ProjectId);
            Assert.Equal("Unclassified", option.DisplayName);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenStartWasPreparedFromExplicitProject_DefaultsToThatProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync("project-a")).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            await nav.PrepareStartForProjectAsync("project-a");
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            Assert.Equal("project-a", startViewModel.SelectedStartProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenStartWasPreparedForUnclassifiedBeforeViewConstruction_DoesNotFallbackToRecentProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync(null)).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            await nav.PrepareStartForProjectAsync(NavigationProjectIds.Unclassified);
            var conversationCatalog = new FakeConversationCatalogReadModel(
                new[]
                {
                    new ConversationCatalogItem(
                        "conv-1",
                        "Recent",
                        @"C:\Repo\Alpha",
                        DateTime.UtcNow.AddDays(-1),
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        ProjectAffinityOverrideProjectId: "project-a")
                });

            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                conversationCatalog: conversationCatalog);

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
            navigationCoordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenExistingStartViewReceivesProjectIntent_NotifiesSelectorRefresh()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync("project-a")).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            var observedSelectionRefresh = false;
            startViewModel.PropertyChanged += (_, e) =>
            {
                if (string.Equals(e.PropertyName, nameof(StartViewModel.SelectedStartProjectId), StringComparison.Ordinal))
                {
                    observedSelectionRefresh = true;
                }
            };

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);

            await nav.PrepareStartForProjectAsync("project-a");

            Assert.True(observedSelectionRefresh);
            Assert.Equal("project-a", startViewModel.SelectedStartProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenExistingStartViewReceivesUnclassifiedIntent_DoesNotFallbackToRecentProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync(null)).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            var conversationCatalog = new FakeConversationCatalogReadModel(
                new[]
                {
                    new ConversationCatalogItem(
                        "conv-1",
                        "Recent",
                        @"C:\Repo\Alpha",
                        DateTime.UtcNow.AddDays(-1),
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        ProjectAffinityOverrideProjectId: "project-a")
                });
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                conversationCatalog: conversationCatalog);

            Assert.Equal("project-a", startViewModel.SelectedStartProjectId);

            await nav.PrepareStartForProjectAsync(NavigationProjectIds.Unclassified);

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
            navigationCoordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenRecentConversationHasProject_DefaultsToRecentConversationProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var conversationCatalog = new FakeConversationCatalogReadModel(
                new[]
                {
                    new ConversationCatalogItem(
                        "conv-1",
                        "Recent",
                        @"C:\Repo\Alpha",
                        DateTime.UtcNow.AddDays(-1),
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        ProjectAffinityOverrideProjectId: "project-a")
                });

            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                conversationCatalog: conversationCatalog);

            Assert.Equal("project-a", startViewModel.SelectedStartProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_WhenCatalogRefreshArrivesOffUiThread_UpdatesOnlyAfterDispatcherDrain()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var dispatcher = new QueueingUiDispatcher();
            var conversationCatalog = new ConversationCatalogPresenter(dispatcher);

            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                conversationCatalog: conversationCatalog);

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);

            conversationCatalog.Refresh(
                new[]
                {
                    new ConversationCatalogItem(
                        "conv-1",
                        "Recent",
                        @"C:\Repo\Alpha",
                        DateTime.UtcNow.AddDays(-1),
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        ProjectAffinityOverrideProjectId: "project-a")
                });

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);

            dispatcher.RunAll();

            Assert.Equal("project-a", startViewModel.SelectedStartProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeOptions_ProjectFromChatNewSessionDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("code")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);

            Assert.True(startViewModel.IsStartModeSelectorEnabled);
            Assert.Equal("code", startViewModel.SelectedStartMode?.ModeId);
            Assert.Collection(
                startViewModel.StartModeOptions,
                first => Assert.Equal("plan", first.ModeId),
                second => Assert.Equal("code", second.ModeId));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_BeforeDraftReady_IsDisabled()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            startViewModel.OnComposerLoaded();

            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.Empty(startViewModel.StartModeOptions);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendCommand_BeforeDraftReady_DisablesSubmitButKeepsTextInputAvailable()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            startViewModel.StartPrompt = "draft";

            Assert.True(startViewModel.IsInputEnabled);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<ChatLaunchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendCommand_WhenDraftBecomesReady_EnablesSubmitFromModePolicy()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            startViewModel.StartPrompt = "draft";
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));

            await MakeStartDraftReadyAsync(chat, startViewModel);

            Assert.True(startViewModel.IsInputEnabled);
            Assert.True(startViewModel.IsStartModeSelectorEnabled);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraftError_WhenDraftFaults_DisablesSubmitButKeepsTextInputAvailable()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Local Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "agent.exe"
            });
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ThrowsAsync(new InvalidOperationException("session/new failed"));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            startViewModel.OnComposerLoaded();
            startViewModel.StartPrompt = "draft";
            await WaitForConditionAsync(() => startViewModel.HasStartSessionDraftError);

            Assert.True(startViewModel.IsInputEnabled);
            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("session/new failed", startViewModel.StartSessionDraftErrorMessage);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenSelectedAgentSwitches_WaitsForForegroundProfileAndStillBecomesReady()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-a",
                Name = "Alpha",
                RootPath = @"C:\Repo\Alpha"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Agent 1",
                Transport = TransportType.Stdio,
                StdioCommand = "acp-one",
                ConnectionTimeout = 10
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-2",
                Name = "Agent 2",
                Transport = TransportType.Stdio,
                StdioCommand = "acp-two",
                ConnectionTimeout = 10
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            chatService.SetupGet(service => service.AgentCapabilities)
                .Returns(new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Close = new SessionCloseCapabilities()
                }));
            var callCount = 0;
            chatService.Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ReturnsAsync(() =>
                {
                    ++callCount;
                    return new SessionNewResponse(
                        $"remote-{callCount}",
                        new SessionModesState
                        {
                            CurrentModeId = "code",
                            AvailableModes = new List<SalmonEgg.Acp.Protocol.SessionMode>
                            {
                                new() { Id = "code", Name = "Code" },
                                new() { Id = "plan", Name = "Plan" }
                            }
                        });
                });
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);
            startViewModel.SelectedStartProjectId = NavigationProjectIds.Unclassified;

            startViewModel.SelectedStartProjectId = "project-a";
            startViewModel.OnComposerLoaded();

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            await WaitForConditionAsync(
                () => startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.StartModeOptions.Count == 2,
                timeoutMilliseconds: 10000);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var timeoutAt = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                var connectionState = await chat.GetConnectionStateAsync();
                if (connectionState.NewSessionDraft is not null
                    && string.Equals(connectionState.NewSessionDraft.ProfileId, "profile-2", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.ConnectionInstanceId, "conn-2", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.RemoteSessionId, "remote-2", StringComparison.Ordinal)
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeStage == StartSessionModeStage.Ready)
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < timeoutAt, "Timed out waiting for profile-2 new-session draft to become ready.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            var finalState = await chat.GetConnectionStateAsync();
            Assert.Equal("profile-2", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("remote-2", finalState.NewSessionDraft?.RemoteSessionId);
            Assert.Equal(2, callCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenColdStartConnectionCompletesAfterPreviousFixedIdentityWait_RetriesAndBecomesReady()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Alpha",
                RemotePath = "/home/ubuntu/Projects/Alpha"
            });

            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            var createCalls = new List<SessionNewParams>();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Callback<SessionNewParams>(request => createCalls.Add(request))
                .ReturnsAsync(new SessionNewResponse(
                    "remote-draft-1",
                    new SessionModesState
                    {
                        CurrentModeId = "code",
                        AvailableModes =
                        [
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    }));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);
            startViewModel.SelectedStartProjectId = "remote-directory:dir-alpha";
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Loading
                    && startViewModel.StartModeSelectorProjection.PlaceholderKind == SelectorPlaceholderKind.Loading
                    && !startViewModel.HasStartSessionDraftError,
                timeoutMilliseconds: 1000);
            await WaitPastPreviousFixedDraftIdentityWaitAsync();
            var pendingWaitState = await chat.GetConnectionStateAsync();
            Assert.True(
                startViewModel.StartModeStage == StartSessionModeStage.Loading,
                $"Start mode should keep loading while the selected profile connection is still pending. automation={startViewModel.StartDraftAutomationState}; " +
                $"phase={pendingWaitState.Phase}; intent={pendingWaitState.SelectedProfileIntentId}; foreground={pendingWaitState.ForegroundTransportProfileId}; " +
                $"conn={pendingWaitState.ConnectionInstanceId}; draftPhase={pendingWaitState.NewSessionDraft?.Phase.ToString() ?? "null"}");
            Assert.True(
                startViewModel.StartModeSelectorProjection.PlaceholderKind == SelectorPlaceholderKind.Loading,
                $"Start mode placeholder should keep loading while the selected profile connection is still pending. automation={startViewModel.StartDraftAutomationState}; " +
                $"phase={pendingWaitState.Phase}; intent={pendingWaitState.SelectedProfileIntentId}; foreground={pendingWaitState.ForegroundTransportProfileId}; " +
                $"conn={pendingWaitState.ConnectionInstanceId}; draftPhase={pendingWaitState.NewSessionDraft?.Phase.ToString() ?? "null"}");
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Empty(createCalls);

            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var recovered = await WaitForConditionOrFalseAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeOptions.Count == 2
                    && !startViewModel.HasStartSessionDraftError,
                timeoutMilliseconds: 10000);
            var recoveredState = await chat.GetConnectionStateAsync();
            Assert.True(
                recovered,
                $"Start draft did not recover. automation={startViewModel.StartDraftAutomationState}; " +
                $"phase={recoveredState.Phase}; intent={recoveredState.SelectedProfileIntentId}; " +
                $"foreground={recoveredState.ForegroundTransportProfileId}; conn={recoveredState.ConnectionInstanceId}; " +
                $"draftPhase={recoveredState.NewSessionDraft?.Phase.ToString() ?? "null"}; " +
                $"draftProfile={recoveredState.NewSessionDraft?.ProfileId ?? "null"}; " +
                $"draftError={recoveredState.NewSessionDraft?.Error ?? "null"}; " +
                $"createCalls={createCalls.Count}");

            var finalState = await chat.GetConnectionStateAsync();
            Assert.Equal("profile-remote", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-remote", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Equal("remote-draft-1", finalState.NewSessionDraft?.RemoteSessionId);
            Assert.Single(createCalls);
            Assert.Equal("/home/ubuntu/Projects/Alpha", createCalls[0].Cwd);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenProfileInitializeExceedsPreviousFixedIdentityWait_KeepsLoadingUntilConnected()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        var allowProfileConnection = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var preferences = CreatePreferences();
            var commands = new Mock<IAcpConnectionCommands>();
            ChatViewModelHarness? chatHarness = null;
            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                acpConnectionCommands: commands.Object);
            chatHarness = chat;
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Claude Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "claude-agent-acp"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-2",
                Name = "Codex Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "codex-acp",
                ConnectionTimeout = 10
            });

            var createCallCount = 0;
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Returns(() =>
                {
                    createCallCount++;
                    return Task.FromResult(new SessionNewResponse(
                        $"remote-draft-{createCallCount}",
                        new SessionModesState
                        {
                            CurrentModeId = "default",
                            AvailableModes =
                            [
                                new SalmonEgg.Acp.Protocol.SessionMode { Id = "default", Name = "Default" }
                            ]
                        }));
                });
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);
            var profileConnectionStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            commands
                .Setup(command => command.ConnectToProfileAsync(
                    It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(
                    async (_, _, _, _) =>
                    {
                        Assert.NotNull(chatHarness);
                        profileConnectionStarted.TrySetResult(null);
                        await chatHarness!.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Initializing));
                        await allowProfileConnection.Task;
                        await chatHarness.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
                        await chatHarness.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));
                        await chatHarness.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                        return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
                    });

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);
            startViewModel.SelectedStartProjectId = NavigationProjectIds.Unclassified;
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && createCallCount == 1,
                timeoutMilliseconds: 3000);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];
            await profileConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            await WaitPastPreviousFixedDraftIdentityWaitAsync();

            Assert.Equal(StartSessionModeStage.Loading, startViewModel.StartModeStage);
            Assert.Equal(SelectorPlaceholderKind.Loading, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Equal(1, createCallCount);

            allowProfileConnection.SetResult(null);

            var recovered = await WaitForConditionOrFalseAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.IsStartModeSelectorEnabled
                    && createCallCount == 2,
                timeoutMilliseconds: 6000);

            var finalState = await chat.GetConnectionStateAsync();
            Assert.True(
                recovered,
                $"Start draft did not recover after delayed profile connection. automation={startViewModel.StartDraftAutomationState}; " +
                $"phase={finalState.Phase}; intent={finalState.SelectedProfileIntentId}; " +
                $"foreground={finalState.ForegroundTransportProfileId}; conn={finalState.ConnectionInstanceId}; " +
                $"draftPhase={finalState.NewSessionDraft?.Phase.ToString() ?? "null"}; " +
                $"draftProfile={finalState.NewSessionDraft?.ProfileId ?? "null"}; " +
                $"draftError={finalState.NewSessionDraft?.Error ?? "null"}; " +
                $"createCallCount={createCallCount}");
            Assert.Equal("profile-2", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-2", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Equal("remote-draft-2", finalState.NewSessionDraft?.RemoteSessionId);
        }
        finally
        {
            allowProfileConnection.TrySetCanceled(TestContext.Current.CancellationToken);
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenSupersededDraftResponseArrivesLate_PrefersNewestProfileDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        var slowResponseTcs = new TaskCompletionSource<SessionNewResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-a",
                Name = "Alpha",
                RootPath = @"C:\Repo\Alpha"
            });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-remote",
                DisplayName = "Remote",
                RemotePath = "/remote/alpha"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-2",
                Name = "Local Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "agent"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            chatService.SetupGet(service => service.AgentCapabilities)
                .Returns(new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Close = new SessionCloseCapabilities()
                }));

            var createCalls = new List<SessionNewParams>();
            chatService.Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Returns<SessionNewParams>(request =>
                {
                    createCalls.Add(request);
                    return createCalls.Count switch
                    {
                        1 => slowResponseTcs.Task,
                        2 => Task.FromResult(new SessionNewResponse(
                            "remote-2",
                            new SessionModesState
                            {
                                CurrentModeId = "code",
                                AvailableModes =
                                [
                                    new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                                    new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                                ]
                            })),
                        _ => throw new Xunit.Sdk.XunitException("Unexpected extra session/new request.")
                    };
                });
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            startViewModel.SelectedStartProjectId = "remote-directory:dir-remote";
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() => createCalls.Count == 1);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-2"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (true)
            {
                var connectionState = await chat.GetConnectionStateAsync();
                if (createCalls.Count == 2
                    && connectionState.NewSessionDraft is not null
                    && string.Equals(connectionState.NewSessionDraft.ProfileId, "profile-2", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.ConnectionInstanceId, "conn-2", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.RemoteSessionId, "remote-2", StringComparison.Ordinal)
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.StartModeOptions.Count == 2)
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < timeoutAt, "Timed out waiting for the latest profile draft to become ready before the superseded response completed.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            slowResponseTcs.SetResult(new SessionNewResponse(
                "remote-1",
                new SessionModesState
                {
                    CurrentModeId = "review",
                    AvailableModes =
                    [
                        new SalmonEgg.Acp.Protocol.SessionMode { Id = "review", Name = "Review" }
                    ]
                }));

            await Task.Delay(100, TestContext.Current.CancellationToken);

            var finalState = await chat.GetConnectionStateAsync();
            Assert.Equal("profile-2", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-2", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Equal("remote-2", finalState.NewSessionDraft?.RemoteSessionId);
            Assert.Equal(StartSessionModeStage.Ready, startViewModel.StartModeStage);
            Assert.Equal(2, startViewModel.StartModeOptions.Count);
            Assert.Equal("/remote/alpha", createCalls[0].Cwd);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), createCalls[1].Cwd);
            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
            chatService.Verify(
                service => service.CloseSessionAsync(
                    It.Is<SessionCloseParams>(request => string.Equals(request.SessionId, "remote-1", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            slowResponseTcs.TrySetCanceled(TestContext.Current.CancellationToken);
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenDisplayedProfileIsStaleButIntentAndForegroundMatch_UsesAuthoritativeForegroundProfile()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-remote",
                Name = "Remote Project",
                RootPath = "/workspace/demo"
            });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-remote",
                DisplayName = "Remote Workspace",
                RemotePath = "/workspace/demo"
            });
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-local",
                Name = "Local Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "agent"
            });

            var chatService = CreateConnectedChatService();
            chatService.SetupGet(service => service.AgentCapabilities)
                .Returns(new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Close = new SessionCloseCapabilities()
                }));
            chatService.Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ReturnsAsync(new SessionNewResponse(
                    "remote-1",
                    new SessionModesState
                    {
                        CurrentModeId = "code",
                        AvailableModes =
                        [
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    }));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            // Simulate the startup race from the real log: the visible selector still shows the
            // stale local profile, but connection intent and authoritative foreground transport
            // already belong to the remote profile.
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-remote"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);
            startViewModel.SelectedStartProjectId = "remote-directory:dir-remote";

            startViewModel.OnComposerLoaded();

            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (true)
            {
                var connectionState = await chat.GetConnectionStateAsync();
                if (connectionState.NewSessionDraft is not null
                    && string.Equals(connectionState.NewSessionDraft.ProfileId, "profile-remote", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.ConnectionInstanceId, "conn-remote", StringComparison.Ordinal)
                    && string.Equals(connectionState.NewSessionDraft.RemoteSessionId, "remote-1", StringComparison.Ordinal)
                    && startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.StartModeOptions.Count == 2)
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < timeoutAt, "Timed out waiting for the start draft to follow the authoritative foreground profile instead of the stale displayed profile.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraft_WhenRemoteProfileHasConfiguredDirectory_UsesRemoteCwdForNewSession()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-a",
                Name = "Alpha",
                RootPath = @"C:\Repo\Alpha"
            });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Alpha",
                RemotePath = "/home/ubuntu/Projects/Alpha"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Agent One",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            SessionNewParams? captured = null;
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Callback<SessionNewParams>(request => captured = request)
                .ReturnsAsync(new SessionNewResponse(
                    "remote-draft",
                    new SessionModesState
                    {
                        CurrentModeId = "default",
                        AvailableModes = [new SalmonEgg.Acp.Protocol.SessionMode { Id = "default", Name = "Default" }]
                    },
                    [new ConfigOption
                    {
                        Id = "mode",
                        Name = "Mode",
                        Category = "mode",
                        Type = "select",
                        CurrentValue = "default",
                        Options =
                        [
                            new ConfigOptionValue
                            {
                                Value = "default",
                                Name = "Default"
                            }
                        ]
                    }]));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            startViewModel.SelectedStartProjectId = "remote-directory:dir-alpha";
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 1);

            Assert.NotNull(captured);
            Assert.Equal("/home/ubuntu/Projects/Alpha", captured!.Cwd);
            Assert.False(startViewModel.HasStartSessionDraftError);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraft_WhenRemoteProfileHasNoResolvableCwd_ShowsRemoteProjectPromptWithoutDraftError()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Agent One",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() =>
                startViewModel.StartModeSelectorProjection.IsSubmitBlocked
                && string.Equals(startViewModel.StartModeSelectorProjection.SubmitBlockReason, RemoteModeProjectPrompt, StringComparison.Ordinal));

            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Equal(string.Empty, startViewModel.StartSessionDraftErrorMessage);
            Assert.Equal(SelectorPlaceholderKind.Unresolved, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.True(startViewModel.StartProjectSelectorProjection.IsSubmitBlocked);
            Assert.Equal(RemoteProjectPrompt, startViewModel.StartProjectSelectorProjection.SubmitBlockReason);
            Assert.Equal(RemoteProjectPrompt, startViewModel.SelectedStartProjectSelectorItem?.DisplayName);
            chatService.Verify(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraft_WhenRemoteProfileCwdFaults_SwitchingToLocalProfileRecoversReadyModes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-a",
                Name = "Alpha",
                RootPath = @"C:\Repo\Alpha"
            });

            var commands = new Mock<IAcpConnectionCommands>();
            ChatViewModelHarness? chatHarness = null;
            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                acpConnectionCommands: commands.Object);
            chatHarness = chat;
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-2",
                Name = "Local Agent",
                Transport = TransportType.Stdio,
                StdioCommand = "agent"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            var createCalls = new List<SessionNewParams>();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Callback<SessionNewParams>(request => createCalls.Add(request))
                .ReturnsAsync(new SessionNewResponse(
                    "remote-local",
                    new SessionModesState
                    {
                        CurrentModeId = "code",
                        AvailableModes =
                        [
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    }));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);
            commands
                .Setup(command => command.ConnectToProfileAsync(
                    It.IsAny<ServerConfiguration>(),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AcpTransportApplyResult(chatService.Object, new InitializeResponse()));
            commands
                .Setup(command => command.ConnectToProfileAsync(
                    It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(
                    async (_, _, _, _) =>
                    {
                        Assert.NotNull(chatHarness);
                        await chatHarness!.ViewModel.ReplaceChatServiceAsync(chatService.Object);
                        await chatHarness.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
                        await chatHarness.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));
                        await chatHarness.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                        return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
                    });

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() =>
                startViewModel.StartModeSelectorProjection.IsSubmitBlocked
                && string.Equals(startViewModel.StartModeSelectorProjection.SubmitBlockReason, RemoteModeProjectPrompt, StringComparison.Ordinal));
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.Empty(createCalls);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            var selectionTimeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (true)
            {
                var connectionState = await chat.GetConnectionStateAsync();
                if (string.Equals(connectionState.SelectedProfileIntentId, "profile-2", StringComparison.Ordinal))
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < selectionTimeoutAt, "Timed out waiting for the selected profile intent to advance to profile-2.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            var recovered = await WaitForConditionOrFalseAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeOptions.Count == 2
                    && !startViewModel.HasStartSessionDraftError,
                timeoutMilliseconds: 3000);

            var finalState = await chat.GetConnectionStateAsync();
            Assert.True(
                recovered,
                $"Start draft did not recover. automation={startViewModel.StartDraftAutomationState}; " +
                $"phase={finalState.Phase}; intent={finalState.SelectedProfileIntentId}; " +
                $"foreground={finalState.ForegroundTransportProfileId}; conn={finalState.ConnectionInstanceId}; " +
                $"draftPhase={finalState.NewSessionDraft?.Phase.ToString() ?? "null"}; " +
                $"draftProfile={finalState.NewSessionDraft?.ProfileId ?? "null"}; " +
                $"draftError={finalState.NewSessionDraft?.Error ?? "null"}; " +
                $"createCalls={createCalls.Count}");
            Assert.Equal("profile-2", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-2", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Equal("remote-local", finalState.NewSessionDraft?.RemoteSessionId);
            Assert.Single(createCalls);
            Assert.Equal(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                createCalls[0].Cwd);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraft_WhenRemoteProfileCwdFaults_SwitchingToConfiguredRemoteDirectory_RecoversReadyModes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Alpha",
                RemotePath = "/home/ubuntu/Projects/Alpha"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var chatService = CreateConnectedChatService();
            var createCalls = new List<SessionNewParams>();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Callback<SessionNewParams>(request => createCalls.Add(request))
                .ReturnsAsync(new SessionNewResponse(
                    "remote-draft-1",
                    new SessionModesState
                    {
                        CurrentModeId = "code",
                        AvailableModes =
                        [
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    }));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            startViewModel.OnComposerLoaded();

            // Phase 1: With unclassified project selected (no cwd for remote), the UI should ask for a remote project without projecting a draft error.
            await WaitForConditionAsync(() =>
                startViewModel.StartModeSelectorProjection.IsSubmitBlocked
                && string.Equals(startViewModel.StartModeSelectorProjection.SubmitBlockReason, RemoteModeProjectPrompt, StringComparison.Ordinal));
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.Empty(createCalls);

            // Phase 2: Switch to a configured remote project — this should trigger a new draft.
            startViewModel.SelectedStartProjectId = "remote-directory:dir-alpha";

            await WaitForConditionAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeOptions.Count == 2
                    && !startViewModel.HasStartSessionDraftError,
                timeoutMilliseconds: 3000);

            var finalState = await chat.GetConnectionStateAsync();
            Assert.Equal("profile-remote", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-remote", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Equal("remote-draft-1", finalState.NewSessionDraft?.RemoteSessionId);
            Assert.Single(createCalls);
            Assert.Equal("/home/ubuntu/Projects/Alpha", createCalls[0].Cwd);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionDraft_WhenRemoteProfileCwdFaults_WithRegistry_SwitchingToRemoteDirectory_RecoversReadyModes()
    {
        // Production wires ChatViewModel with a non-null IAcpConnectionSessionRegistry, so
        // AcpAuthoritativeConnectionResolver takes the registry branch. This test exercises that
        // branch end-to-end: a recorded session matches the foreground connection identity, and
        // switching the project to a configured remote project must recover the mode selector.
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Alpha",
                RemotePath = "/home/ubuntu/Projects/Alpha"
            });

            var registry = new InMemoryAcpConnectionSessionRegistry();
            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                connectionSessionRegistry: registry);
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote Agent",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            // The registry branch resolves the authoritative ChatService from the recorded
            // AcpConnectionSession, so CreateSessionAsync is invoked on the adapter's inner service.
            // Use ONE mock service as both the foreground service and the adapter inner, and mock
            // CreateSessionAsync on it so the draft can complete.
            var createCalls = new List<SessionNewParams>();
            var sharedChatService = CreateConnectedChatService();
            sharedChatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .Callback<SessionNewParams>(request => createCalls.Add(request))
                .ReturnsAsync(new SessionNewResponse(
                    "remote-draft-1",
                    new SessionModesState
                    {
                        CurrentModeId = "code",
                        AvailableModes =
                        [
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "code", Name = "Code" },
                            new SalmonEgg.Acp.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    }));
            var authoritativeAdapter = new AcpChatServiceAdapter(
                sharedChatService.Object,
                new AcpEventAdapter(_ => { }, new ImmediateUiDispatcher()));
            await chat.ViewModel.ReplaceChatServiceAsync(sharedChatService.Object, TestContext.Current.CancellationToken);

            // Record a session whose ConnectionInstanceId matches the foreground state below.
            registry.Upsert(new AcpConnectionSession(
                ProfileId: "profile-remote",
                Service: authoritativeAdapter,
                InitializeResponse: new InitializeResponse(),
                ConnectionReuseKey: new AcpConnectionReuseKey(TransportType.WebSocket, string.Empty, string.Empty, "ws://127.0.0.1:3010/"),
                ConnectionInstanceId: "conn-remote"));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-remote"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() =>
                startViewModel.StartModeSelectorProjection.IsSubmitBlocked
                && string.Equals(startViewModel.StartModeSelectorProjection.SubmitBlockReason, RemoteModeProjectPrompt, StringComparison.Ordinal));
            var phase1State = await chat.GetConnectionStateAsync();
            var phase1Automation = startViewModel.StartDraftAutomationState;
            Assert.True(
                phase1State.NewSessionDraft is null,
                $"Phase1 created a draft even though no remote project was selected. draftPhase={phase1State.NewSessionDraft?.Phase.ToString() ?? "null"}, automation={phase1Automation}, " +
                $"foregroundProfile={phase1State.ForegroundTransportProfileId}, connId={phase1State.ConnectionInstanceId}, " +
                $"phase={phase1State.Phase}, error={phase1State.Error ?? "(null)"}");
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.Empty(createCalls);

            startViewModel.SelectedStartProjectId = "remote-directory:dir-alpha";

            await WaitForConditionAsync(
                () => startViewModel.StartModeStage == StartSessionModeStage.Ready
                    && startViewModel.IsStartModeSelectorEnabled
                    && startViewModel.StartModeOptions.Count == 2
                    && !startViewModel.HasStartSessionDraftError,
                timeoutMilliseconds: 3000);

            var finalState = await chat.GetConnectionStateAsync();
            Assert.Equal("profile-remote", finalState.NewSessionDraft?.ProfileId);
            Assert.Equal("conn-remote", finalState.NewSessionDraft?.ConnectionInstanceId);
            Assert.Single(createCalls);
            Assert.Equal("/home/ubuntu/Projects/Alpha", createCalls[0].Cwd);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ComposerSelectorSlots_ExposeThreeVisibleStartSelectors()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            var slots = startViewModel.ComposerSelectorSlots;

            Assert.True(slots.Agent.IsVisible);
            Assert.True(slots.Mode.IsVisible);
            Assert.True(slots.Project.IsVisible);
            Assert.False(slots.Model.IsVisible);
            Assert.Same(startViewModel.SelectStartAgentDisplayCommand, slots.Agent.SelectionCommand);
            Assert.Same(startViewModel.SelectStartModeDisplayCommand, slots.Mode.SelectionCommand);
            Assert.Same(startViewModel.SelectStartProjectDisplayCommand, slots.Project.SelectionCommand);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSelectorProjection_WhenModeDraftFails_ShowsBlockingModePlaceholderWithoutClearingAgentAndProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Alpha Remote",
                RemotePath = "/remote/alpha"
            });
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent One", Transport = TransportType.StreamableHttp, ServerUrl = "https://example.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ThrowsAsync(new InvalidOperationException("session/new failed"));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            startViewModel.SelectedStartProjectId = "remote-directory:dir-alpha";

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() => startViewModel.HasStartSessionDraftError);

            Assert.Equal("session/new failed", startViewModel.StartSessionDraftErrorMessage);
            Assert.Equal(SelectorPlaceholderKind.Error, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.True(startViewModel.StartModeSelectorProjection.IsSubmitBlocked);
            Assert.Contains("Agent One", startViewModel.StartAgentSelectorProjection.DisplayItems.Select(item => item.DisplayName));
            Assert.Contains("Alpha Remote", startViewModel.StartProjectSelectorProjection.DisplayItems.Select(item => item.DisplayName));
            var slots = startViewModel.ComposerSelectorSlots;
            Assert.Contains(slots.Agent.Items, item => ReferenceEquals(item, slots.Agent.SelectedItem));
            Assert.Contains(slots.Project.Items, item => ReferenceEquals(item, slots.Project.SelectedItem));
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ComposerSelectorSlots_WhenDraftHasModelConfig_ExposeModelSelector()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(new NewSessionDraftState(
                ProfileId: "profile-1",
                Cwd: @"C:\Repo\App",
                RemoteSessionId: "remote-draft",
                ConnectionInstanceId: "conn-1",
                Phase: NewSessionDraftPhase.Ready,
                Version: 1,
                AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
                SelectedModeId: null,
                ConfigOptions: CreateModelConfigSnapshots("claude-sonnet").ToImmutableList(),
                ShowConfigOptionsPanel: true,
                AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                SessionInfo: null)));
            await WaitForConditionAsync(() =>
                startViewModel.ComposerSelectorSlots.Model.IsVisible
                && string.Equals(startViewModel.ComposerSelectorSlots.Model.SelectedItem?.SemanticValue, "claude-sonnet", StringComparison.Ordinal));

            var slots = startViewModel.ComposerSelectorSlots;

            Assert.True(slots.Model.IsVisible);
            Assert.True(slots.Model.IsEnabled);
            Assert.Same(startViewModel.SelectStartModelDisplayCommand, slots.Model.SelectionCommand);
            Assert.Equal("claude-sonnet", slots.Model.SelectedItem?.SemanticValue);
            Assert.Contains(slots.Model.Items, item => ReferenceEquals(item, slots.Model.SelectedItem));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenRequiredProfileConnectionFails_ShowsErrorInsteadOfUnresolved()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            var commands = new Mock<IAcpConnectionCommands>();
            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                acpConnectionCommands: commands.Object);
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Profile One",
                Transport = TransportType.Stdio,
                StdioCommand = "agent-1"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-2",
                Name = "Profile Two",
                Transport = TransportType.Stdio,
                StdioCommand = "agent-2"
            });

            var chatService = CreateConnectedChatService();
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken);
            commands
                .Setup(command => command.ConnectToProfileAsync(
                    It.IsAny<ServerConfiguration>(),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AcpTransportApplyResult(chatService.Object, new InitializeResponse()));
            commands
                .Setup(command => command.ConnectToProfileAsync(
                    It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("profile switch failed"));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() => startViewModel.StartModeStage == StartSessionModeStage.Unavailable);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            await WaitForConditionAsync(() =>
                string.Equals(chat.ViewModel.SelectedProfileIntentId, "profile-2", StringComparison.Ordinal));

            await WaitForConditionAsync(() =>
                startViewModel.HasStartSessionDraftError
                && startViewModel.StartModeSelectorProjection.PlaceholderKind == SelectorPlaceholderKind.Error);

            Assert.Equal("profile switch failed", startViewModel.StartSessionDraftErrorMessage);
            Assert.Equal(SelectorPlaceholderKind.Error, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.Equal("Mode unavailable", startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.True(startViewModel.StartModeSelectorProjection.IsSubmitBlocked);
            Assert.NotEqual(SelectorPlaceholderKind.Unresolved, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSelectorProjection_WhenUnclassifiedProjectSelected_DoesNotBlockSubmit()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.StartProjectSelectorProjection.SelectedDisplayItem?.SemanticValue);
            Assert.False(startViewModel.StartProjectSelectorProjection.IsSubmitBlocked);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSelectorProjection_WhenNoAgentsConfigured_ShowsNonBlockingAgentPlaceholder()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            Assert.Equal(SelectorPlaceholderKind.Default, startViewModel.StartAgentSelectorProjection.PlaceholderKind);
            Assert.Equal("No agent selected", startViewModel.SelectedStartAgentSelectorItem?.DisplayName);
            Assert.False(startViewModel.StartAgentSelectorProjection.IsSubmitBlocked);
            Assert.DoesNotContain(startViewModel.StartAgentSelectorProjection.DisplayItems, item => !item.IsPlaceholder);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelection_WhenSelectorIdentityIsStale_DoesNotChangeSelectedDraftMode()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            await MakeStartDraftReadyAsync(chat, startViewModel);
            var originalMode = startViewModel.SelectedStartMode;
            var staleItem = ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Mode,
                "code",
                "Code",
                "stale-identity");

            startViewModel.SelectStartModeDisplayCommand.Execute(staleItem);

            Assert.Same(originalMode, startViewModel.SelectedStartMode);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartAgentSelection_WhenSelectorIdentityIsStaleButProfileStillExists_SelectsProfile()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, Mock.Of<IChatLaunchWorkflow>());
            var staleItem = ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Agent,
                "profile-remote",
                "Remote",
                "stale-connection-identity");

            startViewModel.SelectStartAgentDisplayCommand.Execute(staleItem);

            Assert.Equal("profile-remote", chat.ViewModel.SelectedAcpProfile?.Id);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartAgentSelection_WhenExplicitProfileIsSelectedAgain_DoesNotRefreshSelectorProjections()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Agent One",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await WaitForConditionAsync(() =>
                string.Equals(chat.ViewModel.SelectedAcpProfile?.Id, "profile-1", StringComparison.Ordinal)
                && string.Equals(chat.ViewModel.SelectedProfileIntentId, "profile-1", StringComparison.Ordinal));

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, Mock.Of<IChatLaunchWorkflow>());
            var selectedAgentItem = Assert.Single(
                startViewModel.StartAgentSelectorItems,
                item => string.Equals(item.SemanticValue, "profile-1", StringComparison.Ordinal));
            var changedProperties = new List<string?>();
            startViewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            startViewModel.SelectStartAgentDisplayCommand.Execute(selectedAgentItem);

            Assert.Equal("profile-1", chat.ViewModel.SelectedAcpProfile?.Id);
            Assert.Equal("profile-1", chat.ViewModel.SelectedProfileIntentId);
            Assert.DoesNotContain(nameof(StartViewModel.StartAgentSelectorProjection), changedProperties);
            Assert.DoesNotContain(nameof(StartViewModel.ComposerSelectorSlots), changedProperties);
            Assert.DoesNotContain(nameof(StartViewModel.StartProjectSelectorProjection), changedProperties);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenConnectionFailsWithoutDraftError_SeparatesSelectorStatusFromErrorMessage()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.OnComposerLoaded();
            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-codex"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(
                ConnectionPhase.Disconnected,
                "Internal error: Already initialized"));

            await WaitForConditionAsync(() =>
                startViewModel.StartModeSelectorProjection.PlaceholderKind == SelectorPlaceholderKind.Error);

            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("Internal error: Already initialized", startViewModel.StartSessionDraftErrorMessage);
            Assert.Equal("Mode unavailable", startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.Equal("Mode unavailable", startViewModel.StartModeSelectorProjection.SubmitBlockReason);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenProfileChanges_DisablesExistingModesUntilFreshDraftArrives()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);
            Assert.True(startViewModel.IsStartModeSelectorEnabled);

            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent 1", Transport = TransportType.StreamableHttp, ServerUrl = "https://example-1.test" });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-2", Name = "Agent 2", Transport = TransportType.StreamableHttp, ServerUrl = "https://example-2.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.True(startViewModel.StartModeSelectorProjection.IsSubmitBlocked);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.StartModeSelectorProjection.SubmitBlockReason);
            Assert.Equal(RemoteModeProjectPrompt, startViewModel.SelectedStartModeSelectorItem?.DisplayName);
            Assert.DoesNotContain("Code", startViewModel.StartModeSelectorProjection.DisplayItems.Select(item => item.DisplayName));
            Assert.DoesNotContain("Plan", startViewModel.StartModeSelectorProjection.DisplayItems.Select(item => item.DisplayName));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartModeSelector_WhenSelectedProfileIntentChanges_ClearsExistingModeProjection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);
            Assert.True(startViewModel.IsStartModeSelectorEnabled);

            await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-2"));

            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 0);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.Null(startViewModel.SelectedStartMode);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartVoiceInput_WhenVoiceStartDoesNotEnterListening_LeavesSharedPromptUnchanged()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            var voiceInput = new FakeVoiceInputService
            {
                IsSupported = true,
                PermissionResult = new VoiceInputPermissionResult(VoiceInputPermissionStatus.Denied, "Denied")
            };
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>(), voiceInput);
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.StartPrompt = "start draft";

            Assert.True(startViewModel.CanStartVoiceInput);

            await startViewModel.StartVoiceInputCommand.ExecuteAsync(null);

            Assert.Equal("start draft", chat.ViewModel.CurrentPrompt);
            Assert.Equal("start draft", startViewModel.StartPrompt);
            Assert.False(chat.ViewModel.IsVoiceInputListening);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartVoiceInput_WhenVoiceSessionCompletes_PreservesSharedMergedPrompt()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            var voiceInput = new FakeVoiceInputService
            {
                IsSupported = true,
                PermissionResult = VoiceInputPermissionResult.Granted()
            };
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>(), voiceInput);
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.StartPrompt = "start draft";
            var expectedPrompt = "start draft dictated prompt";

            await startViewModel.StartVoiceInputCommand.ExecuteAsync(null);
            Assert.True(chat.ViewModel.IsVoiceInputListening);
            Assert.NotNull(voiceInput.LastSessionOptions);

            voiceInput.RaiseFinalResult("dictated prompt");
            voiceInput.RaiseSessionEnded();
            await WaitForConditionAsync(() => !chat.ViewModel.IsVoiceInputListening);

            Assert.Equal(expectedPrompt, chat.ViewModel.CurrentPrompt);
            Assert.Equal(expectedPrompt, startViewModel.StartPrompt);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ComposerUnloaded_DiscardsProjectedNewSessionDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);

            startViewModel.OnComposerUnloaded();

            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 0);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ComposerUnloaded_TracksDraftDiscardCleanupUntilProjectionCompletes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var uiDispatcher = new QueueingUiDispatcher();
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                uiDispatcher: uiDispatcher);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() =>
            {
                uiDispatcher.RunAll();
                return startViewModel.StartModeOptions.Count == 2;
            });

            startViewModel.OnComposerUnloaded();

            var cleanupTask = startViewModel.ComposerUnloadCleanupTask;
            Assert.False(cleanupTask.IsCompleted);

            await WaitForConditionAsync(() => uiDispatcher.PendingCount > 0 || cleanupTask.IsCompleted);
            Assert.False(cleanupTask.IsCompleted);
            uiDispatcher.RunAll();
            await cleanupTask;
            Assert.Null((await chat.GetConnectionStateAsync()).NewSessionDraft);
            uiDispatcher.RunAll();

            Assert.Empty(startViewModel.StartModeOptions);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ProfileChange_WhileComposerIsNotLoaded_DoesNotStartLaunchWorkflow()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent 1", Transport = TransportType.StreamableHttp, ServerUrl = "https://example-1.test" });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-2", Name = "Agent 2", Transport = TransportType.StreamableHttp, ServerUrl = "https://example-2.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            _ = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            workflow.Verify(x => x.StartSessionAndSendAsync(
                It.IsAny<ChatLaunchRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_SelectingProject_ProjectsToGlobalPreferences()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-b", Name = "Beta", RootPath = @"C:\Repo\Beta" });
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.SelectedStartProjectId = "project-b";

            Assert.Equal("project-b", preferences.LastSelectedProjectId);
            Assert.Equal("project-b", startViewModel.SelectedStartProjectId);
            Assert.Collection(
                startViewModel.StartProjectOptions,
                option => Assert.Equal(NavigationProjectIds.Unclassified, option.ProjectId),
                option => Assert.Equal("project-a", option.ProjectId),
                option => Assert.Equal("project-b", option.ProjectId));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelection_SelectingUnclassified_ClearsGlobalPreferences()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            preferences.LastSelectedProjectId = "project-a";

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            startViewModel.SelectedStartProjectId = NavigationProjectIds.Unclassified;

            Assert.Null(preferences.LastSelectedProjectId);
            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectOptions_WhenPreferencesProjectsChange_RefreshesProjection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);

            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-c", Name = "Cargo", RootPath = @"C:\Repo\Cargo" });
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            Assert.Collection(
                startViewModel.StartProjectOptions,
                option => Assert.Equal(NavigationProjectIds.Unclassified, option.ProjectId),
                option => Assert.Equal("project-a", option.ProjectId),
                option => Assert.Equal("project-c", option.ProjectId));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_ProjectsSubmitting_ThenClearsBusyState()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflowStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workflowCompletion = new TaskCompletionSource<ChatLaunchCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync(
                    It.Is<ChatLaunchRequest>(request => request.PromptText == "launch"),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    workflowStarted.TrySetResult(null);
                    return workflowCompletion.Task;
                });

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            var executeTask = startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);
            await workflowStarted.Task;

            Assert.True(startViewModel.IsStarting);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));

            workflowCompletion.TrySetResult(ChatLaunchCompletion.PromptDispatched);
            await executeTask;

            Assert.False(startViewModel.IsStarting);
            Assert.Equal(string.Empty, startViewModel.StartPrompt);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_FailedWorkflow_PreservesDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var exception = new InvalidOperationException("boom");
            workflow.Setup(w => w.StartSessionAndSendAsync(
                    It.Is<ChatLaunchRequest>(request => request.PromptText == "launch"),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var loggerMock = new Mock<ILogger<StartViewModel>>();
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                loggerMock.Object,
                localizer: new TestCoreStringLocalizer());
            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Start session failed")),
                    exception,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            Assert.False(startViewModel.IsStarting);
            Assert.Equal("launch", startViewModel.StartPrompt);
            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("Failed to start the session. Please try again.", startViewModel.StartSessionDraftErrorMessage);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_WhenWorkflowFails_PreservesDraftAndSurfacesError()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync(
                    It.Is<ChatLaunchRequest>(request => request.PromptText == "launch"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChatLaunchCompletion.Failed);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                localizer: new TestCoreStringLocalizer());
            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            Assert.False(startViewModel.IsStarting);
            Assert.Equal("launch", startViewModel.StartPrompt);
            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal("Failed to start the session. Please try again.", startViewModel.StartSessionDraftErrorMessage);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartSessionAndSendAsync_WhenWorkflowIncomplete_PreservesDraftWithoutError()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync(
                    It.Is<ChatLaunchRequest>(request => request.PromptText == "launch"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChatLaunchCompletion.Incomplete);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat,
                preferences,
                nav,
                workflow.Object,
                localizer: new TestCoreStringLocalizer());
            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            Assert.False(startViewModel.IsStarting);
            Assert.Equal("launch", startViewModel.StartPrompt);
            Assert.False(startViewModel.HasStartSessionDraftError);
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelector_RemoteProfile_DisablesUnclassifiedAndLocalProjectsButEnablesRemoteDirectories()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "local-a", Name = "Local A", RootPath = @"C:\Repo\A" });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-a",
                DisplayName = "Remote A",
                RemotePath = "/remote/a"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, Mock.Of<IChatLaunchWorkflow>());

            var items = startViewModel.StartProjectSelectorItems;

            Assert.Contains(items, item => item.SemanticValue == NavigationProjectIds.Unclassified && !item.IsSelectable);
            Assert.Contains(items, item => item.SemanticValue == "local-a" && !item.IsSelectable);
            Assert.Contains(items, item => item.SemanticValue == "remote-directory:dir-a" && item.IsSelectable);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartProjectSelector_WhenSwitchingToRemoteProfileFromAgentSelector_ShowsConfiguredRemoteDirectories()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "local-a", Name = "Local A", RootPath = @"C:\Repo\A" });
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-a",
                DisplayName = "Remote A",
                RemotePath = "/remote/a"
            });

            await using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-local",
                Name = "Local",
                Transport = TransportType.Stdio,
                StdioCommand = "local-agent"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, Mock.Of<IChatLaunchWorkflow>());

            Assert.DoesNotContain(
                startViewModel.StartProjectSelectorItems,
                item => string.Equals(item.SemanticValue, "remote-directory:dir-a", StringComparison.Ordinal));

            var remoteAgentItem = Assert.Single(
                startViewModel.StartAgentSelectorItems,
                item => string.Equals(item.SemanticValue, "profile-remote", StringComparison.Ordinal));

            startViewModel.SelectStartAgentDisplayCommand.Execute(remoteAgentItem);

            Assert.Equal("profile-remote", chat.ViewModel.SelectedAcpProfile?.Id);

            Assert.Contains(
                startViewModel.StartProjectSelectorItems,
                item => string.Equals(item.SemanticValue, "remote-directory:dir-a", StringComparison.Ordinal)
                        && item.IsSelectable);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task StartAgentSelector_WhenSelectingProfile_RequestsProfileConnection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            var commands = new Mock<IAcpConnectionCommands>();
            var connectedProfileIds = new List<string?>();
            commands
                .Setup(x => x.ConnectToProfileAsync(
                    It.IsAny<ServerConfiguration>(),
                    It.IsAny<IAcpTransportConfiguration>(),
                    It.IsAny<IAcpChatCoordinatorSink>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(
                    (profile, _, _, _) => connectedProfileIds.Add(profile.Id))
                .ReturnsAsync(new AcpTransportApplyResult(CreateConnectedChatService().Object, new InitializeResponse()));

            await using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                acpConnectionCommands: commands.Object);
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-local",
                Name = "Local",
                Transport = TransportType.Stdio,
                StdioCommand = "local-agent"
            });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
            {
                Id = "profile-remote",
                Name = "Remote",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://127.0.0.1:3010/"
            });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat, preferences, nav, Mock.Of<IChatLaunchWorkflow>());
            var remoteAgentItem = Assert.Single(
                startViewModel.StartAgentSelectorItems,
                item => string.Equals(item.SemanticValue, "profile-remote", StringComparison.Ordinal));

            startViewModel.SelectStartAgentDisplayCommand.Execute(remoteAgentItem);

            await WaitForConditionAsync(() =>
                connectedProfileIds.Contains("profile-remote", StringComparer.Ordinal));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static ChatViewModelHarness CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager,
        IVoiceInputService? voiceInputService = null,
        IUiDispatcher? uiDispatcher = null,
        IAcpConnectionSessionRegistry? connectionSessionRegistry = null,
        IAcpConnectionCommands? acpConnectionCommands = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStore = new ChatStore(state);
        var transportFactory = new Mock<ITransportFactory>();
        var errorLogger = new Mock<IErrorLogger>();
        var serilog = new Mock<SerilogLogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            errorLogger.Object,
            sessionManager,
            Mock.Of<IAcpClientFactory>(),
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object, new ImmediateUiDispatcher());

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ConversationDocument());

        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var effectiveUiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        var workspace = new ChatConversationWorkspace(
            sessionManager,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            effectiveUiDispatcher);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var conversationCatalogFacade = new ConversationCatalogFacade(
            workspace,
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            conversationCatalogPresenter,
            NullLogger<ConversationCatalogFacade>.Instance);
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var chatStateProjector = new ChatStateProjector();
            var viewModel = new ChatViewModel(
                chatStore,
                configService.Object,
                preferences,
                profiles,
                sessionManager,
                miniWindow.Object,
                workspace,
                conversationCatalogPresenter,
                chatStateProjector,
                null,
                connectionStore,
                effectiveUiDispatcher,
                Mock.Of<IConversationPreviewStore>(),
                vmLogger.Object,
                new StaticMcpResolver([]),
                voiceInputService: voiceInputService,
                conversationCatalogFacade: conversationCatalogFacade,
                acpConnectionCommands: acpConnectionCommands ?? Mock.Of<IAcpConnectionCommands>(),
                connectionSessionRegistry: connectionSessionRegistry);
            conversationCatalogFacade.SetPanelCleanup(viewModel);
            return new ChatViewModelHarness(
                viewModel,
                state,
                connectionState,
                connectionStore,
                conversationCatalogPresenter,
                workspace,
                effectiveUiDispatcher);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
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

    private static MainNavigationViewModel CreateNavigationViewModel(
        ChatViewModelHarness chat,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        INavigationCoordinator? navigationCoordinator = null)
    {
        var ui = new Mock<IUiInteractionService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var navState = new FakeNavigationPaneState();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var effectiveNavigationCoordinator = navigationCoordinator ?? Mock.Of<INavigationCoordinator>();

        var conversationCatalog = new ConversationCatalogFacade(
            chat.Workspace,
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            chat.Presenter,
            NullLogger<ConversationCatalogFacade>.Instance);

        return new MainNavigationViewModel(
            conversationCatalog,
                new NavigationProjectPreferencesAdapter(preferences),
                ui.Object,
                effectiveNavigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            new ShellNavigationRuntimeStateStore(),
            chat.Presenter,
            new ProjectAffinityResolver(),
            new ImmediateUiDispatcher(),
            Mock.Of<IStringLocalizer<CoreStrings>>());
    }

    private static StartViewModel CreateStartViewModel(
        ChatViewModelHarness chat,
        AppPreferencesViewModel preferences,
        MainNavigationViewModel nav,
        IChatLaunchWorkflow workflow,
        ILogger<StartViewModel>? logger = null,
        IConversationCatalogReadModel? conversationCatalog = null,
        IStringLocalizer<CoreStrings>? localizer = null,
        IAppLanguageService? languageService = null)
    {
        return new StartViewModel(
            chatViewModel: chat.ViewModel,
            sessionManager: Mock.Of<ISessionManager>(),
            preferences: preferences,
            projectPreferences: new NavigationProjectPreferencesAdapter(preferences),
            projectSelectionStore: new NavigationProjectSelectionStoreAdapter(preferences),
            navigationCoordinator: Mock.Of<INavigationCoordinator>(),
            nav: nav,
            logger: logger ?? Mock.Of<ILogger<StartViewModel>>(),
            chatLaunchWorkflow: workflow,
            chatConnectionStore: chat.ConnectionStore,
            conversationCatalog: conversationCatalog,
            localizer: localizer,
            languageService: languageService);
    }

    private static NewSessionDraftState CreateReadyDraft(string selectedModeId)
        => new(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: "remote-draft",
            ConnectionInstanceId: "conn-1",
            Phase: NewSessionDraftPhase.Ready,
            Version: 1,
            AvailableModes: ImmutableList.Create(
                new ConversationModeOptionSnapshot
                {
                    ModeId = "plan",
                    ModeName = "Plan"
                },
                new ConversationModeOptionSnapshot
                {
                    ModeId = "code",
                    ModeName = "Code"
                }),
            SelectedModeId: selectedModeId,
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null);

    private static List<ConversationConfigOptionSnapshot> CreateModelConfigSnapshots(string selectedValue)
        => new()
        {
            new ConversationConfigOptionSnapshot
            {
                Id = "model",
                Name = "Model",
                Category = "model",
                ValueType = "select",
                SelectedValue = selectedValue,
                Options = new List<ConversationConfigOptionChoiceSnapshot>
                {
                    new() { Value = "claude-sonnet", Name = "Sonnet", Description = "Balanced model" },
                    new() { Value = "claude-opus", Name = "Opus", Description = "Reasoning model" }
                }
            }
        };

    private static Mock<IChatService> CreateConnectedChatService()
    {
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService.SetupGet(service => service.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        return chatService;
    }

    private static void VerifyNoProfileConnection(Mock<IAcpConnectionCommands> commands)
    {
        commands.Verify(
            command => command.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        commands.Verify(
            command => command.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async Task MakeStartDraftReadyAsync(
        ChatViewModelHarness chat,
        StartViewModel startViewModel)
    {
        await chat.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-1"));
        await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
        await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
        await WaitForConditionAsync(() =>
            startViewModel.StartModeOptions.Count == 2
            && startViewModel.IsStartModeSelectorEnabled);
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMilliseconds = 2000, int pollDelayMilliseconds = 20)
    {
        timeoutMilliseconds = Math.Max(timeoutMilliseconds, 10000);
        var started = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds >= timeoutMilliseconds)
            {
                throw new TimeoutException("Timed out waiting for expected asynchronous condition.");
            }

            await Task.Delay(pollDelayMilliseconds);
        }
    }

    private static Task WaitPastPreviousFixedDraftIdentityWaitAsync()
        => Task.Delay(PreviousFixedDraftIdentityWaitTimeout + DraftIdentityWaitRegressionBuffer);

    private static async Task<bool> WaitForConditionOrFalseAsync(Func<bool> predicate, int timeoutMilliseconds = 2000, int pollDelayMilliseconds = 20)
    {
        var started = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds >= timeoutMilliseconds)
            {
                return false;
            }

            await Task.Delay(pollDelayMilliseconds);
        }

        return true;
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen { get; private set; }
        public event EventHandler? PaneStateChanged;

        public async Task SetPaneOpen(bool isOpen)
        {
            if (IsPaneOpen == isOpen)
            {
                return;
            }

            IsPaneOpen = isOpen;
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class FakeConversationCatalogReadModel : IConversationCatalogReadModel
    {
        public FakeConversationCatalogReadModel(IReadOnlyList<ConversationCatalogItem> snapshot)
        {
            Snapshot = snapshot;
        }

        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 1;

        public IReadOnlyList<ConversationCatalogItem> Snapshot { get; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeVoiceInputService : IVoiceInputService
    {
        public bool IsSupported { get; set; }

        public bool IsListening { get; private set; }

        public VoiceInputSessionOptions? LastSessionOptions { get; private set; }

        public VoiceInputPermissionResult PermissionResult { get; set; } =
            new(VoiceInputPermissionStatus.Unsupported, "Not configured");

        public event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

        public event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

        public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

        public event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

        public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionResult);

        public Task<VoiceInputPermissionResult> GetPermissionStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionResult);

        public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
        {
            if (!PermissionResult.IsGranted)
            {
                throw new VoiceInputStartFailureException(
                    PermissionResult.Message ?? "Voice input permission denied.",
                    requiresAuthorization: PermissionResult.RequiresAuthorization);
            }

            LastSessionOptions = options;
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsListening = false;
            return Task.CompletedTask;
        }

        public void RaiseFinalResult(string text)
        {
            var options = LastSessionOptions ?? throw new InvalidOperationException("Voice input session has not started.");
            FinalResultReceived?.Invoke(this, new VoiceInputFinalResult(options.RequestId, text));
        }

        public void RaisePartialResult(string text)
        {
            var options = LastSessionOptions ?? throw new InvalidOperationException("Voice input session has not started.");
            PartialResultReceived?.Invoke(this, new VoiceInputPartialResult(options.RequestId, text));
        }

        public void RaiseSessionEnded()
        {
            var options = LastSessionOptions ?? throw new InvalidOperationException("Voice input session has not started.");
            IsListening = false;
            SessionEnded?.Invoke(this, new VoiceInputSessionEndedResult(options.RequestId));
        }

        public void RaiseError(string message)
        {
            var options = LastSessionOptions ?? throw new InvalidOperationException("Voice input session has not started.");
            IsListening = false;
            ErrorOccurred?.Invoke(this, new VoiceInputErrorResult(options.RequestId, message));
        }

        public void Dispose()
        {
        }
    }

    private sealed class StaticMcpResolver : IAcpMcpServerResolver
    {
        private readonly IReadOnlyList<McpServer> _servers;

        public StaticMcpResolver(IReadOnlyList<McpServer> servers)
        {
            _servers = McpServerSnapshots.CloneServers(servers);
        }

        public Task<IReadOnlyList<McpServer>> ResolveCurrentMcpServersAsync(
            IAcpChatCoordinatorSink sink,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = McpServerSnapshots.CloneServers(_servers);
            sink.SetCurrentMcpServers(snapshot);
            return Task.FromResult<IReadOnlyList<McpServer>>(snapshot);
        }
    }

    private sealed class ChatViewModelHarness : IAsyncDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        private readonly IChatConnectionStore _connectionStore;
        private readonly IUiDispatcher _uiDispatcher;
        public IChatConnectionStore ConnectionStore => _connectionStore;
        public ConversationCatalogPresenter Presenter { get; }
        public ChatViewModel ViewModel { get; }
        public ChatConversationWorkspace Workspace { get; }

        public ChatViewModelHarness(
            ChatViewModel viewModel,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            IChatConnectionStore connectionStore,
            ConversationCatalogPresenter presenter,
            ChatConversationWorkspace workspace,
            IUiDispatcher uiDispatcher)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            _connectionStore = connectionStore;
            _uiDispatcher = uiDispatcher;
            Presenter = presenter;
            Workspace = workspace;
        }

        public async ValueTask DispatchConnectionAsync(ChatConnectionAction action)
        {
            await _connectionStore.Dispatch(action);
            var projectionTask = ViewModel.ApplyLatestNewSessionDraftProjectionAsync();
            if (_uiDispatcher.HasThreadAccess)
            {
                await projectionTask;
            }
        }

        public ValueTask<ChatConnectionState> GetConnectionStateAsync()
            => _connectionStore.GetCurrentStateAsync();

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            await _connectionState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
