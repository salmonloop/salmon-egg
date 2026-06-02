using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
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
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Start;

[Collection("NonParallel")]
public sealed class StartViewModelTests
{
    [Fact]
    public async Task StartSessionAndSendAsync_DoesNotInvokeWorkflow_WhenPromptIsBlank()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            var startLogger = new Mock<ILogger<StartViewModel>>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object, startLogger.Object);

            startViewModel.StartPrompt = "   ";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);

            startViewModel.StartPrompt = "  hello  ";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync("hello", NavigationProjectIds.Unclassified, It.IsAny<CancellationToken>()), Times.Once);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);
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
    public void ExecuteSuggestion_UpdatesSharedChatPromptDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

            chat.ViewModel.CurrentPrompt = "chat draft";
            startViewModel.OnComposerLoaded();

            var suggestion = startViewModel.Suggestions[0];
            startViewModel.ExecuteSuggestionCommand.Execute(suggestion);

            Assert.Equal(suggestion.Prompt, chat.ViewModel.CurrentPrompt);
            Assert.Equal(suggestion.Prompt, startViewModel.StartPrompt);
            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void StartPromptChanged_UpdatesSharedChatPromptDraft()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public void StartProjectSelection_DefaultsToUnclassifiedOption()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

            var option = Assert.Single(startViewModel.StartProjectOptions);

            Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.SelectedStartProjectId);
            Assert.Equal(NavigationProjectIds.Unclassified, option.ProjectId);
            Assert.Equal("未归类", option.DisplayName);
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

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync("project-a")).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            await nav.PrepareStartForProjectAsync("project-a");
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
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
                chat.ViewModel,
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

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator.Setup(x => x.ActivateStartAsync("project-a")).ReturnsAsync(true);
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences, navigationCoordinator.Object);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);
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

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
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
                chat.ViewModel,
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
    public void StartProjectSelection_WhenRecentConversationHasProject_DefaultsToRecentConversationProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
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
                chat.ViewModel,
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
    public void StartProjectSelection_WhenCatalogRefreshArrivesOffUiThread_UpdatesOnlyAfterDispatcherDrain()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var dispatcher = new QueueingUiDispatcher();
            var conversationCatalog = new ConversationCatalogPresenter(dispatcher);

            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
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
    public void StartModeSelector_BeforeDraftReady_IsDisabled()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            startViewModel.StartPrompt = "draft";

            Assert.True(startViewModel.IsInputEnabled);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ThrowsAsync(new InvalidOperationException("session/new failed"));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object);
            await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            startViewModel.OnComposerLoaded();
            startViewModel.StartPrompt = "draft";
            await WaitForConditionAsync(() => startViewModel.HasStartSessionDraftError);

            Assert.True(startViewModel.IsInputEnabled);
            Assert.True(startViewModel.HasStartSessionDraftError);
            Assert.Equal(
                "Unable to load session configuration. Check the connection and try again.",
                startViewModel.StartSessionDraftErrorMessage);
            Assert.DoesNotContain("session/new failed", startViewModel.StartSessionDraftErrorMessage, StringComparison.Ordinal);
            Assert.False(startViewModel.IsStartModeSelectorEnabled);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void ComposerSelectorSlots_ExposeThreeVisibleStartSelectors()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

            var slots = startViewModel.ComposerSelectorSlots;

            Assert.True(slots.Agent.IsVisible);
            Assert.True(slots.Mode.IsVisible);
            Assert.True(slots.Project.IsVisible);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent One", Transport = TransportType.HttpSse, ServerUrl = "https://example.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];
            var chatService = CreateConnectedChatService();
            chatService
                .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
                .ThrowsAsync(new InvalidOperationException("session/new failed"));
            await chat.ViewModel.ReplaceChatServiceAsync(chatService.Object);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

            await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            startViewModel.OnComposerLoaded();

            await WaitForConditionAsync(() => startViewModel.HasStartSessionDraftError);

            Assert.Equal(SelectorPlaceholderKind.Error, startViewModel.StartModeSelectorProjection.PlaceholderKind);
            Assert.True(startViewModel.StartModeSelectorProjection.IsSubmitBlocked);
            Assert.Contains("Agent One", startViewModel.StartAgentSelectorProjection.DisplayItems.Select(item => item.DisplayName));
            Assert.Contains("Alpha", startViewModel.StartProjectSelectorProjection.DisplayItems.Select(item => item.DisplayName));
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public void StartSelectorProjection_WhenNoAgentsConfigured_ShowsNonBlockingAgentPlaceholder()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

            Assert.Equal(SelectorPlaceholderKind.Default, startViewModel.StartAgentSelectorProjection.PlaceholderKind);
            Assert.Equal("未选择 Agent", startViewModel.SelectedStartAgentSelectorItem?.DisplayName);
            Assert.False(startViewModel.StartAgentSelectorProjection.IsSubmitBlocked);
            Assert.Empty(startViewModel.StartAgentSelectorProjection.DisplayItems.Where(item => !item.IsPlaceholder));
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public async Task StartModeSelector_WhenProfileChanges_DisablesExistingModesUntilFreshDraftArrives()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);
            Assert.True(startViewModel.IsStartModeSelectorEnabled);

            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent 1", Transport = TransportType.HttpSse, ServerUrl = "https://example-1.test" });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-2", Name = "Agent 2", Transport = TransportType.HttpSse, ServerUrl = "https://example-2.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            Assert.False(startViewModel.IsStartModeSelectorEnabled);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
            await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
            await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(CreateReadyDraft("plan")));
            await WaitForConditionAsync(() => startViewModel.StartModeOptions.Count == 2);
            Assert.True(startViewModel.IsStartModeSelectorEnabled);

            await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-2"));

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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>(), voiceInput);
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>(), voiceInput);
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
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
            using var chat = CreateChatViewModel(
                syncContext,
                preferences,
                Mock.Of<ISessionManager>(),
                uiDispatcher: uiDispatcher);

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(
                chat.ViewModel,
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
    public void ProfileChange_WhileComposerIsNotLoaded_DoesNotStartLaunchWorkflow()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent 1", Transport = TransportType.HttpSse, ServerUrl = "https://example-1.test" });
            chat.ViewModel.AcpProfileList.Add(new ServerConfiguration { Id = "profile-2", Name = "Agent 2", Transport = TransportType.HttpSse, ServerUrl = "https://example-2.test" });
            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            _ = CreateStartViewModel(
                chat.ViewModel,
                preferences,
                nav,
                workflow.Object);

            chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[1];

            workflow.Verify(x => x.StartSessionAndSendAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void StartProjectSelection_SelectingProject_ProjectsToGlobalPreferences()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-b", Name = "Beta", RootPath = @"C:\Repo\Beta" });
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public void StartProjectSelection_SelectingUnclassified_ClearsGlobalPreferences()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
            preferences.LastSelectedProjectId = "project-a";

            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public void StartProjectOptions_WhenPreferencesProjectsChange_RefreshesProjection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferences();
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflowStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workflowCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workflow = new Mock<IChatLaunchWorkflow>();
            workflow.Setup(w => w.StartSessionAndSendAsync("launch", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    workflowStarted.TrySetResult(null);
                    return workflowCompletion.Task;
                });

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);
            await MakeStartDraftReadyAsync(chat, startViewModel);
            startViewModel.StartPrompt = "launch";

            var executeTask = startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);
            await workflowStarted.Task;

            Assert.True(startViewModel.IsStarting);
            Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));

            workflowCompletion.TrySetResult(null);
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
            using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
            var workflow = new Mock<IChatLaunchWorkflow>();
            var exception = new InvalidOperationException("boom");
            workflow.Setup(w => w.StartSessionAndSendAsync("launch", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var loggerMock = new Mock<ILogger<StartViewModel>>();
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object, loggerMock.Object);
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
            Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
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
        IUiDispatcher? uiDispatcher = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStore = new ChatStore(state);
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
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
            new NavigationProjectPreferencesAdapter(preferences),
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
                acpConnectionCommands: Mock.Of<IAcpConnectionCommands>());
            conversationCatalogFacade.SetPanelCleanup(viewModel);
            return new ChatViewModelHarness(viewModel, state, connectionState, connectionStore, conversationCatalogPresenter, workspace);
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
            new NavigationProjectPreferencesAdapter(preferences),
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
        ChatViewModel chatViewModel,
        AppPreferencesViewModel preferences,
        MainNavigationViewModel nav,
        IChatLaunchWorkflow workflow,
        ILogger<StartViewModel>? logger = null,
        IConversationCatalogReadModel? conversationCatalog = null)
    {
        return new StartViewModel(
            chatViewModel: chatViewModel,
            sessionManager: Mock.Of<ISessionManager>(),
            preferences: preferences,
            projectPreferences: new NavigationProjectPreferencesAdapter(preferences),
            projectSelectionStore: new NavigationProjectSelectionStoreAdapter(preferences),
            navigationCoordinator: Mock.Of<INavigationCoordinator>(),
            nav: nav,
            logger: logger ?? Mock.Of<ILogger<StartViewModel>>(),
            chatLaunchWorkflow: workflow,
            conversationCatalog: conversationCatalog);
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

    private static Mock<IChatService> CreateConnectedChatService()
    {
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService.SetupGet(service => service.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        return chatService;
    }

    private static async Task MakeStartDraftReadyAsync(
        ChatViewModelHarness chat,
        StartViewModel startViewModel)
    {
        await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
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

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen { get; private set; }
        public event EventHandler? PaneStateChanged;

        public void SetPaneOpen(bool isOpen)
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
            _servers = McpServerJsonConverter.CloneServers(servers);
        }

        public Task<IReadOnlyList<McpServer>> ResolveCurrentMcpServersAsync(
            IAcpChatCoordinatorSink sink,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = McpServerJsonConverter.CloneServers(_servers);
            sink.SetCurrentMcpServers(snapshot);
            return Task.FromResult<IReadOnlyList<McpServer>>(snapshot);
        }
    }

    private sealed class ChatViewModelHarness : IDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        private readonly IChatConnectionStore _connectionStore;
        public ConversationCatalogPresenter Presenter { get; }
        public ChatViewModel ViewModel { get; }
        public ChatConversationWorkspace Workspace { get; }

        public ChatViewModelHarness(
            ChatViewModel viewModel,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            IChatConnectionStore connectionStore,
            ConversationCatalogPresenter presenter,
            ChatConversationWorkspace workspace)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            _connectionStore = connectionStore;
            Presenter = presenter;
            Workspace = workspace;
        }

        public ValueTask DispatchConnectionAsync(ChatConnectionAction action)
            => _connectionStore.Dispatch(action);

        public ValueTask<ChatConnectionState> GetConnectionStateAsync()
            => _connectionStore.GetCurrentStateAsync();

        public void Dispose()
        {
            ViewModel.Dispose();
            _connectionState.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
