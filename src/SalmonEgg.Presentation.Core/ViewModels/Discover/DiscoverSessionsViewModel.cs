using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Discover;

public sealed partial class DiscoverSessionsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<DiscoverSessionsViewModel> _logger;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly AcpProfilesViewModel _profilesViewModel;
    private readonly IDiscoverSessionsConnectionFacade _connectionFacade;
    private readonly IDiscoverSessionImportCoordinator _importCoordinator;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly SynchronizationContext _syncContext;
    private CancellationTokenSource? _refreshSessionsCts;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(LoadingStatus))]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private DiscoverSessionsLoadPhase _loadPhase = DiscoverSessionsLoadPhase.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(DisplayErrorMessage))]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(DisplayErrorMessage);

    public string? DisplayErrorMessage => ErrorMessage ?? _connectionFacade.ConnectionErrorMessage;

    public bool IsLoading => LoadPhase is
        DiscoverSessionsLoadPhase.Connecting or
        DiscoverSessionsLoadPhase.Initializing or
        DiscoverSessionsLoadPhase.ListingSessions or
        DiscoverSessionsLoadPhase.ImportingSession or
        DiscoverSessionsLoadPhase.ActivatingSession or
        DiscoverSessionsLoadPhase.HydratingSession;

    public string? LoadingStatus => LoadPhase switch
    {
        DiscoverSessionsLoadPhase.Connecting => "正在连接到 Agent...",
        DiscoverSessionsLoadPhase.Initializing => "正在初始化 ACP 协议...",
        DiscoverSessionsLoadPhase.ListingSessions => "正在获取会话列表...",
        DiscoverSessionsLoadPhase.ImportingSession => "正在导入会话...",
        DiscoverSessionsLoadPhase.ActivatingSession => "正在打开会话...",
        DiscoverSessionsLoadPhase.HydratingSession => "正在加载会话历史...",
        _ => null
    };

    public bool IsListVisible => LoadPhase == DiscoverSessionsLoadPhase.Loaded;

    public bool ShowEmptyState => LoadPhase == DiscoverSessionsLoadPhase.Empty;

    public AcpProfilesViewModel ProfilesViewModel => _profilesViewModel;

    public ServerConfiguration? SelectedProfile
    {
        get => _profilesViewModel.SelectedProfile;
        set => _profilesViewModel.SelectedProfile = value;
    }

    public bool HasSelectedProfile => SelectedProfile != null;

    public bool HasNoSelectedProfile => SelectedProfile == null;

    public ObservableCollection<ServerConfiguration> AvailableProfiles => _profilesViewModel.Profiles;

    public ObservableCollection<DiscoverSessionItemViewModel> AgentSessions { get; } = new();

    public DiscoverSessionsViewModel(
        ILogger<DiscoverSessionsViewModel> logger,
        INavigationCoordinator navigationCoordinator,
        INavigationProjectPreferences projectPreferences,
        AcpProfilesViewModel profilesViewModel,
        IDiscoverSessionsConnectionFacade connectionFacade,
        IDiscoverSessionImportCoordinator importCoordinator,
        IProjectAffinityResolver? projectAffinityResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _profilesViewModel = profilesViewModel ?? throw new ArgumentNullException(nameof(profilesViewModel));
        _connectionFacade = connectionFacade ?? throw new ArgumentNullException(nameof(connectionFacade));
        _importCoordinator = importCoordinator ?? throw new ArgumentNullException(nameof(importCoordinator));
        _projectAffinityResolver = projectAffinityResolver ?? new ProjectAffinityResolver();
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        _profilesViewModel.PropertyChanged += OnProfilesViewModelPropertyChanged;
        _connectionFacade.PropertyChanged += OnConnectionFacadePropertyChanged;
    }

    private void OnProfilesViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AcpProfilesViewModel.SelectedProfile))
        {
            return;
        }

        _syncContext.Post(_ =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(HasNoSelectedProfile));

            _ = RefreshSessionsAsync();
        }, null);
    }

    private void OnConnectionFacadePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IDiscoverSessionsConnectionFacade.IsConnecting)
            and not nameof(IDiscoverSessionsConnectionFacade.IsInitializing)
            and not nameof(IDiscoverSessionsConnectionFacade.IsConnected)
            and not nameof(IDiscoverSessionsConnectionFacade.ConnectionErrorMessage))
        {
            return;
        }

        _syncContext.Post(_ =>
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(DisplayErrorMessage));
            if (LoadPhase is DiscoverSessionsLoadPhase.Connecting or DiscoverSessionsLoadPhase.Initializing)
            {
                SyncConnectionPhase();
            }
        }, null);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await _profilesViewModel.RefreshIfEmptyAsync().ConfigureAwait(false);

        _syncContext.Post(_ =>
        {
            OnPropertyChanged(nameof(AvailableProfiles));
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(HasNoSelectedProfile));

            if (SelectedProfile != null)
            {
                if (AgentSessions.Count == 0 && !IsLoading)
                {
                    _ = RefreshSessionsAsync();
                }
            }
            else if (AvailableProfiles.Any())
            {
                SelectedProfile = AvailableProfiles.First();
            }
        }, null);
    }

    [RelayCommand]
    private async Task RefreshSessionsAsync()
    {
        var profile = SelectedProfile;
        if (profile == null)
        {
            await PostToUiAsync(() =>
            {
                AgentSessions.Clear();
                ErrorMessage = null;
                LoadPhase = DiscoverSessionsLoadPhase.Idle;
            }).ConfigureAwait(false);
            return;
        }

        _refreshSessionsCts?.Cancel();
        _refreshSessionsCts?.Dispose();
        _refreshSessionsCts = new CancellationTokenSource();
        var cancellationToken = _refreshSessionsCts.Token;

        await PostToUiAsync(() =>
        {
            AgentSessions.Clear();
            ErrorMessage = null;
            LoadPhase = DiscoverSessionsLoadPhase.Connecting;
            SyncConnectionPhase();
        }).ConfigureAwait(false);

        try
        {
            await _connectionFacade.ConnectToProfileAsync(profile).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var chatService = _connectionFacade.CurrentChatService;
            if (chatService is not { IsConnected: true, IsInitialized: true })
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_connectionFacade.ConnectionErrorMessage)
                        ? "ACP 连接尚未完成初始化。"
                        : _connectionFacade.ConnectionErrorMessage);
            }

            await PostToUiAsync(() => LoadPhase = DiscoverSessionsLoadPhase.ListingSessions).ConfigureAwait(false);
            var listResponse = await chatService
                .ListSessionsAsync(new SessionListParams(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var items = new List<DiscoverSessionItemViewModel>();
            var projects = _projectPreferences.Projects
                .Where(project => project != null
                                  && !string.IsNullOrWhiteSpace(project.ProjectId)
                                  && !string.IsNullOrWhiteSpace(project.Name))
                .ToList();
            var projectPathMappings = _projectPreferences.ProjectPathMappings
                .Where(mapping => mapping != null)
                .ToList();
            if (listResponse?.Sessions != null)
            {
                foreach (var session in listResponse.Sessions)
                {
                    var lastModified = DateTime.Now;
                    if (!string.IsNullOrWhiteSpace(session.UpdatedAt)
                        && DateTime.TryParse(session.UpdatedAt, out var parsed))
                    {
                        lastModified = parsed;
                    }

                    var affinityResolution = _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
                        RemoteCwd: session.Cwd,
                        BoundProfileId: profile.Id,
                        RemoteSessionId: session.SessionId,
                        OverrideProjectId: null,
                        Projects: projects,
                        PathMappings: projectPathMappings,
                        UnclassifiedProjectId: NavigationProjectIds.Unclassified));
                    items.Add(new DiscoverSessionItemViewModel(
                        session.SessionId,
                        string.IsNullOrWhiteSpace(session.Title) ? "未命名会话" : session.Title,
                        string.IsNullOrWhiteSpace(session.Description) ? "暂无描述" : session.Description,
                        lastModified,
                        session.Cwd,
                        ResolveAffinityBadgeText(affinityResolution, projects),
                        ResolveAffinityStatusText(affinityResolution),
                        affinityResolution.Source,
                        affinityResolution.NeedsUserAttention));
                }
            }

            await PostToUiAsync(() =>
            {
                AgentSessions.Clear();
                foreach (var item in items)
                {
                    AgentSessions.Add(item);
                }

                LoadPhase = AgentSessions.Count == 0
                    ? DiscoverSessionsLoadPhase.Empty
                    : DiscoverSessionsLoadPhase.Loaded;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sessions for profile {ProfileId}", profile.Id);
            await PostToUiAsync(() =>
            {
                ErrorMessage = ResolveLoadErrorMessage(ex);
                LoadPhase = DiscoverSessionsLoadPhase.Error;
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task LoadSessionAsync(DiscoverSessionItemViewModel? session)
    {
        if (session == null || SelectedProfile == null)
        {
            return;
        }

        try
        {
            await PostToUiAsync(() =>
            {
                ErrorMessage = null;
                LoadPhase = DiscoverSessionsLoadPhase.ImportingSession;
            }).ConfigureAwait(false);

            var importResult = await _importCoordinator
                .ImportAsync(session.Id, session.SessionCwd, SelectedProfile.Id, session.Title)
                .ConfigureAwait(false);
            if (!importResult.Succeeded || string.IsNullOrWhiteSpace(importResult.LocalConversationId))
            {
                await PostToUiAsync(() =>
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(importResult.ErrorMessage)
                        ? "导入会话失败。"
                        : importResult.ErrorMessage;
                    LoadPhase = DiscoverSessionsLoadPhase.Error;
                }).ConfigureAwait(false);
                return;
            }

            await PostToUiAsync(() => LoadPhase = DiscoverSessionsLoadPhase.ActivatingSession).ConfigureAwait(false);
            var activated = await RunOnUiContextAsync(
                    () => _navigationCoordinator.ActivateSessionAsync(importResult.LocalConversationId!, null))
                .ConfigureAwait(false);
            if (!activated)
            {
                await PostToUiAsync(() =>
                {
                    ErrorMessage = "加载会话并导入失败，请检查连接状态。";
                    LoadPhase = DiscoverSessionsLoadPhase.Error;
                }).ConfigureAwait(false);
                return;
            }

            await PostToUiAsync(() => LoadPhase = DiscoverSessionsLoadPhase.HydratingSession).ConfigureAwait(false);
            var hydrated = await RunOnUiContextAsync(
                    () => _connectionFacade.HydrateActiveConversationAsync())
                .ConfigureAwait(false);
            if (!hydrated)
            {
                await PostToUiAsync(() =>
                {
                    ErrorMessage = "导入后的会话历史加载失败，请检查 ACP 连接状态。";
                    LoadPhase = DiscoverSessionsLoadPhase.Error;
                }).ConfigureAwait(false);
                return;
            }

            await PostToUiAsync(() => LoadPhase = AgentSessions.Count == 0
                ? DiscoverSessionsLoadPhase.Empty
                : DiscoverSessionsLoadPhase.Loaded).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import session {SessionId}", session.Id);
            await PostToUiAsync(() =>
            {
                ErrorMessage = $"导入会话时出错: {ex.Message}";
                LoadPhase = DiscoverSessionsLoadPhase.Error;
            }).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _profilesViewModel.PropertyChanged -= OnProfilesViewModelPropertyChanged;
        _connectionFacade.PropertyChanged -= OnConnectionFacadePropertyChanged;
        _refreshSessionsCts?.Cancel();
        _refreshSessionsCts?.Dispose();
    }

    private void SyncConnectionPhase()
    {
        if (!string.IsNullOrWhiteSpace(_connectionFacade.ConnectionErrorMessage))
        {
            LoadPhase = DiscoverSessionsLoadPhase.Error;
            return;
        }

        if (_connectionFacade.IsInitializing)
        {
            LoadPhase = DiscoverSessionsLoadPhase.Initializing;
            return;
        }

        if (_connectionFacade.IsConnecting)
        {
            LoadPhase = DiscoverSessionsLoadPhase.Connecting;
        }
    }

    private string ResolveLoadErrorMessage(Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(_connectionFacade.ConnectionErrorMessage))
        {
            return _connectionFacade.ConnectionErrorMessage!;
        }

        return $"无法获取会话列表: {ex.Message}";
    }

    private static string ResolveAffinityBadgeText(
        ProjectAffinityResolution resolution,
        IReadOnlyList<ProjectDefinition> projects)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(projects);

        if (resolution.Source == ProjectAffinitySource.NeedsMapping)
        {
            return "Needs mapping";
        }

        if (resolution.Source == ProjectAffinitySource.Unclassified)
        {
            return "Unclassified";
        }

        var effectiveProjectId = resolution.EffectiveProjectId;
        if (string.IsNullOrWhiteSpace(effectiveProjectId))
        {
            return "Unclassified";
        }

        var projectName = projects
            .FirstOrDefault(project => string.Equals(project.ProjectId, effectiveProjectId, StringComparison.Ordinal))
            ?.Name;
        return string.IsNullOrWhiteSpace(projectName)
            ? effectiveProjectId
            : projectName;
    }

    private static string ResolveAffinityStatusText(ProjectAffinityResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        return resolution.Source switch
        {
            ProjectAffinitySource.Override => "Using local project override.",
            ProjectAffinitySource.PathMapping => "Mapped from remote path.",
            ProjectAffinitySource.DirectMatch => "Matched by local project path.",
            ProjectAffinitySource.NeedsMapping => "Remote working directory needs path mapping.",
            ProjectAffinitySource.Unclassified when string.Equals(resolution.Reason, "MissingCwd", StringComparison.Ordinal) => "Remote metadata has no usable working directory.",
            ProjectAffinitySource.Unclassified => "No matching local project.",
            _ => "No project affinity information."
        };
    }

    private async Task PostToUiAsync(Action action)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);

        await tcs.Task.ConfigureAwait(false);
    }

    private Task<T> RunOnUiContextAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (SynchronizationContext.Current == _syncContext)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(async _ =>
        {
            try
            {
                var result = await action().ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);

        return tcs.Task;
    }
}

