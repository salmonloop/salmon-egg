using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class NavigationCoordinatorTests
{
    [Fact]
    public async Task ShellNavigationService_NavigateToChat_UsesAsyncResultContract()
    {
        var shellNavigation = new Mock<IShellNavigationService>();
        shellNavigation
            .Setup(x => x.NavigateToChat())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));

        var result = await shellNavigation.Object.NavigateToChat();

        Assert.True(result.Succeeded);
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
            navState.SetPaneOpen(true);
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
    public async Task ActivateSessionAsync_WhenSwitcherFails_DoesNotProjectPreviewAfterActivationEnds()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(false));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .VerifyNoOtherCalls();
            Assert.Null(preferences.LastSelectedProjectId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", runtimeState.DesiredSessionId);
            Assert.False(runtimeState.IsSessionActivationInProgress);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenShellNavigationFails_DoesNotProjectPreviewAfterActivationEnds()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
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
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();
            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.Equal("project-existing", preferences.LastSelectedProjectId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", runtimeState.DesiredSessionId);
            Assert.False(runtimeState.IsSessionActivationInProgress);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateStartAsync_WhenShellNavigationThrows_LogsStructuredFailureAndReturnsFalse()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        var logger = new Mock<ILogger<NavigationCoordinator>>();
        var exception = new InvalidOperationException("Navigation failed");
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Setup(s => s.NavigateToStart(It.IsAny<long>()))
            .Throws(exception);

        var coordinator = CreateCoordinator(
            selectionStore,
            new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true)),
            preferences,
            shellNavigation.Object,
            logger: logger.Object);

        var activated = await coordinator.ActivateStartAsync();

        Assert.False(activated);
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v != null
                    && v.ToString()!.Contains("Navigation activation threw.", StringComparison.Ordinal)
                    && v.ToString()!.Contains("content=Start", StringComparison.Ordinal)
                    && v.ToString()!.Contains("reason=StartNavigationException", StringComparison.Ordinal)),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenRecoveryCapabilityIsMissing_DoesNotImportOrSwitch()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        var switcher = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
        var discoverFacade = new FakeDiscoverSessionsConnectionFacade
        {
            CurrentChatService = new FakeDiscoverChatService
            {
                AgentCapabilities = new AgentCapabilities(loadSession: false, sessionCapabilities: new SessionCapabilities())
            }
        };

        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation.Object,
            discoverConnectionFacade: discoverFacade);

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        Assert.False(result.Succeeded);
        Assert.Equal("当前 Agent 未声明 ACP loadSession 能力，无法导入已发现的远程会话。", result.ErrorMessage);
        Assert.Empty(switcher.ActivatedSessionIds);
        Assert.Empty(switcher.OpenRequests);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenOnlyResumeIsSupported_DoesNotImportOrSwitch()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        var switcher = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
        var discoverFacade = new FakeDiscoverSessionsConnectionFacade
        {
            CurrentChatService = new FakeDiscoverChatService
            {
                AgentCapabilities = new AgentCapabilities(
                    loadSession: false,
                    sessionCapabilities: new SessionCapabilities
                    {
                        Resume = new SessionResumeCapabilities()
                    })
            }
        };

        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation.Object,
            discoverConnectionFacade: discoverFacade);

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        Assert.False(result.Succeeded);
        Assert.Equal("当前 Agent 未声明 ACP loadSession 能力，无法导入已发现的远程会话。", result.ErrorMessage);
        Assert.Empty(switcher.ActivatedSessionIds);
        Assert.Empty(switcher.OpenRequests);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenOpenSucceeds_RunsThroughNavigationOwner()
    {
        var selectionStore = new ShellSelectionStateStore();
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        var expectedRequest = new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote");
        var switcher = new RecordingConversationSessionSwitcher(
            (_, _) => Task.FromResult(true),
            (request, _) => Task.FromResult(new DiscoverRemoteSessionOpenResult(true, "local-1", null)));
        var discoverFacade = new Mock<IDiscoverSessionsConnectionFacade>();
        discoverFacade.SetupGet(x => x.CurrentChatService).Returns(new FakeDiscoverChatService());

        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation.Object,
            runtimeState,
            discoverConnectionFacade: discoverFacade.Object);

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            expectedRequest);

        Assert.True(result.Succeeded);
        Assert.Equal("local-1", result.LocalConversationId);
        Assert.Equal(new[] { "local-1" }, switcher.ActivatedSessionIds);
        Assert.Equal(new[] { expectedRequest }, switcher.OpenRequests);
        Assert.Equal(new NavigationSelectionState.Session("local-1"), selectionStore.CurrentSelection);
        Assert.Equal(ShellNavigationContent.Chat, runtimeState.CurrentShellContent);
        Assert.Equal("local-1", runtimeState.CommittedSessionId);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenSupersededAfterOpen_DisposesStaleImport()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = new TokenAwareShellNavigationService();
        var openCompletion = new TaskCompletionSource<DiscoverRemoteSessionOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var switcher = new RecordingConversationSessionSwitcher(
            (_, _) => Task.FromResult(true),
            (_, _) => openCompletion.Task);
        var discoverFacade = new Mock<IDiscoverSessionsConnectionFacade>();
        discoverFacade.SetupGet(x => x.CurrentChatService).Returns(new FakeDiscoverChatService());

        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation,
            discoverConnectionFacade: discoverFacade.Object);

        var staleOpenTask = coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        await WaitForConditionAsync(() => switcher.OpenRequests.Count == 1);

        await coordinator.ActivateStartAsync();
        openCompletion.TrySetResult(new DiscoverRemoteSessionOpenResult(true, "local-1", null));
        var result = await staleOpenTask;

        Assert.False(result.Succeeded);
        Assert.Equal("DiscoverSessionOpenSuperseded", result.ErrorMessage);
        Assert.Equal(new[] { "local-1" }, switcher.DiscardedDiscoveredSessionIds);
        Assert.Empty(switcher.ActivatedSessionIds);
        Assert.Equal(NavigationSelectionState.StartSelection, selectionStore.CurrentSelection);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenOpenFails_AllowsNextNavigationIntent()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = new TokenAwareShellNavigationService();
        var switcher = new RecordingConversationSessionSwitcher(
            (_, _) => Task.FromResult(true),
            (_, _) => Task.FromException<DiscoverRemoteSessionOpenResult>(
                new InvalidOperationException("open failed")));
        var discoverFacade = new Mock<IDiscoverSessionsConnectionFacade>();
        discoverFacade.SetupGet(x => x.CurrentChatService).Returns(new FakeDiscoverChatService());
        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation,
            discoverConnectionFacade: discoverFacade.Object);

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        Assert.False(result.Succeeded);
        Assert.Equal("open failed", result.ErrorMessage);
        Assert.True(await coordinator.ActivateStartAsync());
        Assert.Equal(NavigationSelectionState.StartSelection, selectionStore.CurrentSelection);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenOpenFailsAfterCancelingSessionActivation_ClearsStaleActivationState()
    {
        var selectionStore = new ShellSelectionStateStore();
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        var switchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var switcher = new RecordingConversationSessionSwitcher(
            async (_, cancellationToken) =>
            {
                switchStarted.TrySetResult(null);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            },
            (_, _) => Task.FromException<DiscoverRemoteSessionOpenResult>(
                new InvalidOperationException("open failed")));
        var discoverFacade = new Mock<IDiscoverSessionsConnectionFacade>();
        discoverFacade.SetupGet(x => x.CurrentChatService).Returns(new FakeDiscoverChatService());
        var coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation.Object,
            runtimeState,
            discoverConnectionFacade: discoverFacade.Object);

        var sessionActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
        await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        Assert.False(result.Succeeded);
        Assert.Equal("open failed", result.ErrorMessage);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Null(runtimeState.ActiveSessionActivation);
        Assert.Null(runtimeState.DesiredSessionId);
        Assert.False(await sessionActivation);
    }

    [Fact]
    public async Task ActivateDiscoveredRemoteSessionAsync_WhenActivationFallsOutOfLatestIntent_RollsBackImportedConversation()
    {
        var selectionStore = new ShellSelectionStateStore();
        var preferences = CreatePreferencesWithProject();
        var shellNavigation = CreateShellNavigationService();
        NavigationCoordinator? coordinator = null;
        var switcher = new RecordingConversationSessionSwitcher(
            async (_, _) =>
            {
                Assert.NotNull(coordinator);
                await coordinator!.ActivateStartAsync();
                return true;
            },
            (_, _) => Task.FromResult(new DiscoverRemoteSessionOpenResult(true, "local-1", null)));
        var discoverFacade = new Mock<IDiscoverSessionsConnectionFacade>();
        discoverFacade.SetupGet(x => x.CurrentChatService).Returns(new FakeDiscoverChatService());

        coordinator = CreateCoordinator(
            selectionStore,
            switcher,
            preferences,
            shellNavigation.Object,
            discoverConnectionFacade: discoverFacade.Object);

        var result = await coordinator.ActivateDiscoveredRemoteSessionAsync(
            new DiscoverRemoteSessionOpenRequest("remote-1", "/repo", "profile-1", "Remote"));

        Assert.False(result.Succeeded);
        Assert.Equal("加载会话并导入失败，请检查连接状态。", result.ErrorMessage);
        Assert.Equal(new[] { "local-1" }, switcher.ActivatedSessionIds);
        Assert.Equal(new[] { "local-1" }, switcher.DiscardedDiscoveredSessionIds);
        Assert.Equal(NavigationSelectionState.StartSelection, selectionStore.CurrentSelection);
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
                    It.IsAny<string>(),
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
    public async Task ActivateSessionAsync_SameSessionInFlight_IgnoresDuplicateIntentWithoutRestartingActivation()
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
            var activationCoordinator = new RecordingConversationSessionSwitcher(async (_, cancellationToken) =>
            {
                switchStarted.TrySetResult(null);
                await allowSwitchCompletion.Task.WaitAsync(cancellationToken);
                return true;
            });
            var shellNavigation = CreateShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);

            var firstActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var secondActivation = coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.True(secondActivation.IsCompletedSuccessfully);
            Assert.True(await secondActivation);

            allowSwitchCompletion.TrySetResult(null);
            Assert.True(await firstActivation);
            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_SameSessionBeforeChatShell_DoesNotRestartActivation()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var shellNavigation = new BlockingChatShellNavigationService();
            var switchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) =>
            {
                switchStarted.TrySetResult(null);
                return Task.FromResult(true);
            });
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation);

            var firstActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await shellNavigation.FirstChatNavigationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var secondActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            Assert.True(secondActivation.IsCompletedSuccessfully);
            Assert.True(await secondActivation);

            shellNavigation.CompleteFirstChatNavigation(ShellNavigationResult.Success());

            Assert.True(await firstActivation);
            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
            Assert.Equal(1, shellNavigation.ChatNavigationCount);
            await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenAlreadyOnChatShellAndTargetDiffers_UsesRuntimeIntentWithoutPreviewSideChannel()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var shellNavigation = CreateShellNavigationService();
            var runtimeState = new ShellNavigationRuntimeStateStore
            {
                CurrentShellContent = ShellNavigationContent.Chat,
                CommittedSessionId = "session-0"
            };
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);

            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
            Assert.Equal("session-1", runtimeState.CommittedSessionId);
            Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
            Assert.Equal("session-1", runtimeState.ActiveSessionActivation?.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenHydratedTerminalStateExists_SameSessionStartsFreshActivation()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var shellNavigation = CreateShellNavigationService();
            var runtimeState = new ShellNavigationRuntimeStateStore
            {
                CurrentShellContent = ShellNavigationContent.Chat,
                LatestActivationToken = 7,
                ActiveSessionActivationVersion = 0,
                IsSessionActivationInProgress = false,
                ActiveSessionActivation = new SessionActivationSnapshot(
                    "session-1",
                    "project-1",
                    7,
                    SessionActivationPhase.Hydrated,
                    "Hydrated")
            };
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);

            var activated = await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.True(activated);
            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
            Assert.True(runtimeState.LatestActivationToken > 7);
            Assert.Equal("session-1", runtimeState.ActiveSessionActivation?.SessionId);
            Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenAlreadyOnCommittedSession_DoesNotUsePreviewSideChannel()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var shellNavigation = CreateShellNavigationService();
            var runtimeState = new ShellNavigationRuntimeStateStore
            {
                CurrentShellContent = ShellNavigationContent.Chat,
                CommittedSessionId = "session-1"
            };
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);

            await coordinator.ActivateSessionAsync("session-1", "project-1");

            Assert.Equal(new[] { "session-1" }, activationCoordinator.ActivatedSessionIds);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_SameCommittedSessionLatestIntent_IgnoresDuplicateWithoutRenavigation()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var shellNavigation = CreateShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);

            Assert.True(await coordinator.ActivateSessionAsync("session-1", "project-1"));
            Assert.True(await coordinator.ActivateSessionAsync("session-1", "project-1"));

            Assert.Equal(1, activationCoordinator.ActivatedSessionIds.Count(id => string.Equals(id, "session-1", StringComparison.Ordinal)));
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_SameSessionAfterStartNavigation_DoesNotTreatCommittedSelectionAsDuplicate()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var preferences = CreatePreferencesWithProject();
            var selectionStore = new ShellSelectionStateStore();
            var activationCoordinator = new RecordingConversationSessionSwitcher((_, _) => Task.FromResult(true));
            var shellNavigation = CreateShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);

            Assert.True(await coordinator.ActivateSessionAsync("session-1", "project-1"));
            await coordinator.ActivateStartAsync();
            Assert.True(await coordinator.ActivateSessionAsync("session-1", "project-1"));

            Assert.Equal(2, activationCoordinator.ActivatedSessionIds.Count(id => string.Equals(id, "session-1", StringComparison.Ordinal)));
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToStart(It.IsAny<long>()), Times.Once);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Exactly(2));
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
    public async Task ActivateStartAsync_SupersedesInFlightSessionActivation_AndPreventsLateSessionCommit()
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
            var activationCoordinator = new RecordingConversationSessionSwitcher(async (_, cancellationToken) =>
            {
                switchStarted.TrySetResult(null);
                await allowSwitchCompletion.Task.WaitAsync(cancellationToken);
                return true;
            });
            var shellNavigation = CreateShellNavigationService();
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object);

            var sessionActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await coordinator.ActivateStartAsync();
            allowSwitchCompletion.TrySetResult(null);
            var activated = await sessionActivation;

            Assert.False(activated);
            Assert.Equal(NavigationSelectionState.StartSelection, selectionStore.CurrentSelection);
            Assert.Null(preferences.LastSelectedProjectId);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToStart(It.IsAny<long>()), Times.Once);
            shellNavigation.As<IActivationTokenShellNavigationService>()
                .Verify(s => s.NavigateToChat(It.IsAny<long>()), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateStartAsync_ClearsInFlightSessionPreviewProjection_WhenRuntimeStateIsShared()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object);
            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var switchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowSwitchCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationCoordinator = new RecordingConversationSessionSwitcher(async (_, cancellationToken) =>
            {
                switchStarted.TrySetResult(null);
                await allowSwitchCompletion.Task.WaitAsync(cancellationToken);
                return true;
            });
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();

            var sessionActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.True(runtimeState.IsSessionActivationInProgress);
            Assert.Equal("session-1", runtimeState.DesiredSessionId);

            await coordinator.ActivateStartAsync();

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.False(runtimeState.IsSessionActivationInProgress);
            Assert.Null(runtimeState.DesiredSessionId);

            allowSwitchCompletion.TrySetResult(null);
            Assert.False(await sessionActivation);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_FromStart_RemoteConversation_PrimesLoadingOverlayBeforeChatNavigationCompletes()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var sessionManager = CreateSessionManager(
                new Session("session-2", @"C:\repo\two") { DisplayName = "Imported Session" });
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = new TokenAwareShellNavigationService();
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));

            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SessionLoadResponse.Completed);
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation, runtimeState);

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");

            await WaitForConditionAsync(() => chat.ViewModel.ShouldShowBlockingLoadingMask, maxAttempts: 20, delayMilliseconds: 10);

            Assert.False(activationTask.IsCompleted);
            Assert.True(chat.ViewModel.IsOverlayVisible);
            Assert.True(chat.ViewModel.ShouldShowBlockingLoadingMask);
            Assert.Contains("切换", chat.ViewModel.OverlayStatusText, StringComparison.Ordinal);

            shellNavigation.CompleteFirst(ShellNavigationResult.Success());

            Assert.True(await activationTask);
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
    public async Task ActivateSessionAsync_RemoteBoundConversation_ContinuesBackgroundHydrationWithBoundRemoteSession()
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
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));

            var loadStarted = new TaskCompletionSource<SessionLoadParams>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .Returns<SessionLoadParams, CancellationToken>(async (parameters, _) =>
                {
                    loadStarted.TrySetResult(parameters);
                    await allowLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(
                selectionStore,
                (IConversationSessionSwitcher)chat.ViewModel,
                preferences,
                shellNavigation.Object,
                runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();
            var initialActivated = await chat.ViewModel.SwitchConversationAsync("session-1");
            Assert.True(initialActivated);
            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");

            _ = await activationTask;
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("project-1", preferences.LastSelectedProjectId);
            Assert.NotNull(runtimeState.ActiveSessionActivation);
            Assert.Equal("session-2", runtimeState.ActiveSessionActivation!.SessionId);

            allowLoadCompletion.TrySetResult(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateStartAsync_AfterCommittedBackgroundRemoteActivation_KeepsLatestNavigationIntent()
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
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            await SetConnectedProfileAsync(chat, "profile-1", "conn-1");

            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));

            var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
                {
                    loadStarted.TrySetResult(null);
                    await allowLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(
                selectionStore,
                (IConversationSessionSwitcher)chat.ViewModel,
                preferences,
                shellNavigation.Object,
                runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();
            Assert.True(await coordinator.ActivateSessionAsync("session-2", "project-1"));
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await coordinator.ActivateStartAsync();

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.Equal(ShellNavigationContent.Start, runtimeState.CurrentShellContent);

            allowLoadCompletion.TrySetResult(null);
            await WaitForConditionAsync(() => !runtimeState.IsSessionActivationInProgress);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.Equal(ShellNavigationContent.Start, runtimeState.CurrentShellContent);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateSessionAsync_RemoteBoundConversation_ProjectsShellActivationThroughHydrationLifecycle()
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
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));

            var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
                {
                    loadStarted.TrySetResult(null);
                    await allowLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(
                selectionStore,
                (IConversationSessionSwitcher)chat.ViewModel,
                preferences,
                shellNavigation.Object,
                runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");

            await WaitForConditionAsync(() =>
                loadStarted.Task.IsCompleted || activationTask.IsCompleted,
                maxAttempts: 100,
                delayMilliseconds: 20);
            Assert.NotNull(runtimeState.ActiveSessionActivation);
            Assert.Equal("session-2", runtimeState.ActiveSessionActivation!.SessionId);

            allowLoadCompletion.TrySetResult(null);
            Assert.True(await activationTask);
            await WaitForConditionAsync(() =>
                !runtimeState.IsSessionActivationInProgress,
                maxAttempts: 100,
                delayMilliseconds: 20);
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
            chat.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                "session-2",
                [],
                [],
                false,
                DateTime.UtcNow,
                DateTime.UtcNow));
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));

            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("remote load failed"));
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(selectionStore, (IConversationSessionSwitcher)chat.ViewModel, preferences, shellNavigation.Object);
            using var navVm = CreateNavigationViewModel(chat, sessionManager.Object, preferences, navState, selectionStore, coordinator);

            navVm.RebuildTree();

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");

            await activationTask;
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);

            await WaitForConditionAsync(() => !chat.ViewModel.IsOverlayVisible);
            Assert.False(string.IsNullOrWhiteSpace(chat.ViewModel.ErrorMessage));
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
    public async Task ActivateSessionAsync_WhenRemoteHydrationFailsAfterSelectionCommit_SameSessionRetryStartsNewActivation()
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
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));
            await SetConnectedProfileAsync(chat, "profile-1", "conn-1");

            var loadAttempts = 0;
            var secondLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .Returns<SessionLoadParams, CancellationToken>((_, _) =>
                {
                    var attempt = Interlocked.Increment(ref loadAttempts);
                    if (attempt == 2)
                    {
                        secondLoadStarted.TrySetResult(null);
                    }

                    return attempt == 1
                        ? Task.FromException<SessionLoadResponse>(new InvalidOperationException("remote load failed"))
                        : Task.FromResult(SessionLoadResponse.Completed);
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(
                selectionStore,
                (IConversationSessionSwitcher)chat.ViewModel,
                preferences,
                shellNavigation.Object,
                runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();

            var firstActivation = coordinator.ActivateSessionAsync("session-2", "project-1");

            await firstActivation;
            await WaitForConditionAsync(() =>
                !runtimeState.IsSessionActivationInProgress,
                maxAttempts: 100,
                delayMilliseconds: 20);

            var retryActivation = coordinator.ActivateSessionAsync("session-2", "project-1");

            await retryActivation;
            await WaitForConditionAsync(() =>
                Volatile.Read(ref loadAttempts) == 2,
                maxAttempts: 500,
                delayMilliseconds: 20);

            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("session-2", runtimeState.ActiveSessionActivation?.SessionId);
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
            var preferences = CreatePreferencesWithProject();
            var shellNavigation = CreateShellNavigationService();
            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var firstSwitchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstSwitchCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSwitchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationCoordinator = new RecordingConversationSessionSwitcher(async (sessionId, cancellationToken) =>
            {
                if (string.Equals(sessionId, "session-1", StringComparison.Ordinal))
                {
                    firstSwitchStarted.TrySetResult(null);
                    await allowFirstSwitchCompletion.Task.WaitAsync(cancellationToken);
                    return true;
                }

                if (string.Equals(sessionId, "session-2", StringComparison.Ordinal))
                {
                    secondSwitchStarted.TrySetResult(null);
                    return true;
                }

                throw new InvalidOperationException($"Unexpected session selection: {sessionId}");
            });
            var coordinator = CreateCoordinator(selectionStore, activationCoordinator, preferences, shellNavigation.Object, runtimeState);

            var firstActivation = coordinator.ActivateSessionAsync("session-1", "project-1");
            await firstSwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var secondActivation = coordinator.ActivateSessionAsync("session-2", "project-1");
            var completedBeforeFirstLoadFinished = await Task.WhenAny(secondActivation, Task.Delay(250)) == secondActivation;

            Assert.True(completedBeforeFirstLoadFinished);
            Assert.True(await secondActivation);
            await secondSwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var selection = Assert.IsType<NavigationSelectionState.Session>(selectionStore.CurrentSelection);
            Assert.Equal("session-2", selection.SessionId);
            Assert.Equal("session-2", runtimeState.CommittedSessionId);
            Assert.Equal("session-2", runtimeState.ActiveSessionActivation?.SessionId);
            Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
            Assert.Equal(new[] { "session-1", "session-2" }, activationCoordinator.ActivatedSessionIds);

            allowFirstSwitchCompletion.TrySetResult(null);
            Assert.False(await firstActivation);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateStartAsync_WhileRemoteHydrationLaterFails_DoesNotSurfaceLateRemoteError()
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
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var chat = CreateChatViewModel(syncContext, preferences, sessionManager.Object, runtimeState);
            await chat.ViewModel.RestoreAsync();
            chat.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

            chat.Workspace.UpdateRemoteBinding("session-2", "remote-2", "profile-1");
            await chat.ChatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice("session-2", "remote-2", "profile-1")));
            await SetConnectedProfileAsync(chat, "profile-1", "conn-1");

            var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loadCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chatService = new Mock<IChatService>();
            chatService.SetupGet(service => service.IsConnected).Returns(true);
            chatService.SetupGet(service => service.IsInitialized).Returns(true);
            chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
                .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
                {
                    loadStarted.TrySetResult(null);
                    await allowLoadCompletion.Task;
                    loadCompleted.TrySetResult(null);
                    throw new InvalidOperationException("remote load failed");
                });
            chat.ViewModel.ReplaceChatService(chatService.Object);

            var selectionStore = new ShellSelectionStateStore();
            var coordinator = CreateCoordinator(
                selectionStore,
                (IConversationSessionSwitcher)chat.ViewModel,
                preferences,
                shellNavigation.Object,
                runtimeState);
            using var navVm = CreateNavigationViewModel(
                chat,
                sessionManager.Object,
                preferences,
                navState,
                selectionStore,
                coordinator,
                runtimeState: runtimeState);

            navVm.RebuildTree();

            var activationTask = coordinator.ActivateSessionAsync("session-2", "project-1");
            Assert.True(await activationTask);
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(await coordinator.ActivateStartAsync());
            var selection = Assert.IsType<NavigationSelectionState.Start>(navVm.CurrentSelection);
            Assert.Equal(NavigationSelectionState.StartSelection, selection);

            allowLoadCompletion.TrySetResult(null);
            await loadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForConditionAsync(() =>
                runtimeState.CurrentShellContent == ShellNavigationContent.Start
                && string.IsNullOrWhiteSpace(chat.ViewModel.ErrorMessage)
                && runtimeState.ActiveSessionActivation is null,
                maxAttempts: 300,
                delayMilliseconds: 20);
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
        IUiInteractionService? ui = null,
        IShellNavigationRuntimeState? runtimeState = null)
    {
        var shellNavigation = CreateShellNavigationService();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var projector = new NavigationSelectionProjector();
        selectionStore ??= new ShellSelectionStateStore();
        runtimeState ??= new ShellNavigationRuntimeStateStore();
        navigationCoordinator ??= new StubNavigationCoordinator();
        var uiDispatcher = SynchronizationContext.Current as IUiDispatcher ?? new ImmediateUiDispatcher();

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
            ui ?? Mock.Of<IUiInteractionService>(),
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            projector,
            selectionStore,
            runtimeState,
            chat.Presenter,
            new ProjectAffinityResolver(),
            uiDispatcher,
            Mock.Of<IStringLocalizer<CoreStrings>>());
    }

    private static NavigationCoordinator CreateCoordinator(
        IShellSelectionMutationSink selectionSink,
        IConversationSessionSwitcher activationCoordinator,
        AppPreferencesViewModel preferences,
        IShellNavigationService shellNavigationService,
        IShellNavigationRuntimeState? runtimeState = null,
        ILogger<NavigationCoordinator>? logger = null,
        IDiscoverSessionsConnectionFacade? discoverConnectionFacade = null)
    {
        return new NavigationCoordinator(
            selectionSink,
            runtimeState ?? new ShellNavigationRuntimeStateStore(),
            activationCoordinator,
            discoverConnectionFacade ?? new FakeDiscoverSessionsConnectionFacade(),
            new NavigationProjectSelectionStoreAdapter(preferences),
            shellNavigationService,
            logger);
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

    private static async Task SetConnectedProfileAsync(
        ChatViewModelHarness chat,
        string profileId,
        string connectionInstanceId)
    {
        await chat.ConnectionStore.Dispatch(new SetForegroundTransportProfileAction(profileId));
        await chat.ConnectionStore.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await chat.ConnectionStore.Dispatch(new SetConnectionInstanceIdAction(connectionInstanceId));
    }

    private static ServerConfiguration CreateConnectableStdioProfile(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

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

    private sealed class ImmediateSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public void Enqueue(Action action) => action();

        public Task EnqueueAsync(Action action)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public async Task EnqueueAsync(Func<Task> function)
        {
            await function().ConfigureAwait(false);
        }
    }

    private static ChatViewModelHarness CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager,
        IShellNavigationRuntimeState? runtimeState = null)
    {
        var uiDispatcher = syncContext as IUiDispatcher ?? new ImmediateUiDispatcher();
        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync())
            .Returns(async () => await state ?? ChatState.Empty);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

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
            uiDispatcher);
        foreach (var session in sessionManager.GetAllSessions())
        {
            workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                session.SessionId,
                [],
                [],
                false,
                session.CreatedAt,
                session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt));
        }
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
            var viewModel = new ChatViewModel(
                chatStore.Object,
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
                uiDispatcher,
                Mock.Of<IConversationPreviewStore>(),
                vmLogger.Object,
                new StaticMcpResolver([]),
                shellNavigationRuntimeState: runtimeState,
                conversationCatalogFacade: conversationCatalogFacade,
                acpConnectionCommands: Mock.Of<IAcpConnectionCommands>());
            conversationCatalogFacade.SetPanelCleanup(viewModel);
            return new ChatViewModelHarness(
                viewModel,
                profiles,
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
        public ConversationCatalogPresenter Presenter { get; }

        public ChatViewModelHarness(
            ChatViewModel viewModel,
            AcpProfilesViewModel profiles,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            ConversationCatalogPresenter presenter,
            ChatConversationWorkspace workspace,
            IChatStore chatStore,
            IChatConnectionStore connectionStore)
        {
            ViewModel = viewModel;
            Profiles = profiles;
            _state = state;
            _connectionState = connectionState;
            Presenter = presenter;
            Workspace = workspace;
            ChatStore = chatStore;
            ConnectionStore = connectionStore;
        }

        public ChatViewModel ViewModel { get; }

        public AcpProfilesViewModel Profiles { get; }

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
        private readonly Func<DiscoverRemoteSessionOpenRequest, CancellationToken, Task<DiscoverRemoteSessionOpenResult>> _onOpen;

        public RecordingConversationSessionSwitcher(
            Func<string, CancellationToken, Task<bool>> onActivate,
            Func<DiscoverRemoteSessionOpenRequest, CancellationToken, Task<DiscoverRemoteSessionOpenResult>>? onOpen = null)
        {
            _onActivate = onActivate;
            _onOpen = onOpen ?? ((_, _) => Task.FromResult(new DiscoverRemoteSessionOpenResult(false, null, "OpenNotConfigured")));
        }

        public List<string> ActivatedSessionIds { get; } = new();
        public List<DiscoverRemoteSessionOpenRequest> OpenRequests { get; } = new();
        public List<string> DiscardedDiscoveredSessionIds { get; } = new();

        public Task<bool> SwitchConversationAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ActivatedSessionIds.Add(sessionId);
            return _onActivate(sessionId, cancellationToken);
        }

        public Task<DiscoverRemoteSessionOpenResult> OpenDiscoveredRemoteSessionAsync(
            DiscoverRemoteSessionOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return _onOpen(request, cancellationToken);
        }

        public Task DiscardDiscoveredRemoteSessionAsync(
            string localConversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscardedDiscoveredSessionIds.Add(localConversationId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public Task<DiscoverRemoteSessionOpenResult> ActivateDiscoveredRemoteSessionAsync(
            DiscoverRemoteSessionOpenRequest request)
            => Task.FromResult(new DiscoverRemoteSessionOpenResult(false, null, null));

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }

    }

    private sealed class FakeDiscoverSessionsConnectionFacade : IDiscoverSessionsConnectionFacade
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public bool IsConnecting => false;
        public bool IsInitializing => false;
        public bool IsConnected => true;
        public string? ConnectionErrorMessage => null;
        public IChatService? CurrentChatService { get; set; } = new FakeDiscoverChatService();
        public Task ConnectToProfileAsync(ServerConfiguration profile) => Task.CompletedTask;
    }

    private sealed class FakeDiscoverChatService : IChatService
    {
        public string? CurrentSessionId => null;
        public bool IsInitialized => true;
        public bool IsConnected => true;
        public AgentInfo? AgentInfo => null;
        public AgentCapabilities? AgentCapabilities { get; set; } = new(loadSession: true, sessionCapabilities: new SessionCapabilities { List = new SessionListCapabilities() });
        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();
        public Plan? CurrentPlan => null;
        public SessionModeState? CurrentMode => null;
        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived { add { } remove { } }
        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived { add { } remove { } }
        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived { add { } remove { } }
        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived { add { } remove { } }
        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived { add { } remove { } }
        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived { add { } remove { } }
        public event EventHandler<string>? ErrorOccurred { add { } remove { } }
        public Task<InitializeResponse> InitializeAsync(InitializeParams @params) => throw new NotSupportedException();
        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params) => throw new NotSupportedException();
        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params) => throw new NotSupportedException();
        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params) => throw new NotSupportedException();
        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params) => throw new NotSupportedException();
        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params) => throw new NotSupportedException();
        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params) => throw new NotSupportedException();
        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null) => throw new NotSupportedException();
        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null) => throw new NotSupportedException();
        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers) => throw new NotSupportedException();
        public Task<bool> DisconnectAsync() => throw new NotSupportedException();
        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync() => throw new NotSupportedException();
        public void ClearHistory() { }
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
            prefsLogger.Object,
            new ImmediateUiDispatcher());

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
        int maxAttempts = 3000,
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

    private static bool IsUserFriendlyLoadingStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return (status.StartsWith("正在", StringComparison.Ordinal) || status.StartsWith("即将", StringComparison.Ordinal))
            && (status.Contains("聊天", StringComparison.Ordinal) || status.Contains("消息", StringComparison.Ordinal))
            && !status.Contains("ACP", StringComparison.OrdinalIgnoreCase)
            && !status.Contains("协议", StringComparison.Ordinal);
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

    private sealed class BlockingChatShellNavigationService : IShellNavigationService, IActivationTokenShellNavigationService
    {
        private readonly TaskCompletionSource<object?> _firstChatNavigationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ShellNavigationResult> _firstChatNavigationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _chatNavigationCount;

        public TaskCompletionSource<object?> FirstChatNavigationStarted => _firstChatNavigationStarted;

        public int ChatNavigationCount => _chatNavigationCount;

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
            var callCount = Interlocked.Increment(ref _chatNavigationCount);
            if (callCount == 1)
            {
                _firstChatNavigationStarted.TrySetResult(null);
                return new ValueTask<ShellNavigationResult>(_firstChatNavigationCompletion.Task);
            }

            return ValueTask.FromResult(ShellNavigationResult.Success());
        }

        public ValueTask<ShellNavigationResult> NavigateToStart(long activationToken)
            => NavigateToStart();

        public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions(long activationToken)
            => NavigateToDiscoverSessions();

        public void CompleteFirstChatNavigation(ShellNavigationResult result)
            => _firstChatNavigationCompletion.TrySetResult(result);
    }
}
