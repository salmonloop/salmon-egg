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
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Uno.Extensions.Reactive;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class MainNavigationViewModel : ObservableObject, IDisposable
{
    public const string UnclassifiedProjectId = NavigationProjectIds.Unclassified;

    public event EventHandler? TreeRebuilt;

    private readonly IChatSessionCatalog _chatSessionCatalogActions;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly IUiInteractionService _ui;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly IPlatformShellService _shell;
    private readonly ILogger<MainNavigationViewModel> _logger;
    private readonly INavigationPaneState _navigationState;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly INavigationSelectionProjector _selectionProjector;
    private readonly IConversationCatalogDisplayReadModel _conversationCatalogPresenter;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly IShellSelectionReadModel _shellSelection;
    private readonly IShellNavigationRuntimeState _shellRuntimeState;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;
    private readonly Timer _relativeTimeTimer;
    private static readonly TimeSpan RelativeTimeRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, SessionNavItemViewModel> _sessionIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectVms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConversationCatalogDisplayItem> _conversationCatalogIndex = new(StringComparer.Ordinal);
    private readonly ObservableCollection<MainNavItemViewModel> _items = new();
    private readonly ObservableCollection<MainNavItemViewModel> _footerItems = new();
    private readonly ReadOnlyObservableCollection<MainNavItemViewModel> _readOnlyItems;
    private readonly ReadOnlyObservableCollection<MainNavItemViewModel> _readOnlyFooterItems;
    private string? _pendingProjectIdForNewSession;
    private long _pendingProjectIntentVersion;
    private int _rebuildPending;
    private int _rebuildScheduled;
    private IReadOnlyList<MainNavItemViewModel> _menuItems = Array.Empty<MainNavItemViewModel>();
    private IReadOnlyList<MainNavItemViewModel> _footerMenuItems = Array.Empty<MainNavItemViewModel>();

    public IReadOnlyList<MainNavItemViewModel> Items => _readOnlyItems;
    public IReadOnlyList<MainNavItemViewModel> FooterItems => _readOnlyFooterItems;

    public IReadOnlyList<MainNavItemViewModel> MenuItems
    {
        get => _menuItems;
        private set => SetProperty(ref _menuItems, value);
    }

    public IReadOnlyList<MainNavItemViewModel> FooterMenuItems
    {
        get => _footerMenuItems;
        private set => SetProperty(ref _footerMenuItems, value);
    }

    public StartNavItemViewModel StartItem { get; }
    public DiscoverSessionsNavItemViewModel DiscoverSessionsItem { get; }
    public SettingsNavItemViewModel SettingsItem { get; }
    public SessionsLabelNavItemViewModel SessionsLabelItem { get; }
    public AddProjectNavItemViewModel AddProjectItem { get; }

    private NavigationViewProjection _projection = new(
        ControlSelectedItem: null,
        IsSettingsSelected: false,
        ActiveProjectIds: new HashSet<string>(StringComparer.Ordinal),
        SelectedSessionIds: new HashSet<string>(StringComparer.Ordinal));

    public NavigationSelectionState CurrentSelection => _shellSelection.CurrentSelection;

    public bool IsSettingsSelected => _projection.IsSettingsSelected;

    public bool IsPaneOpen => _navigationState.IsPaneOpen;

    public MainNavItemViewModel? ProjectedControlSelectedItem => _projection.ControlSelectedItem;

    public string? PendingProjectIdForNewSession
    {
        get => _pendingProjectIdForNewSession;
        private set => SetProperty(ref _pendingProjectIdForNewSession, value);
    }

    private void OnServicePaneStateChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Enqueue(ApplyPaneStateChanged);
    }

    private void ApplyPaneStateChanged()
    {
        OnPropertyChanged(nameof(IsPaneOpen));
        _logger.LogDebug(
            "Pane state changed IsPaneOpen={IsPaneOpen} CurrentSelection={CurrentSelection}",
            _navigationState.IsPaneOpen,
            CurrentSelection);
    }

    public IAsyncRelayCommand AddProjectCommand { get; }

    public MainNavigationViewModel(
        IConversationCatalog conversationCatalog,
        INavigationProjectPreferences projectPreferences,
        IUiInteractionService ui,
        INavigationCoordinator navigationCoordinator,
        ILogger<MainNavigationViewModel> logger,
        INavigationPaneState navigationState,
        IShellLayoutMetricsSink metricsSink,
        INavigationSelectionProjector selectionProjector,
        IShellSelectionReadModel shellSelection,
        IShellNavigationRuntimeState shellRuntimeState,
        IConversationCatalogReadModel conversationCatalogPresenter,
        IProjectAffinityResolver projectAffinityResolver,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        IPlatformShellService? shell = null)
        : this(
            conversationCatalog,
            projectPreferences,
            ui,
            navigationCoordinator,
            logger,
            navigationState,
            metricsSink,
            selectionProjector,
            shellSelection,
            shellRuntimeState,
            new ConversationCatalogDisplayPresenter(
                conversationCatalogPresenter,
                NoOpConversationAttentionStore.Instance,
                uiDispatcher),
            projectAffinityResolver,
            uiDispatcher,
            localizer,
            shell)
    {
    }

    public MainNavigationViewModel(
        IConversationCatalog conversationCatalog,
        INavigationProjectPreferences projectPreferences,
        IUiInteractionService ui,
        INavigationCoordinator navigationCoordinator,
        ILogger<MainNavigationViewModel> logger,
        INavigationPaneState navigationState,
        IShellLayoutMetricsSink metricsSink,
        INavigationSelectionProjector selectionProjector,
        IShellSelectionReadModel shellSelection,
        IShellNavigationRuntimeState shellRuntimeState,
        IConversationCatalogDisplayReadModel conversationCatalogPresenter,
        IProjectAffinityResolver projectAffinityResolver,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        IPlatformShellService? shell = null)
    {
        _chatSessionCatalogActions = conversationCatalog as IChatSessionCatalog ?? new ChatViewModelSessionCatalogAdapter(conversationCatalog);
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _shell = shell ?? NoOpPlatformShellService.Instance;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
        _metricsSink = metricsSink ?? throw new ArgumentNullException(nameof(metricsSink));
        _selectionProjector = selectionProjector ?? throw new ArgumentNullException(nameof(selectionProjector));
        _shellSelection = shellSelection ?? throw new ArgumentNullException(nameof(shellSelection));
        _shellRuntimeState = shellRuntimeState ?? throw new ArgumentNullException(nameof(shellRuntimeState));
        _conversationCatalogPresenter = conversationCatalogPresenter ?? throw new ArgumentNullException(nameof(conversationCatalogPresenter));
        _projectAffinityResolver = projectAffinityResolver ?? throw new ArgumentNullException(nameof(projectAffinityResolver));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _readOnlyItems = new ReadOnlyObservableCollection<MainNavItemViewModel>(_items);
        _readOnlyFooterItems = new ReadOnlyObservableCollection<MainNavItemViewModel>(_footerItems);
        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);

        StartItem = new StartNavItemViewModel(_navigationState, _uiDispatcher);
        DiscoverSessionsItem = new DiscoverSessionsNavItemViewModel(_navigationState, _uiDispatcher);
        SettingsItem = new SettingsNavItemViewModel(localizer["Nav_Settings"], _navigationState, _uiDispatcher);
        SessionsLabelItem = new SessionsLabelNavItemViewModel(_navigationState, _uiDispatcher, localizer["Nav_Sessions"]);
        AddProjectItem = new AddProjectNavItemViewModel(AddProjectCommand, _navigationState, _uiDispatcher);

        _footerItems.Add(DiscoverSessionsItem);
        _footerItems.Add(SettingsItem);

        _items.Add(StartItem);
        _items.Add(SessionsLabelItem);
        _items.Add(AddProjectItem);

        // Show a lightweight placeholder until conversations are restored.
        var placeholderProject = CreateUnclassifiedProject();
        placeholderProject.MutableChildren.Add(CreateLoadingPlaceholder());
        placeholderProject.PublishChildrenMenuSnapshot();
        _items.Add(placeholderProject);
        PublishMenuSnapshots();

        ApplySelectionProjection();

        _conversationCatalogPresenter.PropertyChanged += OnConversationCatalogPresenterPropertyChanged;
        _shellSelection.PropertyChanged += OnShellSelectionPropertyChanged;
        _shellRuntimeState.PropertyChanged += OnShellRuntimeStatePropertyChanged;
        _projectsChangedHandler = (_, _) => RebuildTree();
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.ProjectPathMappings).CollectionChanged += _projectsChangedHandler;

        _relativeTimeTimer = new Timer(
            _ => _uiDispatcher.Enqueue(RefreshRelativeTimes),
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
        _shellRuntimeState.PropertyChanged -= OnShellRuntimeStatePropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged -= _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.ProjectPathMappings).CollectionChanged -= _projectsChangedHandler;
        _relativeTimeTimer.Dispose();

        var itemsToDispose = _items.Concat(_footerItems).ToArray();
        _items.Clear();
        _footerItems.Clear();
        _projectVms.Clear();
        PublishMenuSnapshots();

        foreach (var item in itemsToDispose)
        {
            DisposeItem(item);
        }
    }

    private void DisposeItem(object? item)
    {
        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnConversationCatalogPresenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConversationCatalogDisplayReadModel.IsConversationListLoading)
            || e.PropertyName == nameof(IConversationCatalogDisplayReadModel.ConversationListVersion)
            || e.PropertyName == nameof(IConversationCatalogDisplayReadModel.Snapshot))
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
            return;
        }

    }

    private void OnShellRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellNavigationRuntimeState.ActiveSessionActivation))
        {
            ApplySelectionProjection();
        }
    }

    public void RefreshSelectionProjection()
    {
        ApplySelectionProjection();
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
        var intentVersion = Interlocked.Increment(ref _pendingProjectIntentVersion);
        var requestedProjectId = NormalizeNewSessionProjectIntent(projectId);
        var coordinatorProjectId = string.Equals(requestedProjectId, UnclassifiedProjectId, StringComparison.Ordinal)
            ? null
            : requestedProjectId;

        try
        {
            var activated = await _navigationCoordinator.ActivateStartAsync(coordinatorProjectId).ConfigureAwait(true);
            if (!IsLatestPendingProjectIntent(intentVersion))
            {
                return;
            }

            if (!activated)
            {
                PendingProjectIdForNewSession = null;
                return;
            }

            PendingProjectIdForNewSession = requestedProjectId;
        }
        catch
        {
            if (IsLatestPendingProjectIntent(intentVersion))
            {
                PendingProjectIdForNewSession = null;
            }
        }
    }

    public void ClearPendingProjectForNewSession()
    {
        PendingProjectIdForNewSession = null;
    }

    public string? ConsumePendingProjectRootPath()
    {
        var projectId = PendingProjectIdForNewSession;
        PendingProjectIdForNewSession = null;

        if (string.IsNullOrWhiteSpace(projectId)
            || string.Equals(projectId, UnclassifiedProjectId, StringComparison.Ordinal))
        {
            return null;
        }

        return _projectPreferences.TryGetProjectRootPath(projectId);
    }

    public string? PeekPendingProjectIdForNewSession() => PendingProjectIdForNewSession;

    private static string NormalizeNewSessionProjectIntent(string? projectId)
        => string.IsNullOrWhiteSpace(projectId)
            ? UnclassifiedProjectId
            : string.Equals(projectId, UnclassifiedProjectId, StringComparison.Ordinal)
                ? UnclassifiedProjectId
                : projectId;

    private bool IsLatestPendingProjectIntent(long intentVersion)
        => Interlocked.Read(ref _pendingProjectIntentVersion) == intentVersion;

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
            remoteSessionId: null,
            projectId: UnclassifiedProjectId,
            title: "加载中…",
            relativeTimeText: string.Empty,
            ui: _ui,
            shell: _shell,
            chatSessionCatalog: _chatSessionCatalogActions,
            navigationState: _navigationState,
            uiDispatcher: _uiDispatcher,
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

        _uiDispatcher.Enqueue(ProcessRebuildTreeRequests);
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
            if (_items.Count < 3)
            {
                foreach (var item in _items) DisposeItem(item);
                _items.Clear();
                _items.Add(StartItem);
                _items.Add(SessionsLabelItem);
                _items.Add(AddProjectItem);
            }

            // Build the new indexes in local scope first, then swap atomically.
            // Clearing _sessionIndex/_projectIndex upfront would create a window
            // where ApplySelectionProjection (triggered by async callbacks) sees
            // an empty index and projects ControlSelectedItem to null. That null
            // pushed through the binding causes NavigationView to lose its
            // IsChildSelected ancestor visual during display-mode transitions.
            var newSessionIndex = new Dictionary<string, SessionNavItemViewModel>(StringComparer.Ordinal);
            var newProjectIndex = new Dictionary<string, ProjectNavItemViewModel>(StringComparer.Ordinal);

            var projects = GetProjectDefinitions();
            var sessionsByProject = GetSessionsByProject(projects);
            var removedItemsToDispose = new List<MainNavItemViewModel>();
