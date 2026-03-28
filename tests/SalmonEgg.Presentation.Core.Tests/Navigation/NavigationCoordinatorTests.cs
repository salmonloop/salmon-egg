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
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using System.Reflection;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class NavigationCoordinatorTests
{
    [Fact]
    public void ShellNavigationService_UsesExplicitAsyncResultContract_ForChatNavigation()
    {
        var navigateToChat = typeof(IShellNavigationService).GetMethod(
            nameof(IShellNavigationService.NavigateToChat),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(navigateToChat);
        Assert.NotEqual(typeof(void), navigateToChat!.ReturnType);
    }

    [Fact]
    public async Task ActivateSessionAsync_UpdatesNavAndNavigates_WhenSwitcherSucceeds()
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
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenSwitcherFails_DoesNotCommitSelectionOrProjectSelection()
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
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(false));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .VerifyNoOtherCalls();
            Assert.Null(preferences.LastSelectedProjectId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenShellNavigationFails_DoesNotCommitVisibleSelectionOrProjectSelection()
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
            preferences.LastSelectedProjectId = "project-existing";
            var shellNavigation = CreateShellNavigationService();
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Setup(s => s.NavigateToChat(It.IsAny<long>()))
                .Throws(new InvalidOperationException("Navigation failed"));

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.Equal("project-existing", preferences.LastSelectedProjectId);
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
            var shellNavigation = CreateShellNavigationService();
            var ui = new Mock<IUiInteractionService>();
            Action<string>? pickSession = null;

            ui.Setup(s => s.ShowSessionsListDialogAsync(
                    "会话",
                    It.IsAny<IReadOnlyList<SessionNavItemViewModel>>(),
                    It.IsAny<Action<string>>()))
                .Callback<string, IReadOnlyList<SessionNavItemViewModel>, Action<string>>((_, _, onPickSession) => pickSession = onPickSession)
                .Returns(Task.CompletedTask);

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator, ui.Object);

            navVm.RebuildTree();

            await navVm.ShowAllSessionsForProjectAsync("project-1");

            Assert.NotNull(pickSession);

            pickSession!("session-2");
            await WaitForConditionAsync(() =>
                navVm.CurrentSelection is NavigationSelectionState.Session sessionSelection
                && activationCoordinator.ActivatedSessionIds.Contains("session-2")
                && string.Equals(sessionSelection.SessionId, "session-2", StringComparison.Ordinal));

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("project-1", preferences.LastSelectedProjectId);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_LatestTokenWinsSelectionCommit()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var shellNavigation = new TokenAwareShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation);

            var firstTask = coordinator.ActivateSessionAsync("session-1", "project-1");
            var secondTask = coordinator.ActivateSessionAsync("session-2", "project-2");

            shellNavigation.CompleteSecond(ShellNavigationResult.Success());
            shellNavigation.CompleteFirst(ShellNavigationResult.Success());

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.False(results[0]);
            Assert.True(results[1]);
            Assert.NotNull(shellNavigation.FirstToken);
            Assert.NotNull(shellNavigation.SecondToken);
            Assert.True(shellNavigation.FirstToken < shellNavigation.SecondToken);

            var selection = Assert.IsType<NavigationSelectionState.Session>(selectionStore.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("project-2", preferences.LastSelectedProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_SlowSessionSwitch_NavigatesToChatBeforeSwitchCompletes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var switchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowSwitchCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationCoordinator = new RecordingConversationSessionSwitcher(async (_, _) =>
            {
                switchStarted.TrySetResult(null);
                await allowSwitchCompletion.Task;
                return true;
            });
            var shellNavigation = CreateShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);

            var activationTask = coordinator.ActivateSessionAsync("session-1", "project-1");
            await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
            Assert.Equal(NavigationSelectionState.StartSelection, selectionStore.CurrentSelection);

            allowSwitchCompletion.TrySetResult(null);

            var activated = await activationTask;

            Assert.True(activated);
            var selection = Assert.IsType<NavigationSelectionState.Session>(selectionStore.CurrentSelection);
            Assert.Equal("session-1", selection.SessionId);
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
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Start);

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SyncSelectionFromShellContent_Chat_DoesNotOverrideExistingSelection()
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
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");
            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Chat);

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
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            coordinator.SyncSelectionFromShellContent(ShellNavigationContent.Settings);

            Assert.Equal(NavigationSelectionState.SettingsSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_RemoteBoundConversation_UsesChatViewModelActivationPath()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-1", @"C:\repo\one") { DisplayName = "Session 1" },
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            await chat.ViewModel.RestoreAsync();

            var bindResult = await chat.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("session-2", "remote-2", null);
            Assert.Equal(BindingUpdateStatus.Success, bindResult.Status);

            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>()))
                .ReturnsAsync(SessionLoadResponse.Completed);
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            var initialActivated = await chat.ViewModel.SwitchConversationAsync("session-1");
            Assert.True(initialActivated);
            var activated = await coordinator.ActivateSessionAsync("session-2", "project-1");

            Assert.True(activated);
            chatService.Verify(
                service => service.LoadSessionAsync(It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\two", StringComparison.Ordinal))),
                Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateSessionAsync_FromStart_RemoteConversation_ShowsLoadingUntilHistoryHydrates()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            await chat.ViewModel.RestoreAsync();

            var bindResult = await chat.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("session-2", "remote-2", null);
            Assert.Equal(BindingUpdateStatus.Success, bindResult.Status);

            var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>()))
                .Returns<SessionLoadParams>(async _ =>
                {
                    loadStarted.TrySetResult(null);
                    await allowLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(activationTask.IsCompletedSuccessfully);
            Assert.True(await activationTask);
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.True(chat.ViewModel.IsOverlayVisible);
            Assert.Equal("正在加载会话历史...", chat.ViewModel.OverlayStatusText);

            allowLoadCompletion.TrySetResult(null);
            await WaitForConditionAsync(() => !chat.ViewModel.IsOverlayVisible, maxAttempts: 100, delayMilliseconds: 20);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateSessionAsync_RemoteBoundConversation_CommitsSelectionBeforeRemoteHydrationCompletes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-1", @"C:\repo\one") { DisplayName = "Session 1" },
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            await chat.ViewModel.RestoreAsync();

            var bindResult = await chat.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("session-2", "remote-2", null);
            Assert.Equal(BindingUpdateStatus.Success, bindResult.Status);

            var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>()))
                .Returns<SessionLoadParams>(async _ =>
                {
                    loadStarted.TrySetResult(null);
                    await allowLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();
            var initialActivated = await chat.ViewModel.SwitchConversationAsync("session-1");
            Assert.True(initialActivated);

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(activationTask.IsCompletedSuccessfully);
            Assert.True(await activationTask);

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.True(chat.ViewModel.IsOverlayVisible);
            Assert.Equal("正在加载会话历史...", chat.ViewModel.OverlayStatusText);

            allowLoadCompletion.TrySetResult(null);
            await WaitForConditionAsync(() => !chat.ViewModel.IsOverlayVisible);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateSessionAsync_WhenRemoteHydrationFailsAfterSelectionCommit_SelectionStaysOnTarget()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            await chat.ViewModel.RestoreAsync();

            var bindResult = await chat.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("session-2", "remote-2", null);
            Assert.Equal(BindingUpdateStatus.Success, bindResult.Status);

            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>()))
                .ThrowsAsync(new InvalidOperationException("remote load failed"));
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");

            Assert.True(await activationTask);
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);

            await WaitForConditionAsync(() => !chat.ViewModel.IsOverlayVisible);
            Assert.Contains("remote load failed", chat.ViewModel.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateSessionAsync_WhenPreviousRemoteLoadIsStillRunning_LatestClickCommitsImmediately()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-1", @"C:\repo\one") { DisplayName = "Imported Session 1" },
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session 2" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            await chat.ViewModel.RestoreAsync();

            var bindFirst = await chat.ViewModel.ConversationBindingCommands.UpdateBindingAsync("session-1", "remote-1", null);
            var bindSecond = await chat.ViewModel.ConversationBindingCommands.UpdateBindingAsync("session-2", "remote-2", null);
            Assert.Equal(BindingUpdateStatus.Success, bindFirst.Status);
            Assert.Equal(BindingUpdateStatus.Success, bindSecond.Status);

            var firstLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowSecondLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>()))
                .Returns<SessionLoadParams>(async parameters =>
                {
                    if (string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal))
                    {
                        firstLoadStarted.TrySetResult(null);
                        await allowFirstLoadCompletion.Task;
                        return SessionLoadResponse.Completed;
                    }

                    if (string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal))
                    {
                        secondLoadStarted.TrySetResult(null);
                        await allowSecondLoadCompletion.Task;
                        return SessionLoadResponse.Completed;
                    }

                    throw new InvalidOperationException($"Unexpected session load: {parameters.SessionId}");
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();

            var firstActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await firstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await firstActivation);

            var secondActivation = coordinator.ActivateSessionAsync("session-2", "project-1");
            var completedBeforeFirstLoadFinished = await Task.WhenAny(secondActivation, Task.Delay(250)) == secondActivation;

            Assert.True(completedBeforeFirstLoadFinished);
            Assert.True(await secondActivation);
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.True(chat.ViewModel.IsOverlayVisible);

            allowFirstLoadCompletion.TrySetResult(null);
            await secondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            allowSecondLoadCompletion.TrySetResult(null);
            await WaitForConditionAsync(() => !chat.ViewModel.IsOverlayVisible);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        ChatViewModelHarness chat,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState,
        ShellSelectionStateStore? selectionStore = null,
        INavigationCoordinator? navigationCoordinator = null,
        IUiInteractionService? ui = null)
    {
        var shellNavigation = CreateShellNavigationService();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var projector = new NavigationSelectionProjector();
        selectionStore ??= new ShellSelectionStateStore();
        navigationCoordinator ??= new StubNavigationCoordinator();

        return new MainNavigationViewModel(
            chat.ViewModel,
            new NavigationProjectPreferencesAdapter(preferences),
            ui ?? Mock.Of<IUiInteractionService>(),
            shellNavigation.Object,
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            projector,
            selectionStore,
            chat.Presenter,
            new ProjectAffinityResolver());
    }

    private static NavigationCoordinator CreateCoordinator(
        IShellSelectionMutationSink selectionSink,
        IConversationSessionSwitcher activationCoordinator,
        AppPreferencesViewModel preferences,
        IShellNavigationService shellNavigationService)
    {
        return new NavigationCoordinator(
            selectionSink,
            activationCoordinator,
            new NavigationProjectSelectionStoreAdapter(preferences),
            shellNavigationService);
    }

    private static Mock<ISessionManager> CreateSessionManager(params Session[] sessions)
    {
        var sessionManager = new Mock<ISessionManager>();
        foreach (var session in sessions)
        {
            sessionManager.Setup(s => s.GetSession(session.SessionId)).Returns(session);
        }
        sessionManager.Setup(s => s.GetAllSessions()).Returns(sessions);

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
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStateProjector = new ChatStateProjector();

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ConversationDocument());

        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext);
        foreach (var session in sessionManager.GetAllSessions())
        {
            workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                session.SessionId,
                [],
                [],
                false,
                null,
                session.CreatedAt,
                session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt));
        }
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
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
                miniWindow.Object,
                workspace,
                conversationCatalogPresenter,
                chatStateProjector,
                null,
                connectionStore,
                vmLogger.Object,
                syncContext);
            return new ChatViewModelHarness(
                viewModel,
                state,
                connectionState,
                conversationCatalogPresenter,
                workspace,
                chatStore.Object,
                connectionStore);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class ChatViewModelHarness : IDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        public ConversationCatalogPresenter Presenter { get; }

        public ChatViewModelHarness(
            ChatViewModel viewModel,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            ConversationCatalogPresenter presenter,
            ChatConversationWorkspace workspace,
            IChatStore chatStore,
            IChatConnectionStore connectionStore)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            Presenter = presenter;
            Workspace = workspace;
            ChatStore = chatStore;
            ConnectionStore = connectionStore;
        }

        public ChatViewModel ViewModel { get; }

        public ChatConversationWorkspace Workspace { get; }

        public IChatStore ChatStore { get; }

        public IChatConnectionStore ConnectionStore { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            _connectionState.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingConversationSessionSwitcher : IConversationSessionSwitcher
    {
        private readonly Func<string, CancellationToken, Task<bool>> _onActivate;

        public RecordingConversationSessionSwitcher(Func<string, CancellationToken, Task<bool>> onActivate)
        {
            _onActivate = onActivate;
        }

        public List<string> ActivatedSessionIds { get; } = new();

        public Task<bool> SwitchConversationAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ActivatedSessionIds.Add(sessionId);
            return _onActivate(sessionId, cancellationToken);
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
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

    private static Mock<IShellNavigationService> CreateShellNavigationService()
    {
        var shellNavigation = new Mock<IShellNavigationService>();
        var tokenNavigation = shellNavigation.As<IActivationTokenShellNavigationService>();
        tokenNavigation
            .Setup(s => s.NavigateToChat(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenNavigation
            .Setup(s => s.NavigateToStart(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenNavigation
            .Setup(s => s.NavigateToSettings(It.IsAny<string>(), It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        shellNavigation
            .Setup(s => s.NavigateToChat())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        shellNavigation
            .Setup(s => s.NavigateToStart())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenNavigation
            .Setup(s => s.NavigateToDiscoverSessions(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        shellNavigation
            .Setup(s => s.NavigateToDiscoverSessions())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        shellNavigation
            .Setup(s => s.NavigateToSettings(It.IsAny<string>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        return shellNavigation;
    }

    private static async Task WaitForConditionAsync(
        Func<bool> predicate,
        int maxAttempts = 20,
        int delayMilliseconds = 10)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMilliseconds);
        }

        Assert.True(predicate());
    }

    private sealed class TokenAwareShellNavigationService : IShellNavigationService, IActivationTokenShellNavigationService
    {
        private readonly TaskCompletionSource<ShellNavigationResult> _firstCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ShellNavigationResult> _secondCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _chatCallCount;

        public long? FirstToken { get; private set; }
        public long? SecondToken { get; private set; }

        public ValueTask<ShellNavigationResult> NavigateToSettings(string key)
            => ValueTask.FromResult(ShellNavigationResult.Success());

        public ValueTask<ShellNavigationResult> NavigateToChat()
            => NavigateToChat(0);

        public ValueTask<ShellNavigationResult> NavigateToStart()
            => ValueTask.FromResult(ShellNavigationResult.Success());

        public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions()
            => ValueTask.FromResult(ShellNavigationResult.Success());

        public ValueTask<ShellNavigationResult> NavigateToSettings(string key, long activationToken)
            => NavigateToSettings(key);

        public ValueTask<ShellNavigationResult> NavigateToChat(long activationToken)
        {
            var callIndex = Interlocked.Increment(ref _chatCallCount);
            if (callIndex == 1)
            {
                FirstToken = activationToken;
                return new ValueTask<ShellNavigationResult>(_firstCompletion.Task);
            }

            if (callIndex == 2)
            {
                SecondToken = activationToken;
                return new ValueTask<ShellNavigationResult>(_secondCompletion.Task);
            }

            return ValueTask.FromResult(ShellNavigationResult.Success());
        }

        public ValueTask<ShellNavigationResult> NavigateToStart(long activationToken)
            => NavigateToStart();

        public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions(long activationToken)
            => NavigateToDiscoverSessions();

        public void CompleteFirst(ShellNavigationResult result)
            => _firstCompletion.TrySetResult(result);

        public void CompleteSecond(ShellNavigationResult result)
            => _secondCompletion.TrySetResult(result);
    }
}
