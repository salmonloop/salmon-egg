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
    public async Task StartSessionAndSendAsync_DoesNotNavigate_WhenSwitchFails()
    {
        var originalContext = SynchronizationContext.Current;
        var throwingContext = new ControlledThrowSynchronizationContext();

        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        using var chat = CreateChatViewModel(throwingContext, preferences, sessionManager.Object);
        var chatViewModel = chat.ViewModel;

        SynchronizationContext.SetSynchronizationContext(originalContext);

        var ui = new Mock<IUiInteractionService>();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var navState = new FakeNavigationPaneState();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        using var nav = new MainNavigationViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            ui.Object,
            Mock.Of<IShellNavigationService>(),
            navLogger.Object,
            navState,
            metricsSink.Object);

        var startLogger = new Mock<ILogger<StartViewModel>>();
        var startViewModel = new StartViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            navigationCoordinator.Object,
            nav,
            startLogger.Object);

        chatViewModel.CurrentPrompt = "hello";
        throwingContext.ThrowNextPost();

        await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

        navigationCoordinator.Verify(n => n.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        navigationCoordinator.Verify(n => n.ActivateSettingsAsync(It.IsAny<string>()), Times.Never);
        Assert.Empty(chatViewModel.MessageHistory);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_DoesNotNavigate_WhenConnectionInProgress()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new SynchronizationContext();

        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
        var chatViewModel = chat.ViewModel;
        chatViewModel.IsConnecting = true;

        SynchronizationContext.SetSynchronizationContext(originalContext);

        var ui = new Mock<IUiInteractionService>();
        var navigationCoordinator = new Mock<INavigationCoordinator>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var navState = new FakeNavigationPaneState();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        using var nav = new MainNavigationViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            ui.Object,
            Mock.Of<IShellNavigationService>(),
            navLogger.Object,
            navState,
            metricsSink.Object);

        var startLogger = new Mock<ILogger<StartViewModel>>();
        var startViewModel = new StartViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            navigationCoordinator.Object,
            nav,
            startLogger.Object);

        chatViewModel.CurrentPrompt = "hello";

        await startViewModel.StartSessionAndSendCommand.ExecuteAsync(null);

        navigationCoordinator.Verify(n => n.ActivateSettingsAsync(It.IsAny<string>()), Times.Never);
    }

    private static ChatViewModelHarness CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
        var serilog = new Mock<SerilogLogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
            sessionManager,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(CancellationToken.None)).ReturnsAsync(new ConversationDocument());

        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var viewModel = new ChatViewModel(
                chatStore.Object,
                chatServiceFactory,
                configService.Object,
                preferences,
                profiles,
                sessionManager,
                conversationStore.Object,
                miniWindow.Object,
                vmLogger.Object);
            return new ChatViewModelHarness(viewModel, state);
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

    private sealed class ControlledThrowSynchronizationContext : SynchronizationContext
    {
        private bool _throwNext;

        public void ThrowNextPost()
        {
            _throwNext = true;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_throwNext)
            {
                _throwNext = false;
                throw new InvalidOperationException("Injected failure");
            }

            d(state);
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

    private sealed class ChatViewModelHarness : IDisposable
    {
        private readonly IState<ChatState> _state;
        public ChatViewModel ViewModel { get; }

        public ChatViewModelHarness(ChatViewModel viewModel, IState<ChatState> state)
        {
            ViewModel = viewModel;
            _state = state;
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
