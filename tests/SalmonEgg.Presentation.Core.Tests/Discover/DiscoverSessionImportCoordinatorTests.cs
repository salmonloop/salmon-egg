using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Services;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Discover;

[Collection("NonParallel")]
public sealed class DiscoverSessionImportCoordinatorTests
{
    [Fact]
    public async Task ImportAsync_CreatesLocalConversationUsingRemoteCwd_AndBindsRemoteSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences();
        var sessionManager = new FakeSessionManager();
        using var workspace = CreateWorkspace(
            sessionManager,
            preferences,
            syncContext);
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new ChatStore(state);
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new DiscoverSessionImportCoordinator(
            sessionManager,
            workspace,
            bindingCommands,
            Mock.Of<ILogger<DiscoverSessionImportCoordinator>>());

        var result = await coordinator.ImportAsync("remote-session-42", @"C:\repo\remote", "profile-1");

        Assert.True(result.Succeeded);
        var localConversationId = Assert.IsType<string>(result.LocalConversationId);
        Assert.NotEqual("remote-session-42", localConversationId);
        Assert.Contains(localConversationId, workspace.GetKnownConversationIds());
        var session = Assert.IsType<Session>(sessionManager.GetSession(localConversationId));
        Assert.Equal(@"C:\repo\remote", session.Cwd);

        var binding = workspace.GetRemoteBinding(localConversationId);
        Assert.NotNull(binding);
        Assert.Equal("remote-session-42", binding!.RemoteSessionId);
        Assert.Equal("profile-1", binding.BoundProfileId);

        var currentState = await state;
        Assert.NotNull(currentState?.Bindings);
        Assert.True(currentState!.Bindings!.TryGetValue(localConversationId, out var bindingSlice));
        Assert.Equal("remote-session-42", bindingSlice.RemoteSessionId);
        Assert.Equal("profile-1", bindingSlice.ProfileId);
    }

    [Fact]
    public async Task ImportAsync_SeedsLocalConversationDisplayName_FromRemoteSessionTitle()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences();
        var sessionManager = new FakeSessionManager();
        using var workspace = CreateWorkspace(
            sessionManager,
            preferences,
            syncContext);
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new ChatStore(state);
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new DiscoverSessionImportCoordinator(
            sessionManager,
            workspace,
            bindingCommands,
            Mock.Of<ILogger<DiscoverSessionImportCoordinator>>());

        var result = await coordinator.ImportAsync(
            "remote-session-42",
            @"C:\repo\remote",
            "profile-1",
            "Agent Provided Title");

        Assert.True(result.Succeeded);
        var localConversationId = Assert.IsType<string>(result.LocalConversationId);
        var session = Assert.IsType<Session>(sessionManager.GetSession(localConversationId));
        Assert.Equal("Agent Provided Title", session.DisplayName);
    }

    [Fact]
    public async Task ImportAsync_WhenBindingFails_RollsBackCreatedConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences();
        var sessionManager = new FakeSessionManager();
        using var workspace = CreateWorkspace(
            sessionManager,
            preferences,
            syncContext);
        var coordinator = new DiscoverSessionImportCoordinator(
            sessionManager,
            workspace,
            new FailingBindingCommands(),
            Mock.Of<ILogger<DiscoverSessionImportCoordinator>>());

        var result = await coordinator.ImportAsync("remote-session-42", @"C:\repo\remote", "profile-1");

        Assert.False(result.Succeeded);
        Assert.Empty(workspace.GetKnownConversationIds());
        Assert.Empty(sessionManager.GetAllSessions());
    }

    [Fact]
    public async Task ImportAsync_LeavesProjectAffinityOverrideEmpty()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences();
        var sessionManager = new FakeSessionManager();
        using var workspace = CreateWorkspace(
            sessionManager,
            preferences,
            syncContext);
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new ChatStore(state);
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new DiscoverSessionImportCoordinator(
            sessionManager,
            workspace,
            bindingCommands,
            Mock.Of<ILogger<DiscoverSessionImportCoordinator>>());

        var result = await coordinator.ImportAsync("remote-session-42", @"C:\repo\remote", "profile-1");

        Assert.True(result.Succeeded);
        var localConversationId = Assert.IsType<string>(result.LocalConversationId);
        var overrideValue = workspace.GetProjectAffinityOverride(localConversationId);
        Assert.Null(overrideValue);
    }

    [Fact]
    public async Task ImportAsync_PassesRemoteCwdThroughUntouched()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences();
        var sessionManager = new FakeSessionManager();
        using var workspace = CreateWorkspace(
            sessionManager,
            preferences,
            syncContext);
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new ChatStore(state);
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new DiscoverSessionImportCoordinator(
            sessionManager,
            workspace,
            bindingCommands,
            Mock.Of<ILogger<DiscoverSessionImportCoordinator>>());

        const string remoteCwd = "  C:\\repo\\remote  ";
        var result = await coordinator.ImportAsync("remote-session-42", remoteCwd, "profile-1");

        Assert.True(result.Succeeded);
        var localConversationId = Assert.IsType<string>(result.LocalConversationId);
        var session = Assert.IsType<Session>(sessionManager.GetSession(localConversationId));
        Assert.Equal(remoteCwd, session.Cwd);
    }

    private static ChatConversationWorkspace CreateWorkspace(
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        SynchronizationContext syncContext)
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            return new ChatConversationWorkspace(
                sessionManager,
                new CapturingConversationStore(),
                new AppPreferencesConversationWorkspacePreferences(preferences),
                Mock.Of<ILogger<ChatConversationWorkspace>>(),
                syncContext);
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

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        public Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationDocument());

        public Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

        public IEnumerable<Session> GetAllSessions() => _sessions.Values.ToArray();

        public Session? GetSession(string sessionId)
            => _sessions.TryGetValue(sessionId, out var session) ? session : null;

        public Task<Session> CreateSessionAsync(string sessionId, string? cwd = null)
        {
            var session = new Session(sessionId, cwd)
            {
                DisplayName = sessionId
            };
            _sessions[sessionId] = session;
            return Task.FromResult(session);
        }

        public bool RemoveSession(string sessionId) => _sessions.Remove(sessionId);

        public bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return false;
            }

            updateAction(session);
            if (updateActivity)
            {
                session.LastActivityAt = DateTime.UtcNow;
            }

            return true;
        }

        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
            => Task.FromResult(_sessions.ContainsKey(sessionId));
    }

    private sealed class FailingBindingCommands : IConversationBindingCommands
    {
        public ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
            => ValueTask.FromResult(BindingUpdateResult.Error("BindingFailed"));
    }
}