public enum DiscoverSessionsLoadPhase
{
    Idle = 0,
    Connecting = 1,
    Initializing = 2,
    ListingSessions = 3,
    ImportingSession = 4,
    ActivatingSession = 5,
    HydratingSession = 6,
    Loaded = 7,
    Empty = 8,
    Error = 9
}

public sealed class DiscoverSessionItemViewModel
{
    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public DateTime LastModified { get; }

    public string? SessionCwd { get; }

    public string ProjectAffinityBadgeText { get; }

    public string AffinityStatusText { get; }

    public ProjectAffinitySource AffinitySource { get; }

    public bool NeedsUserAttention { get; }

    public string FormattedDate => LastModified.ToString("yyyy-MM-dd HH:mm");

    public DiscoverSessionItemViewModel(
        string id,
        string title,
        string description,
        DateTime lastModified,
        string? sessionCwd = null,
        string? projectAffinityBadgeText = null,
        string? affinityStatusText = null,
        ProjectAffinitySource affinitySource = ProjectAffinitySource.Unclassified,
        bool needsUserAttention = false)
    {
        Id = id;
        Title = title;
        Description = description;
        LastModified = lastModified;
        SessionCwd = sessionCwd;
        ProjectAffinityBadgeText = string.IsNullOrWhiteSpace(projectAffinityBadgeText)
            ? "Unclassified"
            : projectAffinityBadgeText;
        AffinityStatusText = string.IsNullOrWhiteSpace(affinityStatusText)
            ? "No project affinity information."
            : affinityStatusText;
        AffinitySource = affinitySource;
        NeedsUserAttention = needsUserAttention;
    }
}
