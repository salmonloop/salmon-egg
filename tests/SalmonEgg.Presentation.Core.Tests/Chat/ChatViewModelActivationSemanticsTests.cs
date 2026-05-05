using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task SwitchConversationAsync_WhenSwitchingFromLocalConversationToWarmRemoteConversation_SkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var remoteLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref remoteLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            acpConnectionCommands: commands.Object);

        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local cached transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "remote-1",
                    Timestamp = new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "remote cached transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-remote",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-remote", new ConversationRuntimeSlice(
                    ConversationId: "conv-remote",
                    Phase: ConversationRuntimePhase.Warm,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-1",
                    ProfileId: "profile-1",
                    Reason: "SessionLoadCompleted",
                    UpdatedAtUtc: new DateTime(2026, 5, 2, 0, 0, 2, DateTimeKind.Utc)))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        var switchedLocal = await fixture.ViewModel.SwitchConversationAsync("conv-local");

        Assert.True(switchedLocal);
        var localState = await fixture.GetStateAsync();
        var localRuntime = localState.ResolveRuntimeState("conv-local");
        Assert.NotNull(localRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, localRuntime!.Value.Phase);
        Assert.Equal("LocalConversationReady", localRuntime.Value.Reason);

        var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote");

        Assert.True(switchedRemote);
        Assert.Equal(0, Volatile.Read(ref remoteLoadCount));
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenRemoteHydratingTargetBecomesWarmDuringSelection_SkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        ViewModelFixture? fixture = null;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((sessionId, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, _, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));

        var remoteLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref remoteLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        await using (fixture)
        {
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-local",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                        IsOutgoing = false,
                        ContentType = "text",
                        TextContent = "remote cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                ConnectionInstanceId: "conn-1"),
                ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1")),
                ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                    .Add("conv-remote", new ConversationContentSlice(
                        ImmutableList.Create(
                            new ConversationMessageSnapshot
                            {
                                Id = "remote-1",
                                Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                                IsOutgoing = false,
                                ContentType = "text",
                                TextContent = "remote cached transcript"
                            }),
                        ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        false,
                        null)),
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                    .Add("conv-remote", new ConversationRuntimeSlice(
                        ConversationId: "conv-remote",
                        Phase: ConversationRuntimePhase.RemoteHydrating,
                        ConnectionInstanceId: "conn-1",
                        RemoteSessionId: "remote-1",
                        ProfileId: "profile-1",
                        Reason: "RemoteHydrationPending",
                        UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote");

            Assert.True(switchedRemote);
            Assert.Equal(0, Volatile.Read(ref remoteLoadCount));
            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-remote");
            Assert.NotNull(finalRuntime);
            Assert.Equal(ConversationRuntimePhase.Warm, finalRuntime!.Value.Phase);
            Assert.Equal("WarmReuse", finalRuntime.Value.Reason);
        }

        async Task<ConversationActivationResult> MarkTargetWarmDuringSelectionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(sessionId, "conv-remote", StringComparison.Ordinal))
            {
                await fixture!.UpdateStateAsync(state => state with
                {
                    RuntimeStates = (state.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty).SetItem(
                        "conv-remote",
                        new ConversationRuntimeSlice(
                            ConversationId: "conv-remote",
                            Phase: ConversationRuntimePhase.Warm,
                            ConnectionInstanceId: "conn-1",
                            RemoteSessionId: "remote-1",
                            ProfileId: "profile-1",
                            Reason: "SessionLoadCompleted",
                            UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
                });
            }

            return new ConversationActivationResult(true, sessionId, null);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenCompetingActivationExistsAndTargetBecomesWarmAfterSelection_SkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-competing", @"C:\repo\competing");
        await sessionManager.Object.CreateSessionAsync("conv-target", @"C:\repo\target");

        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((sessionId, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new ConversationActivationResult(true, sessionId, null));
            });
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new ConversationActivationResult(true, sessionId, null));
            });

        var targetLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-target", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref targetLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var appliedWarmAfterSelected = false;
        fixture.ChatStore.AfterDispatch = async action =>
        {
            if (appliedWarmAfterSelected
                || action is not SetConversationRuntimeStateAction
                {
                    RuntimeState:
                    {
                        ConversationId: "conv-target",
                        Phase: ConversationRuntimePhase.Selected
                    }
                })
            {
                return;
            }

            appliedWarmAfterSelected = true;
            await fixture.UpdateStateAsync(state => state with
            {
                RuntimeStates = (state.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty).SetItem(
                    "conv-target",
                    new ConversationRuntimeSlice(
                        ConversationId: "conv-target",
                        Phase: ConversationRuntimePhase.Warm,
                        ConnectionInstanceId: "conn-1",
                        RemoteSessionId: "remote-target",
                        ProfileId: "profile-1",
                        Reason: "SessionLoadCompleted",
                        UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
            });
        };

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-target",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "target-1",
                    Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "target cached transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-competing",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-target", new ConversationBindingSlice("conv-target", "remote-target", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-target", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "target-1",
                            Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                            IsOutgoing = false,
                            ContentType = "text",
                            TextContent = "target cached transcript"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-competing", new ConversationRuntimeSlice(
                    ConversationId: "conv-competing",
                    Phase: ConversationRuntimePhase.RemoteHydrating,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-competing",
                    ProfileId: "profile-1",
                    Reason: "RemoteHydrationPending",
                    UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
                .Add("conv-target", new ConversationRuntimeSlice(
                    ConversationId: "conv-target",
                    Phase: ConversationRuntimePhase.RemoteHydrating,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-target",
                    ProfileId: "profile-1",
                    Reason: "RemoteHydrationPending",
                    UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-target");

        Assert.True(switched);
        Assert.True(appliedWarmAfterSelected);
        Assert.Equal(0, Volatile.Read(ref targetLoadCount));
    }
}
