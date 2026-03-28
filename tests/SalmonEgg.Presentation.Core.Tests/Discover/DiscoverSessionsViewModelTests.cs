using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Discover;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Discover;

[Collection("NonParallel")]
public sealed class DiscoverSessionsViewModelTests
{
    [Fact]
    public async Task RefreshSessionsAsync_WhenRemoteListIsEmpty_UsesEmptyPhaseInsteadOfError()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse()
                }
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new StubImportCoordinator(),
                new StubNavigationCoordinator());

            await viewModel.RefreshSessionsCommand.ExecuteAsync(null);

            Assert.Equal(DiscoverSessionsLoadPhase.Empty, viewModel.LoadPhase);
            Assert.True(viewModel.ShowEmptyState);
            Assert.False(viewModel.HasError);
            Assert.False(viewModel.IsListVisible);
            Assert.Empty(viewModel.AgentSessions);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhenImportFailsAfterAwait_MarshalsErrorStateThroughUiContext()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var importCoordinator = new DelayedImportCoordinator(
                async () =>
                {
                    await Task.Delay(10);
                    return new DiscoverSessionImportResult(false, null, "导入失败");
                });
            using var viewModel = CreateViewModel(
                profilesViewModel,
                new FakeDiscoverSessionsConnectionFacade(),
                importCoordinator,
                new StubNavigationCoordinator());

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);
            Assert.Equal("导入失败", viewModel.ErrorMessage);
            Assert.True(syncContext.PostCount > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_ActivatesImportedLocalConversationAndHydratesSharedChatFacade()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CountingSynchronizationContext());
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = true
            };
            var importCoordinator = new RecordingImportCoordinator(
                new DiscoverSessionImportResult(true, "local-conversation-1", null));
            var navigationCoordinator = new StubNavigationCoordinator
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(("remote-session-1", @"C:\repo\remote", "profile-1", "Remote Session"), importCoordinator.LastRequest);
            Assert.Equal(("local-conversation-1", null), navigationCoordinator.LastActivation);
            Assert.Equal(1, connectionFacade.HydrateCalls);
            Assert.Null(viewModel.ErrorMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhenHydrationFails_SetsPageErrorState()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CountingSynchronizationContext());
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = false
            };
            var importCoordinator = new RecordingImportCoordinator(
                new DiscoverSessionImportResult(true, "local-conversation-1", null));
            var navigationCoordinator = new StubNavigationCoordinator
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);
            Assert.Equal("导入后的会话历史加载失败，请检查 ACP 连接状态。", viewModel.ErrorMessage);
            Assert.Equal(1, connectionFacade.HydrateCalls);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_AfterAsyncImport_MarshalsActivationAndHydrationBackToUiContext()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = true,
                ExpectedSynchronizationContext = syncContext,
                RequireExpectedSynchronizationContextForHydrate = true
            };
            var importCoordinator = new DelayedImportCoordinator(async () =>
            {
                await Task.Delay(10);
                return new DiscoverSessionImportResult(true, "local-conversation-1", null);
            });
            var navigationCoordinator = new ContextAssertingNavigationCoordinator(syncContext)
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.True(navigationCoordinator.WasCalledOnExpectedContext);
            Assert.True(connectionFacade.HydrateCalledOnExpectedContext);
            Assert.Null(viewModel.ErrorMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhileImportIsRunning_KeepsLifecycleLoadingVisible()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var allowImportCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importCoordinator = new DelayedImportCoordinator(async () =>
            {
                importStarted.TrySetResult(null);
                await allowImportCompletion.Task;
                return new DiscoverSessionImportResult(true, "local-conversation-1", null);
            });
            using var viewModel = CreateViewModel(
                profilesViewModel,
                new FakeDiscoverSessionsConnectionFacade
                {
                    HydrateResult = true
                },
                importCoordinator,
                new StubNavigationCoordinator
                {
                    ActivationResult = true
                });

            var loadTask = viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            await importStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在导入会话...", viewModel.LoadingStatus);

            allowImportCompletion.TrySetResult(null);
            await loadTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhileActivationAndHydrationAreRunning_KeepsLifecycleLoadingVisible()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var allowActivationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowHydrationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hydrationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse
                    {
                        Sessions =
                        {
                            new AgentSessionInfo
                            {
                                SessionId = "remote-session-1",
                                Title = "Remote Session",
                                Description = "Imported from ACP",
                                UpdatedAt = "2026-03-27T12:00:00+08:00",
                                Cwd = @"C:\repo\remote"
                            }
                        }
                    }
                },
                OnHydrateAsync = async cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hydrationStarted.TrySetResult(null);
                    await allowHydrationCompletion.Task.WaitAsync(cancellationToken);
                    return true;
                }
            };

            var navigationCoordinator = new DelayedNavigationCoordinator(async () =>
            {
                activationStarted.TrySetResult(null);
                await allowActivationCompletion.Task;
                return true;
            });

            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new RecordingImportCoordinator(new DiscoverSessionImportResult(true, "local-conversation-1", null)),
                navigationCoordinator);

            await viewModel.RefreshSessionsCommand.ExecuteAsync(null);
            Assert.Equal(DiscoverSessionsLoadPhase.Loaded, viewModel.LoadPhase);

            var loadTask = viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在打开会话...", viewModel.LoadingStatus);

            allowActivationCompletion.TrySetResult(null);

            await hydrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在加载会话历史...", viewModel.LoadingStatus);

            allowHydrationCompletion.TrySetResult(null);
            await loadTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static DiscoverSessionsViewModel CreateViewModel(
        AcpProfilesViewModel profilesViewModel,
        IDiscoverSessionsConnectionFacade connectionFacade,
        IDiscoverSessionImportCoordinator importCoordinator,
        INavigationCoordinator navigationCoordinator)
    {
        return new DiscoverSessionsViewModel(
            Mock.Of<ILogger<DiscoverSessionsViewModel>>(),
            navigationCoordinator,
            profilesViewModel,
            connectionFacade,
            importCoordinator);
    }

    private static AcpProfilesViewModel CreateProfilesViewModel(ServerConfiguration profile)
    {
        var configurationService = new Mock<IConfigurationService>();
        var preferences = CreatePreferences();
        var profilesViewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            Mock.Of<ILogger<AcpProfilesViewModel>>());
        profilesViewModel.Profiles.Add(profile);
        profilesViewModel.SelectedProfile = profile;
        return profilesViewModel;
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

    private static ServerConfiguration CreateProfile()
        => new()
        {
            Id = "profile-1",
            Name = "Demo Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe"
        };

    private static DiscoverSessionItemViewModel CreateSessionItem()
        => new(
            "remote-session-1",
            "Remote Session",
            "Imported from ACP",
            new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Local),
            @"C:\repo\remote");

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            var originalContext = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            finally
            {
                SetSynchronizationContext(originalContext);
            }
        }
    }

    private sealed class FakeDiscoverSessionsConnectionFacade : IDiscoverSessionsConnectionFacade
    {
        private bool _isConnecting;
        private bool _isInitializing;
        private bool _isConnected;
        private string? _connectionErrorMessage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetProperty(ref _isConnecting, value, nameof(IsConnecting));
        }

        public bool IsInitializing
        {
            get => _isInitializing;
            private set => SetProperty(ref _isInitializing, value, nameof(IsInitializing));
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value, nameof(IsConnected));
        }

        public string? ConnectionErrorMessage
        {
            get => _connectionErrorMessage;
            set => SetProperty(ref _connectionErrorMessage, value, nameof(ConnectionErrorMessage));
        }

        public IChatService? CurrentChatService { get; set; }

        public bool HydrateResult { get; set; } = true;

        public int HydrateCalls { get; private set; }

        public SynchronizationContext? ExpectedSynchronizationContext { get; set; }

        public bool RequireExpectedSynchronizationContextForHydrate { get; set; }

        public bool HydrateCalledOnExpectedContext { get; private set; }

        public Func<CancellationToken, Task<bool>>? OnHydrateAsync { get; set; }

        public async Task ConnectToProfileAsync(ServerConfiguration profile)
        {
            IsConnecting = true;
            await Task.Yield();
            IsConnecting = false;
            IsInitializing = true;
            await Task.Yield();
            IsInitializing = false;
            IsConnected = true;
        }

        public Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HydrateCalls++;
            HydrateCalledOnExpectedContext = ReferenceEquals(SynchronizationContext.Current, ExpectedSynchronizationContext);
            if (RequireExpectedSynchronizationContextForHydrate && !HydrateCalledOnExpectedContext)
            {
                return Task.FromResult(false);
            }

            if (OnHydrateAsync != null)
            {
                return OnHydrateAsync(cancellationToken);
            }

            return Task.FromResult(HydrateResult);
        }

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class RecordingImportCoordinator : IDiscoverSessionImportCoordinator
    {
        private readonly DiscoverSessionImportResult _result;

        public RecordingImportCoordinator(DiscoverSessionImportResult result)
        {
            _result = result;
        }

        public (string RemoteSessionId, string? RemoteSessionCwd, string? ProfileId, string? RemoteSessionTitle)? LastRequest { get; private set; }

        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = (remoteSessionId, remoteSessionCwd, profileId, remoteSessionTitle);
            return Task.FromResult(_result);
        }
    }

    private sealed class DelayedImportCoordinator : IDiscoverSessionImportCoordinator
    {
        private readonly Func<Task<DiscoverSessionImportResult>> _resultFactory;

        public DelayedImportCoordinator(Func<Task<DiscoverSessionImportResult>> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _resultFactory();
        }
    }

    private sealed class StubImportCoordinator : IDiscoverSessionImportCoordinator
    {
        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DiscoverSessionImportResult(true, "local-session", null));
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public bool ActivationResult { get; set; } = true;

        public (string SessionId, string? ProjectId)? LastActivation { get; private set; }

        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            LastActivation = (sessionId, projectId);
            return Task.FromResult(ActivationResult);
        }

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }

    private sealed class DelayedNavigationCoordinator : INavigationCoordinator
    {
        private readonly Func<Task<bool>> _activation;

        public DelayedNavigationCoordinator(Func<Task<bool>> activation)
        {
            _activation = activation;
        }

        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => _activation();

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }

    private sealed class ContextAssertingNavigationCoordinator : INavigationCoordinator
    {
        private readonly SynchronizationContext _expectedSynchronizationContext;

        public ContextAssertingNavigationCoordinator(SynchronizationContext expectedSynchronizationContext)
        {
            _expectedSynchronizationContext = expectedSynchronizationContext;
        }

        public bool ActivationResult { get; set; } = true;

        public bool WasCalledOnExpectedContext { get; private set; }

        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            WasCalledOnExpectedContext = ReferenceEquals(SynchronizationContext.Current, _expectedSynchronizationContext);
            return Task.FromResult(WasCalledOnExpectedContext && ActivationResult);
        }

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }

    private sealed class FakeChatService : IChatService
    {
        public string? CurrentSessionId => null;

        public bool IsInitialized => true;

        public bool IsConnected => true;

        public AgentInfo? AgentInfo => null;

        public AgentCapabilities? AgentCapabilities => new(loadSession: true);

        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();

        public Plan? CurrentPlan => null;

        public SessionModeState? CurrentMode => null;

        public SessionListResponse SessionListResponse { get; set; } = new();

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => throw new NotSupportedException();

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
            => throw new NotSupportedException();

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => throw new NotSupportedException();

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
            => Task.FromResult(SessionListResponse);

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
            => throw new NotSupportedException();

        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
            => throw new NotSupportedException();

        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
            => throw new NotSupportedException();

        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
            => throw new NotSupportedException();

        public Task<bool> DisconnectAsync()
            => throw new NotSupportedException();

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
            => throw new NotSupportedException();

        public void ClearHistory()
        {
        }
    }
}
