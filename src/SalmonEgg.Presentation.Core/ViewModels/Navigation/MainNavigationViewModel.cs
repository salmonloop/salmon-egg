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
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class MainNavigationViewModel : ObservableObject, IDisposable
{
    public const string UnclassifiedProjectId = "__unclassified__";
    private enum NavigationSelectionKind
    {
        Start,
        Session,
        Settings
    }

    private readonly ChatViewModel _chatViewModel;
    private readonly ISessionManager _sessionManager;
    private readonly AppPreferencesViewModel _preferences;
    private readonly IUiInteractionService _ui;
    private readonly IShellNavigationService _shellNavigation;
    private readonly ILogger<MainNavigationViewModel> _logger;
    private readonly INavigationPaneState _navigationState;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly SynchronizationContext _syncContext;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;
    private readonly Timer _relativeTimeTimer;
    private static readonly TimeSpan RelativeTimeRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, SessionNavItemViewModel> _sessionIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectVms = new(StringComparer.Ordinal);
    private string? _pendingProjectIdForNewSession;

    public ObservableCollection<MainNavItemViewModel> Items { get; } = new();

    public StartNavItemViewModel StartItem { get; }
    public SessionsHeaderNavItemViewModel SessionsHeaderItem { get; }

    private object? _selectedItem;
    private NavigationSelectionKind _selectionKind = NavigationSelectionKind.Start;
    private string? _selectedSessionId;

    public object? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    public bool IsSettingsSelected => _selectionKind == NavigationSelectionKind.Settings;

    public bool IsPaneOpen => _navigationState.IsPaneOpen;

    private void OnServicePaneStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsPaneOpen));
        ApplySelectionProjection();
    }

    public IAsyncRelayCommand AddProjectCommand { get; }

    public MainNavigationViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        IUiInteractionService ui,
        IShellNavigationService shellNavigation,
        ILogger<MainNavigationViewModel> logger,
        INavigationPaneState navigationState,
        IShellLayoutMetricsSink metricsSink)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _shellNavigation = shellNavigation ?? throw new ArgumentNullException(nameof(shellNavigation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
        _metricsSink = metricsSink ?? throw new ArgumentNullException(nameof(metricsSink));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);

        StartItem = new StartNavItemViewModel(_navigationState);
        SessionsHeaderItem = new SessionsHeaderNavItemViewModel(AddProjectCommand, _navigationState);

        Items.Add(StartItem);
        Items.Add(SessionsHeaderItem);

        // Show a lightweight placeholder until conversations are restored.
        var placeholderProject = CreateUnclassifiedProject();
        placeholderProject.Children.Add(CreateLoadingPlaceholder());
        Items.Add(placeholderProject);

        SelectedItem = StartItem;
        ApplySelectionProjection();

        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        _projectsChangedHandler = (_, _) => RebuildTree();
        _preferences.Projects.CollectionChanged += _projectsChangedHandler;

        _relativeTimeTimer = new Timer(
            _ => _syncContext.Post(__ => RefreshRelativeTimes(), null),
            null,
            RelativeTimeRefreshInterval,
            RelativeTimeRefreshInterval);

        _navigationState.PaneStateChanged += OnServicePaneStateChanged;
    }

    public void Dispose()
    {
        _navigationState.PaneStateChanged -= OnServicePaneStateChanged;
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        _preferences.Projects.CollectionChanged -= _projectsChangedHandler;
        _relativeTimeTimer.Dispose();

        DisposeItem(StartItem);
        DisposeItem(SessionsHeaderItem);
        foreach (var item in Items)
        {
            DisposeItem(item);
        }
        Items.Clear();
        _projectVms.Clear();
    }

    private void DisposeItem(object? item)
    {
        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void DisposeAndRemoveAt<T>(ObservableCollection<T> collection, int index)
    {
        if (index >= 0 && index < collection.Count)
        {
            var item = collection[index];
            collection.RemoveAt(index);
            DisposeItem(item);
        }
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsConversationListLoading)
            || e.PropertyName == nameof(ChatViewModel.ConversationListVersion))
        {
            RebuildTree();
        }
        else if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
        {
            // Sync selection when the current session changes externally (e.g. archiving)
            var sessionId = _chatViewModel.CurrentSessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                SelectStart();
            }
            else
            {
                SelectSession(sessionId);
            }
        }
    }

    public void SelectStart()
    {
        SetSelectionState(NavigationSelectionKind.Start);
    }

    public void SelectSettings()
    {
        SetSelectionState(NavigationSelectionKind.Settings);
    }

    public void SelectSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            SelectStart();
            return;
        }

        SetSelectionState(NavigationSelectionKind.Session, sessionId);

        if (!_sessionIndex.ContainsKey(sessionId))
        {
            RebuildTree();
        }
    }

    public void ToggleProjectExpanded(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        if (_projectIndex.TryGetValue(projectId, out var project))
        {
            project.IsExpanded = !project.IsExpanded;
        }
    }

    public Task PrepareStartForProjectAsync(string projectId)
    {
        var normalizedId = string.Equals(projectId, UnclassifiedProjectId, StringComparison.Ordinal)
            ? null
            : projectId;

        _preferences.LastSelectedProjectId = normalizedId;
        _pendingProjectIdForNewSession = normalizedId;
        _shellNavigation.NavigateToStart();
        SelectStart();
        return Task.CompletedTask;
    }

    public string? ConsumePendingProjectRootPath()
    {
        var projectId = _pendingProjectIdForNewSession;
        _pendingProjectIdForNewSession = null;

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var project = _preferences.Projects.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.Ordinal));
        if (project == null || string.IsNullOrWhiteSpace(project.RootPath))
        {
            return null;
        }

        return project.RootPath.Trim();
    }

    public async Task ShowAllSessionsForProjectAsync(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        try
        {
            var all = BuildSessionItemsForProject(projectId, limit: null);
            await _ui.ShowSessionsListDialogAsync(
                title: "会话",
                sessions: all,
                onPickSession: id =>
                {
                    SelectSession(id);
                }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Show sessions list failed");
        }
    }

    private SessionNavItemViewModel CreateLoadingPlaceholder()
    {
        return new SessionNavItemViewModel(
            sessionId: "__loading__",
            projectId: UnclassifiedProjectId,
            title: "加载中…",
            relativeTimeText: string.Empty,
            ui: _ui,
            chatViewModel: _chatViewModel,
            navigationState: _navigationState,
            isPlaceholder: true);
    }

    private async Task AddProjectAsync()
    {
        var pickedPath = await _ui.PickFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(pickedPath).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (_preferences.Projects.Any(p => string.Equals(NavTimeFormatter.NormalizePathForPrefixMatch(p.RootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.ShowInfoAsync("该项目路径已存在。").ConfigureAwait(true);
            return;
        }

        var name = System.IO.Path.GetFileName(normalized.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalized;
        }

        _preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            Name = name,
            RootPath = normalized
        });

        RebuildTree();
    }

    public void RebuildTree()
    {
        _syncContext.Post(_ =>
        {
            try
            {
                // Ensure we have the base items
                if (Items.Count < 2)
                {
                    foreach (var item in Items) DisposeItem(item);
                    Items.Clear();
                    Items.Add(StartItem);
                    Items.Add(SessionsHeaderItem);
                }

                _sessionIndex.Clear();
                _projectIndex.Clear();

                var projects = GetProjectDefinitions();
                var sessionsByProject = GetSessionsByProject(projects);

                // Index of where project items start
                int itemIndex = 2;

                foreach (var (projectDef, isSystem) in projects)
                {
                    var projectId = projectDef.ProjectId!;
                    if (!_projectVms.TryGetValue(projectId, out var projectVm))
                    {
                        projectVm = new ProjectNavItemViewModel(projectDef, isSystem, PrepareStartForProjectAsync, _navigationState)
                        {
                            IsExpanded = true
                        };
                        _projectVms[projectId] = projectVm;
                    }
                    else
                    {
                        // Update existing VM properties if they changed
                        projectVm.Title = projectDef.Name ?? string.Empty;
                    }

                    _projectIndex[projectId] = projectVm;

                    // Ensure the project VM is at the correct position in the Items collection
                    if (itemIndex < Items.Count)
                    {
                        if (!ReferenceEquals(Items[itemIndex], projectVm))
                        {
                            // If it's elsewhere, remove it first (should be rare)
                            int existingIndex = Items.IndexOf(projectVm);
                            if (existingIndex != -1)
                            {
                                // Note: We don't dispose projectVm here because it's still being used (moved)
                                Items.RemoveAt(existingIndex);
                            }
                            Items.Insert(itemIndex, projectVm);
                        }
                    }
                    else
                    {
                        Items.Add(projectVm);
                    }

                    SyncSessions(projectVm, sessionsByProject.TryGetValue(projectId, out var s) ? s : new List<Session>());
                    itemIndex++;
                }

                // Remove orphans
                while (Items.Count > itemIndex)
                {
                    DisposeAndRemoveAt(Items, Items.Count - 1);
                }

                // Cleanup _projectVms for projects that no longer exist
                var currentProjectIds = new HashSet<string>(projects.Select(p => p.Project.ProjectId!), StringComparer.Ordinal);
                var toRemove = _projectVms.Keys.Where(id => !currentProjectIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                {
                    if (_projectVms.TryGetValue(id, out var vm)) DisposeItem(vm);
                    _projectVms.Remove(id);
                }

                NormalizeSelectionAfterRebuild();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "导航树重建过程中发生异常，已拦截以防止闪退");
            }
        }, null);
    }

    private void SyncSessions(ProjectNavItemViewModel projectVm, List<Session> sessions)
    {
        var top = sessions.Take(20).ToList();
        var remainingCount = Math.Max(0, sessions.Count - top.Count);

        int childIndex = 0;
        foreach (var session in top)
        {
            var title = string.IsNullOrWhiteSpace(session.DisplayName)
                ? SessionNamePolicy.CreateDefault(session.SessionId)
                : session.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt);

            SessionNavItemViewModel? sessionVm = null;
            if (childIndex < projectVm.Children.Count && projectVm.Children[childIndex] is SessionNavItemViewModel existingSvm && !existingSvm.IsPlaceholder)
            {
                if (string.Equals(existingSvm.SessionId, session.SessionId, StringComparison.Ordinal))
                {
                    sessionVm = existingSvm;
                    sessionVm.Title = title;
                    sessionVm.RelativeTimeText = relative;
                }
            }

            if (sessionVm == null)
            {
                // Look for it elsewhere in children to avoid full re-creation if it moved
                sessionVm = projectVm.Children.OfType<SessionNavItemViewModel>().FirstOrDefault(v => string.Equals(v.SessionId, session.SessionId, StringComparison.Ordinal));
                if (sessionVm != null)
                {
                    // Note: We don't dispose here because we are re-inserting it at a new position
                    projectVm.Children.Remove(sessionVm);
                    sessionVm.Title = title;
                    sessionVm.RelativeTimeText = relative;
                }
                else
                {
                    sessionVm = new SessionNavItemViewModel(
                        sessionId: session.SessionId,
                        projectId: projectVm.ProjectId,
                        title: title,
                        relativeTimeText: relative,
                        ui: _ui,
                        chatViewModel: _chatViewModel,
                        navigationState: _navigationState);
                }
                projectVm.Children.Insert(childIndex, sessionVm);
            }

            _sessionIndex[session.SessionId] = sessionVm;
            childIndex++;
        }

        // Handle "More" item
        if (remainingCount > 0)
        {
            if (childIndex < projectVm.Children.Count && projectVm.Children[childIndex] is MoreSessionsNavItemViewModel existingMore)
            {
                existingMore.Count = remainingCount;
            }
            else
            {
                // Remove any existing More item if it's at the wrong place
                var oldMore = projectVm.Children.OfType<MoreSessionsNavItemViewModel>().FirstOrDefault();
                if (oldMore != null)
                {
                    projectVm.Children.Remove(oldMore);
                    DisposeItem(oldMore);
                }

                var showMore = new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(projectVm.ProjectId));
                projectVm.Children.Insert(childIndex, new MoreSessionsNavItemViewModel(projectVm.ProjectId, remainingCount, showMore, _navigationState));
            }
            childIndex++;
        }
        else
        {
            var oldMore = projectVm.Children.OfType<MoreSessionsNavItemViewModel>().FirstOrDefault();
            if (oldMore != null)
            {
                projectVm.Children.Remove(oldMore);
                DisposeItem(oldMore);
            }
        }

        // Add loading placeholder if needed
        if (_chatViewModel.IsConversationListLoading && childIndex == 0 && projectVm.ProjectId == UnclassifiedProjectId)
        {
            projectVm.Children.Add(CreateLoadingPlaceholder());
            childIndex++;
        }

        while (projectVm.Children.Count > childIndex)
        {
            var item = projectVm.Children[projectVm.Children.Count - 1];
            projectVm.Children.RemoveAt(projectVm.Children.Count - 1);
            DisposeItem(item);
        }

    }

    private List<(ProjectDefinition Project, bool IsSystem)> GetProjectDefinitions()
    {
        var projects = new List<(ProjectDefinition Project, bool IsSystem)>
        {
            (new ProjectDefinition { ProjectId = UnclassifiedProjectId, Name = "未归类", RootPath = string.Empty }, true)
        };

        projects.AddRange(_preferences.Projects
            .Where(p => p != null
                        && !string.IsNullOrWhiteSpace(p.ProjectId)
                        && !string.IsNullOrWhiteSpace(p.Name)
                        && !string.IsNullOrWhiteSpace(p.RootPath))
            .Select(p => (p, false)));

        return projects;
    }

    private Dictionary<string, List<Session>> GetSessionsByProject(List<(ProjectDefinition Project, bool IsSystem)> projects)
    {
        var normalizedRoots = projects.ToDictionary(
            p => p.Project.ProjectId!,
            p => NavTimeFormatter.NormalizePathForPrefixMatch(p.Project.RootPath),
            StringComparer.Ordinal);

        var sessions = _chatViewModel.GetKnownConversationIds()
            .Select(id => _sessionManager.GetSession(id))
            .Where(s => s != null)
            .Cast<Session>()
            .ToList();

        return sessions
            .GroupBy(s => ProjectSessionClassifier.ClassifyProjectId(s.Cwd, normalizedRoots, UnclassifiedProjectId), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).ToList(), StringComparer.Ordinal);
    }

    private void NormalizeSelectionAfterRebuild()
    {
        try
        {
            NormalizeSelectionState();
            ApplySelectionProjection();
        }
        catch
        {
            SetSelectionState(NavigationSelectionKind.Start);
        }
    }

    private void NormalizeSelectionState()
    {
        if (_selectionKind != NavigationSelectionKind.Session)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSessionId) || !_sessionIndex.ContainsKey(_selectedSessionId))
        {
            _selectionKind = NavigationSelectionKind.Start;
            _selectedSessionId = null;
            OnPropertyChanged(nameof(IsSettingsSelected));
        }
    }

    private void SetSelectionState(NavigationSelectionKind kind, string? sessionId = null)
    {
        var normalizedSessionId = kind == NavigationSelectionKind.Session ? sessionId : null;
        var kindChanged = _selectionKind != kind;
        var sessionChanged = !string.Equals(_selectedSessionId, normalizedSessionId, StringComparison.Ordinal);
        if (!kindChanged && !sessionChanged)
        {
            ApplySelectionProjection();
            return;
        }

        _selectionKind = kind;
        _selectedSessionId = normalizedSessionId;
        OnPropertyChanged(nameof(IsSettingsSelected));
        ApplySelectionProjection();
    }

    private void ApplySelectionProjection()
    {
        ApplyVisualSelectionState();
        SelectedItem = ResolveProjectedSelectedItem();
    }

    private void ApplyVisualSelectionState()
    {
        StartItem.IsLogicallySelected = _selectionKind == NavigationSelectionKind.Start;
        SessionsHeaderItem.IsLogicallySelected = false;

        foreach (var project in _projectVms.Values)
        {
            project.IsLogicallySelected = false;
            project.IsActiveDescendant = false;

            foreach (var child in project.Children)
            {
                child.IsLogicallySelected = false;
            }
        }

        if (_selectionKind == NavigationSelectionKind.Session &&
            !string.IsNullOrWhiteSpace(_selectedSessionId) &&
            _sessionIndex.TryGetValue(_selectedSessionId, out var sessionItem))
        {
            sessionItem.IsLogicallySelected = true;

            if (_projectIndex.TryGetValue(sessionItem.ProjectId, out var projectItem))
            {
                projectItem.IsActiveDescendant = true;
            }
        }
    }

    private object? ResolveProjectedSelectedItem()
        => _selectionKind switch
        {
            NavigationSelectionKind.Start => StartItem,
            NavigationSelectionKind.Settings => null,
            NavigationSelectionKind.Session when !string.IsNullOrWhiteSpace(_selectedSessionId)
                && _sessionIndex.TryGetValue(_selectedSessionId, out var sessionItem) => sessionItem,
            NavigationSelectionKind.Session => StartItem,
            _ => StartItem
        };


    private void RefreshRelativeTimes()
    {
        foreach (var item in Items)
        {
            RefreshRelativeTimes(item);
        }
    }

    private void RefreshRelativeTimes(MainNavItemViewModel item)
    {
        if (item is SessionNavItemViewModel sessionItem)
        {
            if (sessionItem.IsPlaceholder || string.IsNullOrWhiteSpace(sessionItem.SessionId))
            {
                return;
            }

            var session = _sessionManager.GetSession(sessionItem.SessionId);
            if (session == null)
            {
                return;
            }

            var timestamp = session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt;
            var relative = NavTimeFormatter.ToRelativeText(timestamp);
            if (!string.Equals(sessionItem.RelativeTimeText, relative, StringComparison.Ordinal))
            {
                sessionItem.RelativeTimeText = relative;
            }

            return;
        }

        foreach (var child in item.Children)
        {
            RefreshRelativeTimes(child);
        }
    }

    private IEnumerable<ProjectNavItemViewModel> BuildProjects()
    {
        var projects = GetProjectDefinitions();
        var byProject = GetSessionsByProject(projects);

        foreach (var (project, isSystem) in projects)
        {
            var projectVm = new ProjectNavItemViewModel(project, isSystem, PrepareStartForProjectAsync, _navigationState)
            {
                IsExpanded = true
            };

            var list = byProject.TryGetValue(project.ProjectId!, out var value) ? value : new List<Session>();
            var top = list.Take(20).ToList();
            var remaining = Math.Max(0, list.Count - top.Count);

            foreach (var session in top)
            {
                var title = string.IsNullOrWhiteSpace(session.DisplayName)
                    ? SessionNamePolicy.CreateDefault(session.SessionId)
                    : session.DisplayName.Trim();

                var relative = NavTimeFormatter.ToRelativeText(session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt);
                var vm = new SessionNavItemViewModel(
                    sessionId: session.SessionId,
                    projectId: projectVm.ProjectId,
                    title: title,
                    relativeTimeText: relative,
                    ui: _ui,
                    chatViewModel: _chatViewModel,
                    navigationState: _navigationState);

                projectVm.Children.Add(vm);
            }

            if (remaining > 0)
            {
                var showMore = new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(projectVm.ProjectId));
                projectVm.Children.Add(new MoreSessionsNavItemViewModel(projectVm.ProjectId, remaining, showMore, _navigationState));
            }

            if (_chatViewModel.IsConversationListLoading && projectVm.Children.Count == 0 && projectVm.ProjectId == UnclassifiedProjectId)
            {
                projectVm.Children.Add(CreateLoadingPlaceholder());
            }

            yield return projectVm;
        }
    }

    private ProjectNavItemViewModel CreateUnclassifiedProject()
    {
        var project = new ProjectDefinition
        {
            ProjectId = UnclassifiedProjectId,
            Name = "未归类",
            RootPath = string.Empty
        };
        var vm = new ProjectNavItemViewModel(project, isSystemProject: true, PrepareStartForProjectAsync, _navigationState) { IsExpanded = true };
        _projectIndex[vm.ProjectId] = vm;
        return vm;
    }

    private IReadOnlyList<SessionNavItemViewModel> BuildSessionItemsForProject(string projectId, int? limit)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Array.Empty<SessionNavItemViewModel>();
        }

        var projects = new List<ProjectDefinition>
        {
            new() { ProjectId = UnclassifiedProjectId, Name = "未归类", RootPath = string.Empty }
        };
        projects.AddRange(_preferences.Projects);

        var normalizedRoots = projects
            .Where(p => !string.IsNullOrWhiteSpace(p.ProjectId))
            .ToDictionary(p => p.ProjectId, p => NavTimeFormatter.NormalizePathForPrefixMatch(p.RootPath), StringComparer.Ordinal);

        var sessions = _chatViewModel.GetKnownConversationIds()
            .Select(id => _sessionManager.GetSession(id))
            .Where(s => s != null)
            .Cast<Session>()
            .Where(s => string.Equals(ProjectSessionClassifier.ClassifyProjectId(s.Cwd, normalizedRoots, UnclassifiedProjectId), projectId, StringComparison.Ordinal))
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        if (limit.HasValue)
        {
            sessions = sessions.Take(limit.Value).ToList();
        }

        return sessions.Select(s =>
        {
            var title = string.IsNullOrWhiteSpace(s.DisplayName) ? SessionNamePolicy.CreateDefault(s.SessionId) : s.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(s.LastActivityAt == default ? s.CreatedAt : s.LastActivityAt);
            return new SessionNavItemViewModel(
                sessionId: s.SessionId,
                projectId: projectId,
                title: title,
                relativeTimeText: relative,
                ui: _ui,
                chatViewModel: _chatViewModel,
                navigationState: _navigationState);
        }).ToList();
    }
}
