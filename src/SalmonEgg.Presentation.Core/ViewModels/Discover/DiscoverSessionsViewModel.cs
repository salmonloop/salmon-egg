using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly IUiDispatcher _uiDispatcher;
    private CancellationTokenSource? _refreshSessionsCts;
    private int _refreshGeneration;
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

    public string? DisplayErrorMessage => ErrorMessage;

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

    [ObservableProperty]
    private DiscoverLayoutMode _layoutMode = DiscoverLayoutMode.Wide;

    [ObservableProperty]
    private DiscoverPaneMode _activePaneMode = DiscoverPaneMode.List;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedProfile))]
    private ServerConfiguration? _selectedProfile;

    public bool ShowProfilesPane => LayoutMode == DiscoverLayoutMode.Wide || ActivePaneMode == DiscoverPaneMode.List;

    public bool ShowDetailsPane => LayoutMode == DiscoverLayoutMode.Wide || (ActivePaneMode == DiscoverPaneMode.Detail && SelectedProfile != null);

    public bool ShowCompactBackButton => LayoutMode == DiscoverLayoutMode.Narrow && ActivePaneMode == DiscoverPaneMode.Detail;

    public bool IsListVisible => LoadPhase == DiscoverSessionsLoadPhase.Loaded;

    public bool ShowEmptyState => LoadPhase == DiscoverSessionsLoadPhase.Empty;

    public AcpProfilesViewModel ProfilesViewModel => _profilesViewModel;

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
        IUiDispatcher uiDispatcher,
        IProjectAffinityResolver? projectAffinityResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _profilesViewModel = profilesViewModel ?? throw new ArgumentNullException(nameof(profilesViewModel));
        _connectionFacade = connectionFacade ?? throw new ArgumentNullException(nameof(connectionFacade));
        _projectAffinityResolver = projectAffinityResolver ?? new ProjectAffinityResolver();
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _selectedProfile = ResolvePreferredSelectedProfile();
    }

    public void SetLayoutMode(DiscoverLayoutMode mode)
    {
        if (LayoutMode == mode)
        {
            return;
        }

        LayoutMode = mode;
        OnPropertyChanged(nameof(ShowProfilesPane));
        OnPropertyChanged(nameof(ShowDetailsPane));
        OnPropertyChanged(nameof(ShowCompactBackButton));
    }

    [RelayCommand]
    private void OpenProfileDetails()
    {
        if (LayoutMode == DiscoverLayoutMode.Narrow
            && SelectedProfile != null
            && ActivePaneMode != DiscoverPaneMode.Detail)
        {
            ActivePaneMode = DiscoverPaneMode.Detail;
            OnPropertyChanged(nameof(ShowProfilesPane));
            OnPropertyChanged(nameof(ShowDetailsPane));
            OnPropertyChanged(nameof(ShowCompactBackButton));
        }
    }

    [RelayCommand]
    private void BackToProfiles()
    {
        if (LayoutMode == DiscoverLayoutMode.Narrow
            && ActivePaneMode != DiscoverPaneMode.List)
        {
            ActivePaneMode = DiscoverPaneMode.List;
            OnPropertyChanged(nameof(ShowProfilesPane));
            OnPropertyChanged(nameof(ShowDetailsPane));
            OnPropertyChanged(nameof(ShowCompactBackButton));
        }
    }

    partial void OnSelectedProfileChanged(ServerConfiguration? value)
    {
        if (_disposed)
        {
            return;
        }

        void ApplySelectionChange()
        {
            if (value == null)
            {
                ActivePaneMode = DiscoverPaneMode.List;
            }
            else if (LayoutMode == DiscoverLayoutMode.Narrow)
            {
                ActivePaneMode = DiscoverPaneMode.Detail;
            }

            OnPropertyChanged(nameof(ShowProfilesPane));
            OnPropertyChanged(nameof(ShowDetailsPane));
            OnPropertyChanged(nameof(ShowCompactBackButton));

            _ = RefreshSessionsAsync();
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            ApplySelectionChange();
            return;
        }

        _uiDispatcher.Enqueue(ApplySelectionChange);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await _profilesViewModel.RefreshIfEmptyAsync().ConfigureAwait(false);

        _uiDispatcher.Enqueue(() =>
        {
            OnPropertyChanged(nameof(AvailableProfiles));

            var preferredSelection = ResolvePreferredSelectedProfile();
            if (!ReferenceEquals(SelectedProfile, preferredSelection))
            {
                SelectedProfile = preferredSelection;
                return;
            }

            if (SelectedProfile != null)
            {
                if (AgentSessions.Count == 0 && !IsLoading)
                {
                    _ = RefreshSessionsAsync();
                }
            }
        });
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

        var generation = Interlocked.Increment(ref _refreshGeneration);
        
        await PostToUiAsync(() =>
        {
            AgentSessions.Clear();
            ErrorMessage = null;
            LoadPhase = DiscoverSessionsLoadPhase.Connecting;
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

            if (chatService.AgentCapabilities?.SupportsSessionList != true)
            {
                throw new InvalidOperationException("当前 Agent 未声明 session/list 能力。");
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
                if (generation != Volatile.Read(ref _refreshGeneration) || profile.Id != SelectedProfile?.Id)
                {
                    return;
                }

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
            if (generation != Volatile.Read(ref _refreshGeneration) || profile.Id != SelectedProfile?.Id)
            {
                return;
            }

            _logger.LogError(ex, "Failed to load sessions for profile {ProfileId}", profile.Id);
            await PostToUiAsync(() =>
            {
                if (generation != Volatile.Read(ref _refreshGeneration) || profile.Id != SelectedProfile?.Id)
                {
                    return;
                }

                ErrorMessage = ResolveLoadErrorMessage(ex);
                LoadPhase = DiscoverSessionsLoadPhase.Error;
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task LoadSessionAsync(DiscoverSessionItemViewModel? session)
    {
        var selectedProfile = SelectedProfile;
        if (session == null || selectedProfile == null)
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

            var openResult = await RunOnUiContextAsync(
                    () => _navigationCoordinator.ActivateDiscoveredRemoteSessionAsync(
                        new DiscoverRemoteSessionOpenRequest(
                            session.Id,
                            session.SessionCwd,
                            selectedProfile.Id,
                            session.Title)))
                .ConfigureAwait(false);
            if (!openResult.Succeeded)
            {
                await PostToUiAsync(() =>
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(openResult.ErrorMessage)
                        ? "导入会话失败。"
                        : openResult.ErrorMessage;
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
        _refreshSessionsCts?.Cancel();
        _refreshSessionsCts?.Dispose();
    }

    private ServerConfiguration? ResolvePreferredSelectedProfile()
    {
        var currentSelectionId = SelectedProfile?.Id;
        if (!string.IsNullOrWhiteSpace(currentSelectionId))
        {
            var currentSelection = AvailableProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, currentSelectionId, StringComparison.Ordinal));
            if (currentSelection != null)
            {
                return currentSelection;
            }
        }

        var sharedSelectionId = _profilesViewModel.SelectedProfile?.Id;
        if (!string.IsNullOrWhiteSpace(sharedSelectionId))
        {
            var sharedSelection = AvailableProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, sharedSelectionId, StringComparison.Ordinal));
            if (sharedSelection != null)
            {
                return sharedSelection;
            }
        }

        return AvailableProfiles.FirstOrDefault();
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
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiDispatcher.Enqueue(() =>
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
        });

        await tcs.Task.ConfigureAwait(false);
    }

    private Task<T> RunOnUiContextAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_uiDispatcher.HasThreadAccess)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiDispatcher.Enqueue(async () =>
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
        });

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

public enum DiscoverLayoutMode
{
    Wide,
    Narrow
}

public enum DiscoverPaneMode
{
    List,
    Detail
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
