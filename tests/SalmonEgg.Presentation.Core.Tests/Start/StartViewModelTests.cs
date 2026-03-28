using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Start;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
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

            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

            startViewModel.StartPrompt = "  hello  ";

            await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

            workflow.Verify(w => w.StartSessionAndSendAsync("hello", It.IsAny<CancellationToken>()), Times.Once);
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
            workflow.Setup(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
            var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

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
    public void ExecuteSuggestion_DoesNotMutateChatViewModelDraft()
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

            var suggestion = startViewModel.Suggestions[0];
            startViewModel.ExecuteSuggestionCommand.Execute(suggestion);

            Assert.Equal("chat draft", chat.ViewModel.CurrentPrompt);
            Assert.Equal(suggestion.Prompt, startViewModel.StartPrompt);
            workflow.Verify(w => w.StartSessionAndSendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static ChatViewModelHarness CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager)
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
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            sessionManager,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ConversationDocument());

        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var chatStateProjector = new ChatStateProjector();
            var viewModel = new ChatViewModel(
                chatStore,
                chatServiceFactory,
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
                vmLogger.Object,
                syncContext);
            return new ChatViewModelHarness(viewModel, state, connectionState, conversationCatalogPresenter);
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
            prefsLogger.Object);
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        ChatViewModelHarness chat,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences)
    {
        var ui = new Mock<IUiInteractionService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var navState = new FakeNavigationPaneState();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var navigationCoordinator = Mock.Of<INavigationCoordinator>();

        return new MainNavigationViewModel(
            chat.ViewModel,
            new NavigationProjectPreferencesAdapter(preferences),
            ui.Object,
            Mock.Of<IShellNavigationService>(),
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            chat.Presenter,
            new ProjectAffinityResolver());
    }

    private static StartViewModel CreateStartViewModel(
        ChatViewModel chatViewModel,
        AppPreferencesViewModel preferences,
        MainNavigationViewModel nav,
        IChatLaunchWorkflow workflow,
        ILogger<StartViewModel>? logger = null)
    {
        return new StartViewModel(
            chatViewModel: chatViewModel,
            sessionManager: Mock.Of<ISessionManager>(),
            preferences: preferences,
            navigationCoordinator: Mock.Of<INavigationCoordinator>(),
            nav: nav,
            logger: logger ?? Mock.Of<ILogger<StartViewModel>>(),
            chatLaunchWorkflow: workflow);
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

    private sealed class ChatViewModelHarness : IDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        public ConversationCatalogPresenter Presenter { get; }
        public ChatViewModel ViewModel { get; }

        public ChatViewModelHarness(
            ChatViewModel viewModel,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            ConversationCatalogPresenter presenter)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            Presenter = presenter;
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            _connectionState.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
