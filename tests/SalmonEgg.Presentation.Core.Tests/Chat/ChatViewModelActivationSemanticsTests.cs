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
}
