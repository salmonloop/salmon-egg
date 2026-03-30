using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class MainNavigationViewModel : ObservableObject, IDisposable
{
    public const string UnclassifiedProjectId = NavigationProjectIds.Unclassified;

    private readonly IChatSessionCatalog _chatSessionCatalogActions;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly IUiInteractionService _ui;
    private readonly IShellNavigationService _shellNavigation;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ILogger<MainNavigationViewModel> _logger;
    private readonly INavigationPaneState _navigationState;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly NavigationSelectionProjector _selectionProjector;
    private readonly IConversationCatalogReadModel _conversationCatalogPresenter;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly IShellSelectionReadModel _shellSelection;
    private readonly IShellSelectionMutationSink _shellSelectionMutations;
    private readonly SynchronizationContext _syncContext;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;
    private readonly Timer _relativeTimeTimer;
    private static readonly TimeSpan RelativeTimeRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, SessionNavItemViewModel> _sessionIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectVms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConversationCatalogItem> _conversationCatalogIndex = new(StringComparer.Ordinal);
    private string? _pendingProjectIdForNewSession;
    private int _rebuildPending;
    private int _rebuildScheduled;

    public ObservableCollection<MainNavItemViewModel> Items { get; } = new();
    public ObservableCollection<MainNavItemViewModel> FooterItems { get; } = new();

    public StartNavItemViewModel StartItem { get; }
    public DiscoverSessionsNavItemViewModel DiscoverSessionsItem { get; }
    public SessionsLabelNavItemViewModel SessionsLabelItem { get; }
    public AddProjectNavItemViewModel AddProjectItem { get; }

    private object? _selectedItem;
    private NavigationViewProjection _projection = new(
        ControlSelectedItem: null,
        IsSettingsSelected: false,
        ActiveProjectIds: new HashSet<string>(StringComparer.Ordinal),
        SelectedSessionIds: new HashSet<string>(StringComparer.Ordinal));

    public object? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    public NavigationSelectionState CurrentSelection => _shellSelection.CurrentSelection;

    public object? ProjectedControlSelectedItem => _projection.ControlSelectedItem;

    public bool IsSettingsSelected => _projection.IsSettingsSelected;

    public bool IsPaneOpen => _navigationState.IsPaneOpen;

    private void OnServicePaneStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsPaneOpen));
        ApplySelectionProjection();
    }

    public IAsyncRelayCommand AddProjectCommand { get; }

    public MainNavigationViewModel(
        IConversationCatalog conversationCatalog,
        INavigationProjectPreferences projectPreferences,
        IUiInteractionService ui,
        IShellNavigationService shellNavigation,
        INavigationCoordinator navigationCoordinator,
        ILogger<MainNavigationViewModel> logger,
        INavigationPaneState navigationState,
        IShellLayoutMetricsSink metricsSink,
        NavigationSelectionProjector selectionProjector,
        IShellSelectionReadModel shellSelection,
        IConversationCatalogReadModel conversationCatalogPresenter,
        IProjectAffinityResolver projectAffinityResolver)
    {
        _chatSessionCatalogActions = conversationCatalog as IChatSessionCatalog ?? new ChatViewModelSessionCatalogAdapter(conversationCatalog);
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _shellNavigation = shellNavigation ?? throw new ArgumentNullException(nameof(shellNavigation));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
        _metricsSink = metricsSink ?? throw new ArgumentNullException(nameof(metricsSink));
        _selectionProjector = selectionProjector ?? throw new ArgumentNullException(nameof(selectionProjector));
        _shellSelection = shellSelection ?? throw new ArgumentNullException(nameof(shellSelection));
        _shellSelectionMutations = shellSelection as IShellSelectionMutationSink
            ?? throw new ArgumentException("Shell selection read model must also support mutations.", nameof(shellSelection));
        _conversationCatalogPresenter = conversationCatalogPresenter ?? throw new ArgumentNullException(nameof(conversationCatalogPresenter));
        _projectAffinityResolver = projectAffinityResolver ?? throw new ArgumentNullException(nameof(projectAffinityResolver));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);

        StartItem = new StartNavItemViewModel(_navigationState);
        DiscoverSessionsItem = new DiscoverSessionsNavItemViewModel(_navigationState);
        SessionsLabelItem = new SessionsLabelNavItemViewModel(_navigationState);
        AddProjectItem = new AddProjectNavItemViewModel(AddProjectCommand, _navigationState);

        FooterItems.Add(DiscoverSessionsItem);

        Items.Add(StartItem);
        Items.Add(SessionsLabelItem);
        Items.Add(AddProjectItem);

        // Show a lightweight placeholder until conversations are restored.
        var placeholderProject = CreateUnclassifiedProject();
        placeholderProject.Children.Add(CreateLoadingPlaceholder());
        Items.Add(placeholderProject);

        SelectedItem = StartItem;
        ApplySelectionProjection();

        _conversationCatalogPresenter.PropertyChanged += OnConversationCatalogPresenterPropertyChanged;
        _shellSelection.PropertyChanged += OnShellSelectionPropertyChanged;
        _projectsChangedHandler = (_, _) => RebuildTree();
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.ProjectPathMappings).CollectionChanged += _projectsChangedHandler;

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
        _conversationCatalogPresenter.PropertyChanged -= OnConversationCatalogPresenterPropertyChanged;
        _shellSelection.PropertyChanged -= OnShellSelectionPropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged -= _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.ProjectPathMappings).CollectionChanged -= _projectsChangedHandler;
        _relativeTimeTimer.Dispose();

        DisposeItem(StartItem);
        DisposeItem(DiscoverSessionsItem);
        DisposeItem(SessionsLabelItem);
        DisposeItem(AddProjectItem);
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

    private void OnConversationCatalogPresenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConversationCatalogReadModel.IsConversationListLoading)
            || e.PropertyName == nameof(IConversationCatalogReadModel.ConversationListVersion))
        {
            RebuildTree();
        }
    }

    private void OnShellSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellSelectionReadModel.CurrentSelection)
            || e.PropertyName == nameof(ShellSelectionStateStore.CurrentSelection))
        {
            OnPropertyChanged(nameof(CurrentSelection));
            OnPropertyChanged(nameof(IsSettingsSelected));
            NormalizeSelectionAfterRebuild();
        }
    }

    public void SelectStart()
    {
        SetSelectionState(NavigationSelectionState.StartSelection);
    }

    public void SelectDiscoverSessions()
    {
        SetSelectionState(NavigationSelectionState.DiscoverSessionsSelection);
    }

    public void SelectSettings()
    {
        SetSelectionState(NavigationSelectionState.SettingsSelection);
    }

    public void SelectSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            SelectStart();
            return;
        }

        SetSelectionState(new NavigationSelectionState.Session(sessionId));

        if (!_sessionIndex.ContainsKey(sessionId))
        {
            RebuildTree();
        }
    }

    public string? TryGetProjectIdForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _sessionIndex.TryGetValue(sessionId, out var sessionItem)
            ? sessionItem.ProjectId
            : null;
    }

    public async Task PrepareStartForProjectAsync(string projectId)
    {
        var normalizedId = string.Equals(projectId, UnclassifiedProjectId, StringComparison.Ordinal)
            ? null
            : projectId;

        try
        {
            var navigationResult = await _shellNavigation.NavigateToStart().ConfigureAwait(true);
            if (!navigationResult.Succeeded)
            {
                return;
            }

            _projectPreferences.LastSelectedProjectId = normalizedId;
            _pendingProjectIdForNewSession = normalizedId;
            SelectStart();
        }
        catch
        {
        }
    }

    public string? ConsumePendingProjectRootPath()
    {
        var projectId = _pendingProjectIdForNewSession;
        _pendingProjectIdForNewSession = null;

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return _projectPreferences.TryGetProjectRootPath(projectId);
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
                title: string.Empty,
                sessions: all,
                onPickSession: id => ActivateSessionFromSessionsList(id, projectId)).ConfigureAwait(true);
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
            chatSessionCatalog: _chatSessionCatalogActions,
            navigationState: _navigationState,
            isPlaceholder: true);
    }

    private void ActivateSessionFromSessionsList(string sessionId, string projectId)
    {
        try
        {
            var activationTask = _navigationCoordinator.ActivateSessionAsync(sessionId, projectId);
            if (!activationTask.IsCompletedSuccessfully)
            {
                _ = ObserveSessionActivationAsync(activationTask);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session activation from sessions list failed");
        }
    }

    private async Task ObserveSessionActivationAsync(Task activationTask)
    {
        try
        {
            await activationTask.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session activation from sessions list failed");
        }
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

        if (_projectPreferences.Projects.Any(p => string.Equals(NavTimeFormatter.NormalizePathForPrefixMatch(p.RootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.ShowInfoAsync("该项目路径已存在。").ConfigureAwait(true);
            return;
        }

        var name = System.IO.Path.GetFileName(normalized.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalized;
        }

        _projectPreferences.AddProject(new ProjectDefinition
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            Name = name,
            RootPath = normalized
        });

        RebuildTree();
    }

    public void RebuildTree()
    {
        Interlocked.Exchange(ref _rebuildPending, 1);
        ScheduleRebuildTreeProcessing();
    }

    private void ScheduleRebuildTreeProcessing()
    {
        if (Interlocked.CompareExchange(ref _rebuildScheduled, 1, 0) != 0)
        {
            return;
        }

        _syncContext.Post(_ => ProcessRebuildTreeRequests(), null);
    }

    private void ProcessRebuildTreeRequests()
    {
        try
        {
            Interlocked.Exchange(ref _rebuildPending, 0);
            RebuildTreeCore();
        }
        finally
        {
            Interlocked.Exchange(ref _rebuildScheduled, 0);
            if (Volatile.Read(ref _rebuildPending) != 0)
            {
                ScheduleRebuildTreeProcessing();
            }
        }
    }

    private void RebuildTreeCore()
    {
        try
        {
            // Ensure we have the base items
            if (Items.Count < 3)
            {
                foreach (var item in Items) DisposeItem(item);
                Items.Clear();
                Items.Add(StartItem);
                Items.Add(SessionsLabelItem);
                Items.Add(AddProjectItem);
            }

            _sessionIndex.Clear();
            _projectIndex.Clear();

            var projects = GetProjectDefinitions();
            var sessionsByProject = GetSessionsByProject(projects);

            // Index of where project items start (after Start, SessionsLabel, AddProject)
            int itemIndex = 3;

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

                SyncSessions(projectVm, sessionsByProject.TryGetValue(projectId, out var s) ? s : new List<ConversationCatalogItem>());
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
                if (_projectVms.TryGetValue(id, out var vm))
                {
                    DisposeItem(vm);
                }

                _projectVms.Remove(id);
            }

            NormalizeSelectionAfterRebuild();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "导航树重建过程中发生异常，已拦截以防止闪退");
        }
    }

    private void SyncSessions(ProjectNavItemViewModel projectVm, List<ConversationCatalogItem> sessions)
    {
        var top = sessions.Take(20).ToList();
        var remainingCount = Math.Max(0, sessions.Count - top.Count);

        int childIndex = 0;
        foreach (var session in top)
        {
            var title = string.IsNullOrWhiteSpace(session.DisplayName)
                ? SessionNamePolicy.CreateDefault(session.ConversationId)
                : session.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(session.LastUpdatedAt == default ? session.CreatedAt : session.LastUpdatedAt);

            SessionNavItemViewModel? sessionVm = null;
            if (childIndex < projectVm.Children.Count && projectVm.Children[childIndex] is SessionNavItemViewModel existingSvm && !existingSvm.IsPlaceholder)
            {
                if (string.Equals(existingSvm.SessionId, session.ConversationId, StringComparison.Ordinal))
                {
                    sessionVm = existingSvm;
                    sessionVm.Title = title;
                    sessionVm.RelativeTimeText = relative;
                }
            }

            if (sessionVm == null)
            {
                // Look for it elsewhere in children to avoid full re-creation if it moved
                sessionVm = projectVm.Children.OfType<SessionNavItemViewModel>().FirstOrDefault(v => string.Equals(v.SessionId, session.ConversationId, StringComparison.Ordinal));
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
                            sessionId: session.ConversationId,
                            projectId: projectVm.ProjectId,
                            title: title,
                            relativeTimeText: relative,
                            ui: _ui,
                            chatSessionCatalog: _chatSessionCatalogActions,
                            navigationState: _navigationState);
                }
                projectVm.Children.Insert(childIndex, sessionVm);
            }

            _sessionIndex[session.ConversationId] = sessionVm;
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
        if (IsConversationListLoading && childIndex == 0 && projectVm.ProjectId == UnclassifiedProjectId)
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

        projects.AddRange(_projectPreferences.Projects
            .Where(p => p != null
                        && !string.IsNullOrWhiteSpace(p.ProjectId)
                        && !string.IsNullOrWhiteSpace(p.Name)
                        && !string.IsNullOrWhiteSpace(p.RootPath))
            .Select(p => (p, false)));

        return projects;
    }

    private Dictionary<string, List<ConversationCatalogItem>> GetSessionsByProject(List<(ProjectDefinition Project, bool IsSystem)> projects)
    {
        var sessions = GetConversationCatalogSnapshot();

        return sessions
            .GroupBy(session => ResolveEffectiveProjectId(session), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(GetNavigationSortTimestamp)
                    .ThenByDescending(s => s.LastUpdatedAt)
                    .ToList(),
                StringComparer.Ordinal);
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
            SetSelectionState(NavigationSelectionState.StartSelection);
        }
    }

    private void NormalizeSelectionState()
    {
        if (CurrentSelection is not NavigationSelectionState.Session sessionSelection)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionSelection.SessionId))
        {
            _shellSelectionMutations.SetSelection(NavigationSelectionState.StartSelection);
        }
    }

    private void SetSelectionState(NavigationSelectionState selection)
    {
        if (Equals(CurrentSelection, selection))
        {
            ApplySelectionProjection();
            return;
        }

        _shellSelectionMutations.SetSelection(selection);
    }

    private void ApplySelectionProjection()
    {
        _projection = _selectionProjector.Project(
            CurrentSelection,
            StartItem,
            DiscoverSessionsItem,
            _sessionIndex,
            _projectIndex,
            _navigationState.IsPaneOpen);

        ApplyVisualSelectionState(_projection);
        SelectedItem = _projection.ControlSelectedItem;
        OnPropertyChanged(nameof(ProjectedControlSelectedItem));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    private void ApplyVisualSelectionState(NavigationViewProjection projection)
    {
        StartItem.IsLogicallySelected = CurrentSelection is NavigationSelectionState.Start;
        DiscoverSessionsItem.IsLogicallySelected = CurrentSelection is NavigationSelectionState.DiscoverSessions;
        SessionsLabelItem.IsLogicallySelected = false;
        AddProjectItem.IsLogicallySelected = false;

        foreach (var project in _projectVms.Values)
        {
            project.IsLogicallySelected = false;
            project.IsActiveDescendant = projection.ActiveProjectIds.Contains(project.ProjectId);

            foreach (var child in project.Children)
            {
                if (child is SessionNavItemViewModel sessionItem)
                {
                    child.IsLogicallySelected = projection.SelectedSessionIds.Contains(sessionItem.SessionId);
                }
                else
                {
                    child.IsLogicallySelected = false;
                }
            }
        }
    }

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

            if (!_conversationCatalogIndex.TryGetValue(sessionItem.SessionId, out var session))
            {
                return;
            }

            var timestamp = session.LastUpdatedAt == default ? session.CreatedAt : session.LastUpdatedAt;
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

            var list = byProject.TryGetValue(project.ProjectId!, out var value) ? value : new List<ConversationCatalogItem>();
            var top = list.Take(20).ToList();
            var remaining = Math.Max(0, list.Count - top.Count);

            foreach (var session in top)
            {
                var title = string.IsNullOrWhiteSpace(session.DisplayName)
                    ? SessionNamePolicy.CreateDefault(session.ConversationId)
                    : session.DisplayName.Trim();

                var relative = NavTimeFormatter.ToRelativeText(session.LastUpdatedAt == default ? session.CreatedAt : session.LastUpdatedAt);
                var vm = new SessionNavItemViewModel(
                    sessionId: session.ConversationId,
                    projectId: projectVm.ProjectId,
                    title: title,
                    relativeTimeText: relative,
                    ui: _ui,
                    chatSessionCatalog: _chatSessionCatalogActions,
                    navigationState: _navigationState);

                projectVm.Children.Add(vm);
            }

            if (remaining > 0)
            {
                var showMore = new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(projectVm.ProjectId));
                projectVm.Children.Add(new MoreSessionsNavItemViewModel(projectVm.ProjectId, remaining, showMore, _navigationState));
            }

            if (IsConversationListLoading && projectVm.Children.Count == 0 && projectVm.ProjectId == UnclassifiedProjectId)
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

        var sessions = GetConversationCatalogSnapshot()
            .Where(s => string.Equals(ResolveEffectiveProjectId(s), projectId, StringComparison.Ordinal))
            .OrderByDescending(GetNavigationSortTimestamp)
            .ThenByDescending(s => s.LastUpdatedAt)
            .ToList();

        if (limit.HasValue)
        {
            sessions = sessions.Take(limit.Value).ToList();
        }

        return sessions.Select(s =>
        {
            var title = string.IsNullOrWhiteSpace(s.DisplayName) ? SessionNamePolicy.CreateDefault(s.ConversationId) : s.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(s.LastUpdatedAt == default ? s.CreatedAt : s.LastUpdatedAt);
            return new SessionNavItemViewModel(
                sessionId: s.ConversationId,
                projectId: projectId,
                title: title,
                relativeTimeText: relative,
                ui: _ui,
                chatSessionCatalog: _chatSessionCatalogActions,
                navigationState: _navigationState);
        }).ToList();
    }

    private bool IsConversationListLoading => _conversationCatalogPresenter.IsConversationListLoading;

    private IReadOnlyList<ConversationCatalogItem> GetConversationCatalogSnapshot()
    {
        _conversationCatalogIndex.Clear();
        foreach (var item in _conversationCatalogPresenter.Snapshot)
        {
            _conversationCatalogIndex[item.ConversationId] = item;
        }

        return _conversationCatalogPresenter.Snapshot;
    }

    private static DateTime GetNavigationSortTimestamp(ConversationCatalogItem item)
        // Keep navigation order aligned with the timestamp we actually render in the UI.
        // LastAccessedAt is still meaningful for restore/recency flows, but should not reorder
        // the left nav when the conversation content itself has not changed.
        => item.LastUpdatedAt == default ? item.CreatedAt : item.LastUpdatedAt;

    private string ResolveEffectiveProjectId(ConversationCatalogItem item)
        => ResolveProjectAffinity(item).EffectiveProjectId;

    private ProjectAffinityResolution ResolveProjectAffinity(ConversationCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: item.Cwd,
            BoundProfileId: item.BoundProfileId,
            RemoteSessionId: item.RemoteSessionId,
            OverrideProjectId: item.ProjectAffinityOverrideProjectId,
            Projects: _projectPreferences.Projects,
            PathMappings: _projectPreferences.ProjectPathMappings,
            UnclassifiedProjectId: UnclassifiedProjectId));
    }

    private IEnumerable<string> GetKnownConversationIds()
        => _conversationCatalogPresenter.Snapshot.Select(static item => item.ConversationId);
}
