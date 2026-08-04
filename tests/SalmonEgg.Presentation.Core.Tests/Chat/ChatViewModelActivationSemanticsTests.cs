using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
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
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));

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
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                ConnectionInstanceId: "conn-1"),
                ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
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
                        false)),
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
            await fixture.ApplyCurrentStoreProjectionAsync();
            Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);

            var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);

            Assert.True(switchedRemote);
            Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
            Assert.True(Volatile.Read(ref remoteLoadCount) <= 1);
            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-remote");
            Assert.NotNull(finalRuntime);
            Assert.Equal(ConversationRuntimePhase.Warm, finalRuntime!.Value.Phase);
            Assert.True(
                string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.WarmReuse, StringComparison.Ordinal)
                || string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.SessionLoadCompleted, StringComparison.Ordinal),
                finalRuntime.Value.Reason);
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
                            Reason: "WarmReuse",
                            UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
                });
            }

            return new ConversationActivationResult(true, sessionId, null);
        }
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenColdRemoteSelectionHasCachedStoreProjection_DoesNotExposeCachedTranscriptBeforeSessionLoadCompletes()
    {
        var syncContext = new QueueingSynchronizationContext();
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

        await sessionManager.Object.CreateSessionAsync("conv-current", @"C:\repo\current");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task.WaitAsync(cancellationToken);
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
        await AwaitWithSynchronizationContextAsync(
            syncContext,
            fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-current",
            Transcript = ImmutableList.Create(
                new ConversationMessageSnapshot
                {
                    Id = "current-1",
                    Timestamp = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "current transcript"
                }),
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-2", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-remote", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "remote-cached-1",
                            Timestamp = new DateTime(2026, 5, 20, 0, 0, 1, DateTimeKind.Utc),
                            IsOutgoing = false,
                            ContentType = "text",
                            TextContent = "cached remote transcript"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false)),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-remote", new ConversationRuntimeSlice(
                    ConversationId: "conv-remote",
                    Phase: ConversationRuntimePhase.Warm,
                    ConnectionInstanceId: "old-conn",
                    RemoteSessionId: "remote-2",
                    ProfileId: "profile-1",
                    Reason: ConversationRuntimeReasons.SessionLoadCompleted,
                    UpdatedAtUtc: new DateTime(2026, 5, 20, 0, 0, 2, DateTimeKind.Utc)))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await fixture.ApplyCurrentStoreProjectionAsync();

        var exposedCachedTranscriptBeforeLoadCompleted = false;
        fixture.ViewModel.PropertyChanged += (_, _) =>
        {
            if (string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "cached remote transcript", StringComparison.Ordinal))
                && fixture.ViewModel.ShouldShowTranscriptSurface)
            {
                exposedCachedTranscriptBeforeLoadCompleted = true;
            }
        };

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
        var switchTask = switcher.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(loadStarted.Task.IsCompleted);
        });

        Assert.False(
            exposedCachedTranscriptBeforeLoadCompleted,
            "Cold remote selection exposed a cached transcript before session/load completed.");
        Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
        Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.False(fixture.ViewModel.ShouldShowTranscriptSurface);

        allowLoadCompletion.TrySetResult(null);
        await syncContext.RunUntilCompletedAsync(switchTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsRemoteHydrationPending);
        });
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenStartComposerIntentDiffersFromWarmRemoteBinding_StillSkipsRemoteSessionLoad()
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
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));

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
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                ConnectionInstanceId: "conn-1"),
                ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
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
                        false)),
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

            fixture.ViewModel.CurrentPrompt = "start composer draft";
            await fixture.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-2"));
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await fixture.ApplyCurrentStoreProjectionAsync();
            Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);

            var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);

            Assert.True(switchedRemote);
            Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
            Assert.True(Volatile.Read(ref remoteLoadCount) <= 1);
            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-remote");
            Assert.NotNull(finalRuntime);
            Assert.Equal(ConversationRuntimePhase.Warm, finalRuntime!.Value.Phase);
            Assert.True(
                string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.WarmReuse, StringComparison.Ordinal)
                || string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.SessionLoadCompleted, StringComparison.Ordinal),
                finalRuntime.Value.Reason);
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
                            Reason: "WarmReuse",
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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));

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
            CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
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
                    false)),
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
        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal("conn-1", connectionState.ConnectionInstanceId);

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-target", TestContext.Current.CancellationToken);

        Assert.True(switched);
        Assert.True(
            appliedWarmAfterSelected
            || string.Equals(fixture.ViewModel.CurrentSessionId, "conv-target", StringComparison.Ordinal));
        Assert.Equal(0, Volatile.Read(ref targetLoadCount));
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenReturningToSameRemoteWhileLoadIsInFlight_ReusesExistingSessionLoad()
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

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                Interlocked.Increment(ref loadCount);
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local", TestContext.Current.CancellationToken);
        Assert.True(localSwitch);

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)
                && fixture.ViewModel.IsRemoteHydrationPending);
        }, timeoutMilliseconds: 2000);

        Assert.Equal(1, Volatile.Read(ref loadCount));

        allowLoadCompletion.TrySetResult(null);
        Assert.True(await secondRemoteSwitch);
        await firstRemoteSwitch;

        var finalState = await fixture.GetStateAsync();
        Assert.Equal("conv-remote", finalState.HydratedConversationId);
        Assert.Equal(ConversationRuntimePhase.Warm, finalState.ResolveRuntimeState("conv-remote")?.Phase);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenCurrentWebSocketRemoteConversationIsCold_StartsConnectionInsteadOfWarmReuse()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessionManager = CreateSessionManagerWithStore();
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var connectEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowConnectCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadEntered = new TaskCompletionSource<SessionLoadParams>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        ViewModelFixture? fixture = null;
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat
        };
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-ws-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                Interlocked.Increment(ref loadCount);
                loadEntered.TrySetResult(parameters);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(command => command.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-ws", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-remote", StringComparison.Ordinal)
                    && context.PreserveConversation
                    && context.ActivationVersion.HasValue),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(
                async (connectedProfile, _, sink, _, cancellationToken) =>
                {
                    await sink.SelectProfileAsync(connectedProfile, cancellationToken);
                    sink.UpdateConnectionState(isConnecting: true, isConnected: false, isInitialized: false, errorMessage: null);
                    connectEntered.TrySetResult(null);
                    await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                    sink.UpdateConnectionState(isConnecting: false, isConnected: true, isInitialized: true, errorMessage: null);
                    await fixture!.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-ws-1"));
                    await sink.ReplaceChatServiceAsync(chatService.Object, cancellationToken);
                    return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
                });

        var profile = new ServerConfiguration
        {
            Id = "profile-ws",
            Name = "WebSocket profile",
            Transport = TransportType.WebSocket,
            ServerUrl = "ws://127.0.0.1:3010/"
        };
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(service => service.LoadConfigurationAsync("profile-ws"))
            .ReturnsAsync(profile);
        configurationService.Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync([profile]);

        fixture = CreateViewModel(
            syncContext,
            configurationService: configurationService,
            sessionManager: sessionManager,
            acpConnectionCommands: commands.Object,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(
                store,
                NullLogger<AcpConnectionCoordinator>.Instance,
                new StaticMcpResolver([]),
                new AcpRemoteSessionRecoveryContextResolver(
                    NullLogger<AcpRemoteSessionRecoveryContextResolver>.Instance)),
            shellNavigationRuntimeState: runtimeState);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(profile);
            await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpdateRemoteBinding("conv-remote", "remote-ws-1", "profile-ws");
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-remote",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-ws-1", "profile-ws")),
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
            });
            SetCurrentSessionId(fixture.ViewModel, "conv-remote");

            var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
            var switchTask = switcher.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);

            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(connectEntered.Task.IsCompleted);
            });

            Assert.True(connectEntered.Task.IsCompleted);
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(
                    fixture.ViewModel.IsOverlayVisible
                    && fixture.ViewModel.OverlayLoadingStage == ChatViewModel.LoadingOverlayStage.Connecting);
            });
            Assert.True(fixture.ViewModel.IsOverlayVisible);
            Assert.Equal(ChatViewModel.LoadingOverlayStage.Connecting, fixture.ViewModel.OverlayLoadingStage);
            Assert.True(await switchTask);

            allowConnectCompletion.TrySetResult(null);
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(loadEntered.Task.IsCompleted);
            });
            var loadParameters = await loadEntered.Task;
            Assert.Equal("remote-ws-1", loadParameters.SessionId);
            Assert.Equal(1, Volatile.Read(ref loadCount));

            await WaitForConditionAsync(async () =>
            {
                syncContext.RunAll();
                var state = await fixture.GetStateAsync();
                return state.ResolveRuntimeState("conv-remote")?.Phase == ConversationRuntimePhase.Warm;
            });
            chatService.Verify(service => service.LoadSessionAsync(
                    It.Is<SessionLoadParams>(parameters =>
                        string.Equals(parameters.SessionId, "remote-ws-1", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenRemoteBoundConversationHasNoReadyChatService_UsesSelectionOnlyBeforeRemoteHydration()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessionManager = CreateSessionManagerWithStore();
        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        ConversationActivationHydrationMode? capturedHydrationMode = null;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, hydrationMode, _) =>
            {
                capturedHydrationMode = hydrationMode;
                return Task.FromResult(new ConversationActivationResult(true, sessionId, null));
            });

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "stale-remote-1",
                    Timestamp = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript must not be selection source"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpdateRemoteBinding("conv-remote", "remote-ws-1", "profile-ws");
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-ws-1", "profile-ws"))
        });

        var switched = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(capturedHydrationMode.HasValue || switched.IsCompleted);
        });
        Assert.Equal(ConversationActivationHydrationMode.SelectionOnly, capturedHydrationMode);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenTogglingBetweenTwoRemoteLoads_ReusesEachInFlightSessionLoad()
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

        await sessionManager.Object.CreateSessionAsync("conv-a", @"C:\repo\a");
        await sessionManager.Object.CreateSessionAsync("conv-b", @"C:\repo\b");

        var aStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowACompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aLoadCount = 0;
        var bLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.IsAny<SessionLoadParams>(),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, cancellationToken) =>
            {
                if (string.Equals(parameters.SessionId, "remote-a", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref aLoadCount);
                    aStarted.TrySetResult(null);
                    await allowACompletion.Task.WaitAsync(cancellationToken);
                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-b", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref bLoadCount);
                    bStarted.TrySetResult(null);
                    await allowBCompletion.Task.WaitAsync(cancellationToken);
                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected remote session id: {parameters.SessionId}");
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-a",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-b",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-a",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-a", new ConversationBindingSlice("conv-a", "remote-a", "profile-1"))
                .Add("conv-b", new ConversationBindingSlice("conv-b", "remote-b", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        var firstASwitch = fixture.ViewModel.SwitchConversationAsync("conv-a", TestContext.Current.CancellationToken);
        await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var bSwitch = fixture.ViewModel.SwitchConversationAsync("conv-b", TestContext.Current.CancellationToken);
        await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var secondASwitch = fixture.ViewModel.SwitchConversationAsync("conv-a", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-a", StringComparison.Ordinal)
                && fixture.ViewModel.IsRemoteHydrationPending);
        }, timeoutMilliseconds: 2000);

        Assert.Equal(1, Volatile.Read(ref aLoadCount));
        Assert.Equal(1, Volatile.Read(ref bLoadCount));

        allowACompletion.TrySetResult(null);
        Assert.True(await secondASwitch);

        allowBCompletion.TrySetResult(null);
        await firstASwitch;
        await bSwitch;
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenSupersededBackgroundHydrationCompletes_PromotesConversationToAuthoritativeWarm()
    {
        // Evidence chain:
        // 1) Fast switching supersedes the previous activation (BeginActivation cancels the prior context),
        //    but the background recovery task keeps running on the request token (decoupled from activation).
        // 2) When that superseded background session/load completes, PublishRemoteSessionRecoveryProjectionAsync
        //    hits the "no longer projection owner" branch. It must still promote the conversation to authoritative
        //    Warm (SessionLoadCompleted) and land the projection, without touching the foreground session.
        // 3) Otherwise the passed-over conversation stays RemoteHydrating forever, so returning to it always
        //    denies warm reuse (RuntimeStateNotWarm) and re-runs a slow session/load — the observed stutter.
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

        await sessionManager.Object.CreateSessionAsync("conv-a", @"C:\repo\a");
        await sessionManager.Object.CreateSessionAsync("conv-b", @"C:\repo\b");

        var aLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowALoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aLoadCount = 0;
        var bLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.IsAny<SessionLoadParams>(),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, cancellationToken) =>
            {
                if (string.Equals(parameters.SessionId, "remote-a", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref aLoadCount);
                    aLoadStarted.TrySetResult(null);
                    await allowALoadCompletion.Task.WaitAsync(cancellationToken);
                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-b", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref bLoadCount);
                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected remote session id: {parameters.SessionId}");
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-a",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-b",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-a",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-a", new ConversationBindingSlice("conv-a", "remote-a", "profile-1"))
                .Add("conv-b", new ConversationBindingSlice("conv-b", "remote-b", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;

        // Switch to A; its session/load starts and hangs in the background.
        var switchA = switcher.SwitchConversationAsync("conv-a", TestContext.Current.CancellationToken);
        await aLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Switch to B before A finishes; this supersedes A's activation. B completes quickly.
        var switchB = switcher.SwitchConversationAsync("conv-b", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-b", StringComparison.Ordinal));
        }, timeoutMilliseconds: 5000);

        // Let A's superseded background load finish.
        allowALoadCompletion.TrySetResult(null);
        await switchA;
        await switchB;

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return state.ResolveRuntimeState("conv-a")?.Phase == ConversationRuntimePhase.Warm;
        }, timeoutMilliseconds: 5000);

        var finalState = await fixture.GetStateAsync();
        var runtimeA = finalState.ResolveRuntimeState("conv-a");
        Assert.NotNull(runtimeA);
        Assert.Equal(ConversationRuntimePhase.Warm, runtimeA!.Value.Phase);
        Assert.Equal(ConversationRuntimeReasons.SessionLoadCompleted, runtimeA.Value.Reason);
        // Superseded background completion must not steal the foreground session.
        Assert.Equal("conv-b", finalState.HydratedConversationId);
        Assert.Equal(1, Volatile.Read(ref aLoadCount));
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenConnectionIdentityChangesDuringHydration_ReachesTerminalActivationPhase()
    {
        // The recovery projection is discarded when the connection instance changes mid-hydration,
        // and that path restores the runtime slice without touching the activation surface. Nothing
        // supersedes this activation, so it must still reach a terminal phase; otherwise the shell
        // sits on RemoteHydrationPending forever, neither settling nor reporting a failure.
        var syncContext = new ImmediateSynchronizationContext();
        var sessionManager = CreateSessionManagerWithStore();
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var loadEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The navigation coordinator owns the activation snapshot in production. The orchestrator
        // assigns the activation version, so mirror it into the snapshot the way the coordinator
        // does: the outcome publisher only accepts a snapshot whose Version matches the activation.
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat
        };
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                loadEntered.TrySetResult(null);
                await allowLoadCompletion.Task.WaitAsync(cancellationToken);
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-old"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-old"));

        // Mirror the coordinator: it publishes the activation snapshot before handing control to the
        // chat view model. Both the coordinator token and the orchestrator version start at zero and
        // advance once per activation, so this single activation carries version 1.
        runtimeState.LatestActivationToken = 1;
        runtimeState.ActiveSessionActivationVersion = 1;
        runtimeState.IsSessionActivationInProgress = true;
        runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
            "conv-remote",
            "project-1",
            1,
            SessionActivationPhase.Selected);

        var switchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(
            () => Task.FromResult(loadEntered.Task.IsCompleted),
            timeoutMilliseconds: 5000);

        // The transport reconnects underneath the in-flight hydration. No new activation is issued.
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-new"));
        allowLoadCompletion.TrySetResult(null);
        await switchTask;

        var activation = runtimeState.ActiveSessionActivation;
        Assert.NotNull(activation);
        Assert.True(
            activation!.Phase is SessionActivationPhase.Hydrated or SessionActivationPhase.Faulted,
            $"Activation must reach a terminal phase; observed {activation.Phase} (reason {activation.Reason ?? "<null>"}).");
        Assert.False(runtimeState.IsSessionActivationInProgress);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenSameRemoteSessionMovesToNewConnectionInstance_CancelsOldRecoveryAndStartsNewLoad()
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

        var oldLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                var invocation = Interlocked.Increment(ref loadCount);
                if (invocation == 1)
                {
                    oldLoadStarted.TrySetResult(null);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        return SessionLoadResponse.Completed;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldLoadCanceled.TrySetResult(null);
                        throw;
                    }
                }

                Assert.Equal(2, invocation);
                newLoadStarted.TrySetResult(null);
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-old"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-old"));

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(
            () => Task.FromResult(oldLoadStarted.Task.IsCompleted),
            timeoutMilliseconds: 5000);

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local", TestContext.Current.CancellationToken);
        Assert.True(localSwitch);
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-new"));

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await WaitForConditionAsync(
            () => Task.FromResult(newLoadStarted.Task.IsCompleted),
            timeoutMilliseconds: 5000);
        await WaitForConditionAsync(
            () => Task.FromResult(oldLoadCanceled.Task.IsCompleted),
            timeoutMilliseconds: 5000);

        Assert.Equal(2, Volatile.Read(ref loadCount));
        Assert.True(await secondRemoteSwitch);
        Assert.False(await firstRemoteSwitch);
    }

    [Fact]
    public async Task ReplaceChatServiceAsync_WhenRemoteLoadIsInFlightWithoutConnectionInstance_CancelsOldRecoveryAndUsesReplacementService()
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

        var oldLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldService = CreateConnectedChatService();
        oldService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        oldService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                oldLoadStarted.TrySetResult(null);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return SessionLoadResponse.Completed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    oldLoadCanceled.TrySetResult(null);
                    throw;
                }
            });

        var newLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLoadCount = 0;
        var replacementService = CreateConnectedChatService();
        replacementService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        replacementService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref newLoadCount);
                newLoadStarted.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(oldService.Object, TestContext.Current.CancellationToken));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc)),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await oldLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local", TestContext.Current.CancellationToken);
        Assert.True(localSwitch);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(replacementService.Object, TestContext.Current.CancellationToken));
        await WaitForConditionAsync(() => Task.FromResult(oldLoadCanceled.Task.IsCompleted), timeoutMilliseconds: 2000);
        Assert.False(await firstRemoteSwitch);

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await newLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref newLoadCount));
        Assert.True(await secondRemoteSwitch);
    }

    [Fact]
    public void RemoteSessionRecoveryRequestRegistry_DoesNotCallExternalWorkWhileHoldingRegistryLock()
    {
        var lifecycleSource = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var requestSource = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.cs"));
        var getOrStartBody = ExtractMethodBody(lifecycleSource, "private AcpSessionRecoveryStartResult GetOrStartRemoteSessionRecoveryProjection");
        var cleanupBody = ExtractMethodBody(lifecycleSource, "private void CancelAndClearRemoteSessionRecoveryRequests");
        var supersedeBody = ExtractMethodBody(lifecycleSource, "private List<RemoteSessionRecoveryRequest> RemoveConflictingRemoteSessionRecoveryRequestsLocked");
        var cancelBody = ExtractMethodBody(requestSource, "public void Cancel()");
        var cancelTransportBody = ExtractMethodBody(requestSource, "public void CancelTransport()");

        Assert.DoesNotContain("RunRemoteSessionRecoveryProjectionAsync", ExtractFirstLockBlock(getOrStartBody), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlock(cleanupBody), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", supersedeBody, StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlockOrEmpty(cancelBody, "lock (_sync)"), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlockOrEmpty(cancelTransportBody, "lock (_sync)"), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteSessionRecoveryTransportTasks_AreObservedAfterWaiterCancellation()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var runBody = ExtractMethodBody(source, "private async Task<AcpSessionRecoveryProjection> RunRemoteSessionRecoveryProjectionAsync");
        var loadBody = ExtractMethodBody(source, "private async Task<AcpSessionRecoveryProjection> RunRemoteSessionLoadRecoveryProjectionAsync");

        Assert.Matches(@"ObserveRemoteSessionRecoveryTransportTaskAsync\s*\(\s*loadTask\b", loadBody);
        Assert.Matches(@"ObserveRemoteSessionRecoveryTransportTaskAsync\s*\(\s*resumeTask\b", runBody);
    }

    [Fact]
    public void RemoteSessionRecoveryRequestCleanup_WaitsForExecutionBeforeDisposingCancellationSource()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var cleanupBody = ExtractMethodBody(source, "private async Task RemoveRemoteSessionRecoveryRequestWhenCompleteAsync");

        var executionAwaitIndex = cleanupBody.IndexOf("await request.ExecutionTask.ConfigureAwait(false)", StringComparison.Ordinal);
        var disposeIndex = cleanupBody.IndexOf("request.Dispose();", StringComparison.Ordinal);
        Assert.True(executionAwaitIndex >= 0, "Cleanup must wait for the recovery request execution to unwind.");
        Assert.True(disposeIndex > executionAwaitIndex, "Cleanup must not dispose the request before execution unwinds.");
    }

    private static string ExtractFirstLockBlock(string source, string lockPattern = "lock (_remoteSessionRecoveryRequestsSync)")
    {
        var lockStart = source.IndexOf(lockPattern, StringComparison.Ordinal);
        Assert.True(lockStart >= 0, $"Could not find lock block: {lockPattern}");
        return ExtractBlockAt(source, lockStart);
    }

    private static string ExtractFirstLockBlockOrEmpty(string source, string lockPattern)
    {
        var lockStart = source.IndexOf(lockPattern, StringComparison.Ordinal);
        return lockStart < 0 ? string.Empty : ExtractBlockAt(source, lockStart);
    }

    private static string ExtractBlockAt(string source, int blockOwnerStart)
    {
        var bodyStart = source.IndexOf('{', blockOwnerStart);
        Assert.True(bodyStart >= 0, "Could not find lock body.");
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not extract lock body.");
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature: {methodSignature}");
        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body: {methodSignature}");
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body: {methodSignature}");
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativeSegments));
    }
}