#if DEBUG
            if (CurrentSelection is NavigationSelectionState.Session selectedSession
                && !string.IsNullOrWhiteSpace(selectedSession.SessionId))
            {
                var currentCatalogItem = _conversationCatalogPresenter.Snapshot
                    .FirstOrDefault(item => string.Equals(item.ConversationId, selectedSession.SessionId, StringComparison.Ordinal));
                _logger.LogInformation(
                    "Navigation rebuild evaluating selected session. SessionId={SessionId} CatalogCwd={CatalogCwd} BoundProfileId={BoundProfileId} RemoteSessionId={RemoteSessionId} SnapshotCount={SnapshotCount}",
                    selectedSession.SessionId,
                    currentCatalogItem?.Cwd,
                    currentCatalogItem?.BoundProfileId,
                    currentCatalogItem?.RemoteSessionId,
                    _conversationCatalogPresenter.Snapshot.Count);
            }
#endif

            // Index of where project items start (after Start, SessionsLabel, AddProject)
            int itemIndex = 3;

            foreach (var (projectDef, isSystem) in projects)
            {
                var projectId = projectDef.ProjectId!;
                if (!_projectVms.TryGetValue(projectId, out var projectVm))
                {
                    projectVm = new ProjectNavItemViewModel(projectDef, isSystem, PrepareStartForProjectAsync, _navigationState, _uiDispatcher)
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

                newProjectIndex[projectId] = projectVm;

                // Ensure the project VM is at the correct position in the Items collection
                if (itemIndex < _items.Count)
                {
                    if (!ReferenceEquals(_items[itemIndex], projectVm))
                    {
                        // If it's elsewhere, remove it first (should be rare)
                        int existingIndex = _items.IndexOf(projectVm);
                        if (existingIndex != -1)
                        {
                            // Note: We don't dispose projectVm here because it's still being used (moved)
                            _items.RemoveAt(existingIndex);
                        }

                        _items.Insert(itemIndex, projectVm);
                    }
                }
                else
                {
                    _items.Add(projectVm);
                }

                SyncSessions(projectVm, sessionsByProject.TryGetValue(projectId, out var s) ? s : new List<ConversationCatalogDisplayItem>(), newSessionIndex);
                itemIndex++;
            }

            // Remove orphans
            while (_items.Count > itemIndex)
            {
                var removedItem = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                removedItemsToDispose.Add(removedItem);
            }

            // Cleanup _projectVms for projects that no longer exist
            var currentProjectIds = new HashSet<string>(projects.Select(p => p.Project.ProjectId!), StringComparer.Ordinal);
            var toRemove = _projectVms.Keys.Where(id => !currentProjectIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_projectVms.TryGetValue(id, out var vm))
                {
                    if (!removedItemsToDispose.Contains(vm))
                    {
                        removedItemsToDispose.Add(vm);
                    }
                }

                _projectVms.Remove(id);
            }

            // Atomic swap: replace shared indexes only after the new tree is fully built.
            _sessionIndex.Clear();
            foreach (var kvp in newSessionIndex)
            {
                _sessionIndex[kvp.Key] = kvp.Value;
            }

            _projectIndex.Clear();
            foreach (var kvp in newProjectIndex)
            {
                _projectIndex[kvp.Key] = kvp.Value;
            }

            PublishMenuSnapshots();
            foreach (var item in removedItemsToDispose)
            {
                DisposeItem(item);
            }

            NormalizeSelectionAfterRebuild();

            // Notify that tree has been rebuilt
            TreeRebuilt?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "导航树重建过程中发生异常，已拦截以防止闪退");
        }
    }

    private void SyncSessions(ProjectNavItemViewModel projectVm, List<ConversationCatalogDisplayItem> sessions, Dictionary<string, SessionNavItemViewModel> targetSessionIndex)
    {
        var top = sessions.Take(20).ToList();
        var remainingCount = Math.Max(0, sessions.Count - top.Count);
        var children = projectVm.MutableChildren;

        int childIndex = 0;
        foreach (var session in top)
        {
            var title = string.IsNullOrWhiteSpace(session.DisplayName)
                ? SessionNamePolicy.CreateDefault(session.ConversationId)
                : session.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(session.CatalogUpdatedAt == default ? session.CreatedAt : session.CatalogUpdatedAt);

            SessionNavItemViewModel? sessionVm = null;

            if (childIndex < children.Count && children[childIndex] is SessionNavItemViewModel existingSvm && !existingSvm.IsPlaceholder)
            {
                if (string.Equals(existingSvm.SessionId, session.ConversationId, StringComparison.Ordinal))
                {
                    sessionVm = existingSvm;
                    sessionVm.Title = title;
                    sessionVm.RemoteSessionId = session.RemoteSessionId;
                    sessionVm.RelativeTimeText = relative;
                    sessionVm.HasUnreadAttention = session.HasUnreadAttention;
                }
            }

            if (sessionVm == null)
            {
                // Look for it elsewhere in children to avoid full re-creation if it moved
                sessionVm = children.OfType<SessionNavItemViewModel>().FirstOrDefault(v => string.Equals(v.SessionId, session.ConversationId, StringComparison.Ordinal));
                if (sessionVm != null)
                {
                    // Note: We don't dispose here because we are re-inserting it at a new position
                    children.Remove(sessionVm);
                    sessionVm.Title = title;
                    sessionVm.RemoteSessionId = session.RemoteSessionId;
                    sessionVm.RelativeTimeText = relative;
                    sessionVm.HasUnreadAttention = session.HasUnreadAttention;
                }
                else
                {
                    sessionVm = new SessionNavItemViewModel(
                            sessionId: session.ConversationId,
                            remoteSessionId: session.RemoteSessionId,
                            projectId: projectVm.ProjectId,
                            title: title,
                            relativeTimeText: relative,
                            ui: _ui,
                            shell: _shell,
                            chatSessionCatalog: _chatSessionCatalogActions,
                            navigationState: _navigationState,
                            uiDispatcher: _uiDispatcher);
                    sessionVm.HasUnreadAttention = session.HasUnreadAttention;
                }
                children.Insert(childIndex, sessionVm);
            }

            targetSessionIndex[session.ConversationId] = sessionVm;
            childIndex++;
        }

        // Handle "More" item
        if (remainingCount > 0)
        {
            if (childIndex < children.Count && children[childIndex] is MoreSessionsNavItemViewModel existingMore)
            {
                existingMore.Count = remainingCount;
            }
            else
            {
                // Remove any existing More item if it's at the wrong place
                var oldMore = children.OfType<MoreSessionsNavItemViewModel>().FirstOrDefault();
                if (oldMore != null)
                {
                    children.Remove(oldMore);
                    DisposeItem(oldMore);
                }

                var showMore = new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(projectVm.ProjectId));
                children.Insert(childIndex, new MoreSessionsNavItemViewModel(projectVm.ProjectId, remainingCount, showMore, _navigationState, _uiDispatcher));
            }
            childIndex++;
        }
        else
        {
            var oldMore = children.OfType<MoreSessionsNavItemViewModel>().FirstOrDefault();
            if (oldMore != null)
            {
                children.Remove(oldMore);
                DisposeItem(oldMore);
            }
        }

        // Add loading placeholder if needed
        if (IsConversationListLoading && childIndex == 0 && projectVm.ProjectId == UnclassifiedProjectId)
        {
            children.Add(CreateLoadingPlaceholder());
            childIndex++;
        }

        while (children.Count > childIndex)
        {
            var item = children[children.Count - 1];
            children.RemoveAt(children.Count - 1);
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

    private Dictionary<string, List<ConversationCatalogDisplayItem>> GetSessionsByProject(List<(ProjectDefinition Project, bool IsSystem)> projects)
    {
        var sessions = GetConversationCatalogSnapshot();

        return sessions
            .GroupBy(session => ResolveEffectiveProjectId(session), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(GetNavigationSortTimestamp)
                    .ThenByDescending(s => s.CatalogUpdatedAt)
                    .ToList(),
                StringComparer.Ordinal);
    }

    private void NormalizeSelectionAfterRebuild()
    {
        try
        {
            ApplySelectionProjection();
        }
        catch
        {
            _projection = _selectionProjector.Project(
                NavigationSelectionState.StartSelection,
                StartItem,
                DiscoverSessionsItem,
                SettingsItem,
                _sessionIndex,
                _projectIndex);
            ApplyVisualSelectionState(_projection);
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(ProjectedControlSelectedItem));
        }
    }

    private void ApplySelectionProjection()
    {
        var previousProjection = _projection;
        var projectedSelection = GetProjectedSelectionState();

        if (projectedSelection is NavigationSelectionState.Session selectionState
            && !string.IsNullOrWhiteSpace(selectionState.SessionId)
            && _sessionIndex.TryGetValue(selectionState.SessionId, out var sessionItem))
        {
            _logger.LogDebug(
                "SelectionProjection sessionId={SessionId} projectId={ProjectId} paneOpen={PaneOpen} projectIndexHas={ProjectIndexHas} semantic={SemanticSelection} previewActive={PreviewActive}",
                selectionState.SessionId,
                sessionItem.ProjectId,
                _navigationState.IsPaneOpen,
                _projectIndex.ContainsKey(sessionItem.ProjectId),
                CurrentSelection,
                _shellRuntimeState.ActiveSessionActivation is not null);
        }

        var nextProjection = _selectionProjector.Project(
            projectedSelection,
            StartItem,
            DiscoverSessionsItem,
            SettingsItem,
            _sessionIndex,
            _projectIndex);

        _projection = nextProjection;
        ApplyVisualSelectionState(_projection);
        if (previousProjection.IsSettingsSelected != _projection.IsSettingsSelected)
        {
            OnPropertyChanged(nameof(IsSettingsSelected));
        }

        if (!ReferenceEquals(previousProjection.ControlSelectedItem, _projection.ControlSelectedItem))
        {
            OnPropertyChanged(nameof(ProjectedControlSelectedItem));
        }
    }

    private NavigationSelectionState GetProjectedSelectionState()
    {
        if (CurrentSelection is NavigationSelectionState.Session currentSession
            && (string.IsNullOrWhiteSpace(currentSession.SessionId)
                || !_sessionIndex.ContainsKey(currentSession.SessionId)))
        {
            return NavigationSelectionState.StartSelection;
        }

        return CurrentSelection;
    }
    private void ApplyVisualSelectionState(NavigationViewProjection projection)
    {
        // Visual selection state is now handled by NavigationView's native projection behavior
        // We only need to maintain the logical state for our internal logic
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
        foreach (var item in _items)
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

            var timestamp = session.CatalogUpdatedAt == default ? session.CreatedAt : session.CatalogUpdatedAt;
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

    private void PublishMenuSnapshots()
    {
        foreach (var project in _projectVms.Values)
        {
            project.PublishChildrenMenuSnapshot();
        }

        MenuItems = _items.ToArray();
        FooterMenuItems = _footerItems.ToArray();
    }

    private ProjectNavItemViewModel CreateUnclassifiedProject()
    {
        var project = new ProjectDefinition
        {
            ProjectId = UnclassifiedProjectId,
            Name = "未归类",
            RootPath = string.Empty
        };
        var vm = new ProjectNavItemViewModel(project, isSystemProject: true, PrepareStartForProjectAsync, _navigationState, _uiDispatcher) { IsExpanded = true };
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
            .ThenByDescending(s => s.CatalogUpdatedAt)
            .ToList();

        if (limit.HasValue)
        {
            sessions = sessions.Take(limit.Value).ToList();
        }

        return sessions.Select(s =>
        {
            var title = string.IsNullOrWhiteSpace(s.DisplayName) ? SessionNamePolicy.CreateDefault(s.ConversationId) : s.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(s.CatalogUpdatedAt == default ? s.CreatedAt : s.CatalogUpdatedAt);
            var vm = new SessionNavItemViewModel(
                sessionId: s.ConversationId,
                remoteSessionId: s.RemoteSessionId,
                projectId: projectId,
                title: title,
                relativeTimeText: relative,
                ui: _ui,
                shell: _shell,
                chatSessionCatalog: _chatSessionCatalogActions,
                navigationState: _navigationState,
                uiDispatcher: _uiDispatcher);
            vm.HasUnreadAttention = s.HasUnreadAttention;
            return vm;
        }).ToList();
    }

    private bool IsConversationListLoading => _conversationCatalogPresenter.IsConversationListLoading;

    private IReadOnlyList<ConversationCatalogDisplayItem> GetConversationCatalogSnapshot()
    {
        _conversationCatalogIndex.Clear();
        foreach (var item in _conversationCatalogPresenter.Snapshot)
        {
            _conversationCatalogIndex[item.ConversationId] = item;
        }

        return _conversationCatalogPresenter.Snapshot;
    }

    private static DateTime GetNavigationSortTimestamp(ConversationCatalogDisplayItem item)
        // Keep navigation order aligned with the timestamp we actually render in the UI.
        // LastAccessedAt is still meaningful for restore/recency flows, but should not reorder
        // the left nav when the conversation content itself has not changed.
        => item.CatalogUpdatedAt == default ? item.CreatedAt : item.CatalogUpdatedAt;

    private string ResolveEffectiveProjectId(ConversationCatalogDisplayItem item)
        => ResolveProjectAffinity(item).EffectiveProjectId;

    private ProjectAffinityResolution ResolveProjectAffinity(ConversationCatalogDisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var resolution = _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: item.Cwd,
            BoundProfileId: item.BoundProfileId,
            RemoteSessionId: item.RemoteSessionId,
            OverrideProjectId: item.ProjectAffinityOverrideProjectId,
            Projects: _projectPreferences.Projects,
            PathMappings: _projectPreferences.ProjectPathMappings,
            UnclassifiedProjectId: UnclassifiedProjectId));
