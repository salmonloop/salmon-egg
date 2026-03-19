using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class NavigationCoordinatorTests
{
    [Fact]
    public async Task ActivateSessionAsync_UpdatesNavAndChatInOnePath()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-1", chat.ViewModel.CurrentSessionId);
            shellNavigation.Verify(s => s.NavigateToChat(), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ShowAllSessionsForProjectAsync_PickedSession_UsesCoordinatorActivationPath()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-1", @"C:\repo\demo")
                {
                    DisplayName = "Session 1",
                    LastActivityAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new Session("session-2", @"C:\repo\demo")
                {
                    DisplayName = "Session 2",
                    LastActivityAt = DateTime.UtcNow.AddMinutes(-1)
                });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();
            var ui = new Mock<IUiInteractionService>();
            Action<string>? pickSession = null;

            ui.Setup(s => s.ShowSessionsListDialogAsync(
                    "会话",
                    It.IsAny<IReadOnlyList<SessionNavItemViewModel>>(),
                    It.IsAny<Action<string>>()))
                .Callback<string, IReadOnlyList<SessionNavItemViewModel>, Action<string>>((_, _, onPickSession) => pickSession = onPickSession)
                .Returns(Task.CompletedTask);

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState, ui.Object);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            await chat.ViewModel.TrySwitchToSessionAsync("session-1");
            navVm.RebuildTree();

            await navVm.ShowAllSessionsForProjectAsync("project-1");

            Assert.NotNull(pickSession);

            pickSession!("session-2");
            await WaitForConditionAsync(() => string.Equals(chat.ViewModel.CurrentSessionId, "session-2", StringComparison.Ordinal));

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("project-1", preferences.LastSelectedProjectId);
            shellNavigation.Verify(s => s.NavigateToChat(), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ToggleProjectAsync_DoesNotMutateSemanticSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            await chat.ViewModel.TrySwitchToSessionAsync("session-1");
            navVm.RebuildTree();

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var originalExpanded = project.IsExpanded;

            await coordinator.ToggleProjectAsync("project-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.NotEqual(originalExpanded, project.IsExpanded);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SyncSelectionFromShellContent_Start_SelectsStart()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager();
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Start, currentSessionId: null);

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SyncSelectionFromShellContent_ChatWithSession_SelectsSession()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-2", @"C:\repo\demo")
            {
                DisplayName = "Session 2"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            await chat.ViewModel.TrySwitchToSessionAsync("session-2");
            navVm.RebuildTree();
            await coordinator.ActivateStartAsync();
            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Chat, "session-2");

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SyncSelectionFromShellContent_ChatWithoutSession_KeepsExistingSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            await coordinator.ActivateSessionAsync("session-1", "project-1");
            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Chat, currentSessionId: null);

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-1", selection.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SyncSelectionFromShellContent_Settings_SelectsSettings()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager();
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new Mock<IShellNavigationService>();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            using var navVm = CreateNavigationViewModel(chat.ViewModel, sessionManager.Object, preferences, navState);
            var coordinator = new NavigationCoordinator(navVm, chat.ViewModel, preferences, shellNavigation.Object);

            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Settings, currentSessionId: null);

            Assert.Equal(NavigationSelectionState.SettingsSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState,
        IUiInteractionService? ui = null)
    {
        var shellNavigation = new Mock<IShellNavigationService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var projector = new NavigationSelectionProjector();

        return new MainNavigationViewModel(
            chatViewModel,
            sessionManager,
            preferences,
            ui ?? Mock.Of<IUiInteractionService>(),
            shellNavigation.Object,
            navLogger.Object,
            navState,
            metricsSink.Object,
            projector);
    }

    private static Mock<ISessionManager> CreateSessionManager(params Session[] sessions)
    {
        var sessionManager = new Mock<ISessionManager>();
        foreach (var session in sessions)
        {
            sessionManager.Setup(s => s.GetSession(session.SessionId)).Returns(session);
        }

        return sessionManager;
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

    private static ChatViewModelHarness CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

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
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ConversationDocument());

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
                vmLogger.Object,
                syncContext);
            return new ChatViewModelHarness(viewModel, state);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class ChatViewModelHarness : IDisposable
    {
        private readonly IState<ChatState> _state;

        public ChatViewModelHarness(ChatViewModel viewModel, IState<ChatState> state)
        {
            ViewModel = viewModel;
            _state = state;
        }

        public ChatViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static AppPreferencesViewModel CreatePreferencesWithProject()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Demo",
            RootPath = @"C:\repo\demo"
        });

        return preferences;
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate());
    }
}