#if DEBUG
        _logger.LogDebug(
            "Project affinity resolved. ConversationId={ConversationId} Cwd={Cwd} RemoteSessionId={RemoteSessionId} BoundProfileId={BoundProfileId} OverrideProjectId={OverrideProjectId} EffectiveProjectId={EffectiveProjectId} Source={Source}",
            item.ConversationId,
            item.Cwd,
            item.RemoteSessionId,
            item.BoundProfileId,
            item.ProjectAffinityOverrideProjectId,
            resolution.EffectiveProjectId,
            resolution.Source);
#endif
        return resolution;
    }

    private IEnumerable<string> GetKnownConversationIds()
        => _conversationCatalogPresenter.Snapshot.Select(static item => item.ConversationId);

    private sealed class NoOpConversationAttentionStore : IConversationAttentionStore
    {
        private static readonly IState<ConversationAttentionState> EmptyState =
            Uno.Extensions.Reactive.State.Value(new object(), static () => ConversationAttentionState.Empty);

        public static NoOpConversationAttentionStore Instance { get; } = new();

        public IState<ConversationAttentionState> State => EmptyState;

        public ValueTask Dispatch(ConversationAttentionAction action) => ValueTask.CompletedTask;

        public ValueTask<ConversationAttentionState> GetCurrentStateAsync()
            => ValueTask.FromResult(ConversationAttentionState.Empty);
    }
}
