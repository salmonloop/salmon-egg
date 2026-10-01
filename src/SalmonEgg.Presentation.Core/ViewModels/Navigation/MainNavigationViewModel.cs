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
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class MainNavigationViewModel : ObservableObject, IDisposable, IConversationActivationEntryPoint
{
    public const string UnclassifiedProjectId = NavigationProjectIds.Unclassified;
    private const int VisibleSessionsPerProjectLimit = 20;
    private const int PinnedMenuItemCount = 3;
    private static readonly ConversationStatusGroup[] StatusGroupOrder =
        [ConversationStatusGroup.NeedsAttention, ConversationStatusGroup.Working, ConversationStatusGroup.Other];

    public event EventHandler? TreeRebuilt;

    private readonly IChatSessionCatalog _chatSessionCatalogActions;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly IUiInteractionService _ui;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly IAddProjectCoordinator _addProjectCoordinator;
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
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly IAppLanguageService? _languageService;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;
    private readonly Timer _relativeTimeTimer;
    private static readonly TimeSpan RelativeTimeRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, SessionNavItemViewModel> _sessionIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionNavItemViewModel> _sessionVms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectVms = new(StringComparer.Ordinal);
    private readonly Dictionary<ConversationStatusGroup, StatusGroupNavItemViewModel> _statusGroupVms = new();
    private readonly Dictionary<ConversationStatusGroup, int> _statusGroupVisibleLimits = new();
    // Applied membership includes paginated rows so a temporary interaction hold keeps counts and
    // visible rows in the same projection. The catalog remains authoritative after the hold ends.
    private readonly Dictionary<string, ConversationStatusGroup> _appliedStatusMembership = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConversationCatalogDisplayItem> _conversationCatalogIndex = new(StringComparer.Ordinal);
    private string _appliedGrouping;
    private string? _focusedConversationId;
    private bool _transitionsFrozen;
    private bool _isDisposed;
    private string? _pendingProjectIdForNewSession;
    private long _pendingProjectIntentVersion;
    private int _rebuildPending;
    private int _rebuildScheduled;

    // Structural revision of the menu source the control renders, bumped by every add/move/remove on
    // Items or on a project's Children. ApplySelectionProjection compares it against the revision it
    // last published so a selection that survives a reshuffle is still re-pushed once; see the publish
    // decision there for why the projected instance alone is not a sufficient key.
    private long _navTreeStructureRevision;
    private long _publishedNavTreeStructureRevision = -1;

    public ObservableCollection<MainNavItemViewModel> Items { get; } = new();
    public ObservableCollection<MainNavItemViewModel> FooterItems { get; } = new();

    public StartNavItemViewModel StartItem { get; }
    public DiscoverSessionsNavItemViewModel DiscoverSessionsItem { get; }
    public SettingsNavItemViewModel SettingsItem { get; }
    public SessionsLabelNavItemViewModel SessionsLabelItem { get; }
    public AddProjectNavItemViewModel AddProjectItem { get; }

    private NavigationViewProjection _projection = new(
        ControlSelectedItem: null,
        IsSettingsSelected: false);

    public NavigationSelectionState CurrentSelection => _shellSelection.CurrentSelection;

    public bool IsSettingsSelected => _projection.IsSettingsSelected;

    public bool IsPaneOpen => _navigationState.IsPaneOpen;

    public MainNavItemViewModel? ProjectedControlSelectedItem => _projection.ControlSelectedItem;

    public bool CanAddProject => _ui.CanPickFolder;

    public bool IsStatusGrouping => _appliedGrouping == AppSettingValueCatalog.StatusConversationGrouping;

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

    // Two explicit source intents behind the single "添加项目" entry. Both flow through the
    // unified AddProjectCoordinator so dedup, identity and persistence stay in one place.
    public IAsyncRelayCommand AddLocalProjectCommand { get; }

    public IAsyncRelayCommand SelectRemoteProjectCommand { get; }

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
        IPlatformShellService? shell = null,
        IAppLanguageService? languageService = null,
        IAddProjectCoordinator? addProjectCoordinator = null)
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
            shell,
            languageService,
            addProjectCoordinator)
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
        IPlatformShellService? shell = null,
        IAppLanguageService? languageService = null,
        IAddProjectCoordinator? addProjectCoordinator = null)
    {
        _chatSessionCatalogActions = conversationCatalog as IChatSessionCatalog ?? new ChatViewModelSessionCatalogAdapter(conversationCatalog);
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _appliedGrouping = AppSettingValueCatalog.NormalizeSidebarConversationGrouping(projectPreferences.SidebarConversationGrouping);
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
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _languageService = languageService;
        // The coordinator is the single owner of add/dedup/persist. Fall back to a locally
        // constructed instance over the same authoritative preferences when one is not injected
        // (keeps the many existing test construction sites working without a second state owner).
        _addProjectCoordinator = addProjectCoordinator
            ?? new AddProjectCoordinator(
                _projectPreferences,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AddProjectCoordinator>.Instance);
        AddLocalProjectCommand = new AsyncRelayCommand(AddLocalProjectAsync, () => CanAddProject);
        SelectRemoteProjectCommand = new AsyncRelayCommand(SelectRemoteProjectAsync);

        StartItem = new StartNavItemViewModel(_navigationState, _uiDispatcher, Localize("Nav_Start", "Start"));
        DiscoverSessionsItem = new DiscoverSessionsNavItemViewModel(_navigationState, _uiDispatcher, Localize("Nav_DiscoverSessions", "Discover sessions"));
        SettingsItem = new SettingsNavItemViewModel(Localize("Nav_Settings", "Settings"), _navigationState, _uiDispatcher);
        SessionsLabelItem = new SessionsLabelNavItemViewModel(_navigationState, _uiDispatcher, Localize("Nav_Sessions", "Sessions"));
        AddProjectItem = new AddProjectNavItemViewModel(AddLocalProjectCommand, SelectRemoteProjectCommand, _navigationState, _uiDispatcher);

        FooterItems.Add(DiscoverSessionsItem);
        FooterItems.Add(SettingsItem);

        Items.CollectionChanged += OnNavTreeStructureChanged;
        Items.Add(StartItem);
        Items.Add(SessionsLabelItem);
        Items.Add(AddProjectItem);

        // Show a lightweight placeholder until conversations are restored.
        if (IsStatusGrouping)
        {
            foreach (var group in StatusGroupOrder)
            {
                Items.Add(GetOrCreateStatusGroup(group));
            }

            _statusGroupVms[ConversationStatusGroup.Other].Children.Add(CreateLoadingPlaceholder());
        }
        else
        {
            var placeholderProject = CreateUnclassifiedProject();
            placeholderProject.Children.Add(CreateLoadingPlaceholder());
            Items.Add(placeholderProject);
        }

        ApplySelectionProjection();

        _conversationCatalogPresenter.PropertyChanged += OnConversationCatalogPresenterPropertyChanged;
        _shellSelection.PropertyChanged += OnShellSelectionPropertyChanged;
        _shellRuntimeState.PropertyChanged += OnShellRuntimeStatePropertyChanged;
        _projectsChangedHandler = (_, _) => RebuildTree();
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.AgentRemoteDirectories).CollectionChanged += _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.NavigationRemoteDirectoryIds).CollectionChanged += _projectsChangedHandler;
        _projectPreferences.PropertyChanged += OnProjectPreferencesPropertyChanged;

        _relativeTimeTimer = new Timer(
            _ => _uiDispatcher.Enqueue(RefreshRelativeTimes),
            null,
            RelativeTimeRefreshInterval,
            RelativeTimeRefreshInterval);

        _navigationState.PaneStateChanged += OnServicePaneStateChanged;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
    }

    public void RefreshLocalizedText()
    {
        _uiDispatcher.Enqueue(ApplyLocalizedText);
    }

    private void ApplyLocalizedText()
    {
        StartItem.UpdateTitle(Localize("Nav_Start", "Start"));
        DiscoverSessionsItem.UpdateTitle(Localize("Nav_DiscoverSessions", "Discover sessions"));
        SettingsItem.UpdateTitle(Localize("Nav_Settings", "Settings"));
        SessionsLabelItem.UpdateTitle(Localize("Nav_Sessions", "Sessions"));

        var unclassifiedTitle = Localize("Nav_Unclassified", "Unclassified");
        foreach (var project in _projectVms.Values
                     .Where(project => string.Equals(
                         project.ProjectId,
                         UnclassifiedProjectId,
                         StringComparison.Ordinal)))
        {
            project.Title = unclassifiedTitle;
        }

        foreach (var group in _statusGroupVms.Values)
        {
            group.Title = GetStatusGroupTitle(group.Group);
        }

        var moreTitleFormat = Localize("Nav_MoreSessionsFormat", "Show more (+{0})");
        foreach (var moreItem in Items
                     .SelectMany(group => group.Children.OfType<MoreSessionsNavItemViewModel>()))
        {
            moreItem.UpdateTitleFormat(moreTitleFormat);
        }

        var loadingTitle = Localize("Nav_LoadingSessions", "Loading...");
        foreach (var placeholder in Items
                     .SelectMany(group => group.Children.OfType<SessionNavItemViewModel>())
                     .Where(session => session.IsPlaceholder))
        {
            placeholder.Title = loadingTitle;
        }

        foreach (var session in _sessionVms.Values)
        {
            if (_conversationCatalogIndex.TryGetValue(session.SessionId, out var catalogItem))
            {
                UpdateSessionPresentation(session, catalogItem);
            }

            session.RefreshLocalizedText();
        }

        RefreshRelativeTimes();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }

        _navigationState.PaneStateChanged -= OnServicePaneStateChanged;
        _conversationCatalogPresenter.PropertyChanged -= OnConversationCatalogPresenterPropertyChanged;
        _shellSelection.PropertyChanged -= OnShellSelectionPropertyChanged;
        _shellRuntimeState.PropertyChanged -= OnShellRuntimeStatePropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged -= _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.AgentRemoteDirectories).CollectionChanged -= _projectsChangedHandler;
        ((INotifyCollectionChanged)_projectPreferences.NavigationRemoteDirectoryIds).CollectionChanged -= _projectsChangedHandler;
        _projectPreferences.PropertyChanged -= OnProjectPreferencesPropertyChanged;
        _relativeTimeTimer.Dispose();

        var itemsToDispose = Items.Concat(FooterItems).Concat(GetCachedGroups()).Distinct().ToArray();
        foreach (var group in GetCachedGroups())
        {
            foreach (var child in group.Children.ToArray())
            {
                RemoveGroupChild(group, child);
            }
        }

        Items.Clear();
        FooterItems.Clear();
        _projectVms.Clear();
        _statusGroupVms.Clear();

        foreach (var item in itemsToDispose)
        {
            DisposeItem(item);
        }

        foreach (var session in _sessionVms.Values)
        {
            session.Dispose();
        }

        _sessionVms.Clear();
        _sessionIndex.Clear();
        _projectIndex.Clear();
        _appliedStatusMembership.Clear();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedText();
    }

    private void DisposeItem(object? item)
    {
        if (item is ProjectNavItemViewModel or StatusGroupNavItemViewModel)
        {
            UnwatchNavTreeStructure((MainNavItemViewModel)item);
        }

        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void WatchNavTreeStructure(MainNavItemViewModel group)
        => group.Children.CollectionChanged += OnNavTreeStructureChanged;

    private void UnwatchNavTreeStructure(MainNavItemViewModel group)
        => group.Children.CollectionChanged -= OnNavTreeStructureChanged;

    private void OnProjectPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationProjectPreferences.SidebarConversationGrouping))
        {
            RebuildTree();
        }
    }

    // Any add/move/remove changes which container renders which row, so the control's own selection
    // visual can end up attached to a row that now shows a different session. Record that the menu
    // source moved; ApplySelectionProjection decides whether a re-push is owed.
    private void OnNavTreeStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _navTreeStructureRevision++;

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
            if (TryMaterializeSelectedSession())
            {
                return;
            }

            NormalizeSelectionAfterRebuild();
            return;
        }

    }

    private void OnShellRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellNavigationRuntimeState.ActiveSessionActivation)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.DesiredSessionId)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.IsSessionActivationInProgress)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.PendingShellContent))
        {
            if (IsStatusGrouping)
            {
                RebuildTree();
            }
            else
            {
                ApplySelectionProjection();
            }
        }
    }

    public void RefreshSelectionProjection()
    {
        ApplySelectionProjection();
    }

    public void SetTransitionsFrozen(bool frozen)
    {
        if (_isDisposed || _transitionsFrozen == frozen)
        {
            return;
        }

        _transitionsFrozen = frozen;
        if (!frozen)
        {
            RebuildTree();
        }
    }

    public void SetFocusedConversationId(string? conversationId)
    {
        var focusedId = string.IsNullOrWhiteSpace(conversationId) ? null : conversationId;
        if (_isDisposed || string.Equals(_focusedConversationId, focusedId, StringComparison.Ordinal)) return;
        _focusedConversationId = focusedId;
        RebuildTree();
    }

    public string? TryGetProjectIdForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _sessionIndex.TryGetValue(sessionId, out var sessionItem)
            ? sessionItem.ProjectId
            : _conversationCatalogPresenter.Snapshot.FirstOrDefault(item => item.ConversationId == sessionId) is { } item
                ? ResolveEffectiveProjectId(item)
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
                await NotifyOpenStartFailedAsync().ConfigureAwait(true);
                return;
            }

            PendingProjectIdForNewSession = requestedProjectId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prepare start for project failed. projectId={ProjectId}", requestedProjectId);
            if (!IsLatestPendingProjectIntent(intentVersion))
            {
                return;
            }

            PendingProjectIdForNewSession = null;
            await NotifyOpenStartFailedAsync().ConfigureAwait(true);
        }
    }

    private Task NotifyOpenStartFailedAsync()
        => _ui.ShowInfoAsync(
            Localize(
                "Navigation_OpenStartFailed",
                "Failed to open the start page. Please try again later."));

    public async Task<bool> ActivateStartAsync(string? projectIdForNewSession = null)
    {
        try
        {
            var activated = await _navigationCoordinator
                .ActivateStartAsync(projectIdForNewSession)
                .ConfigureAwait(true);
            if (activated)
            {
                return true;
            }

            await NotifyOpenStartFailedAsync().ConfigureAwait(true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navigating to start failed");
            await NotifyOpenStartFailedAsync().ConfigureAwait(true);
            return false;
        }
    }

    public async Task<bool> ActivateSettingsAsync(string sectionKey)
    {
        try
        {
            var opened = await _navigationCoordinator
                .ActivateSettingsAsync(sectionKey)
                .ConfigureAwait(true);
            if (opened)
            {
                return true;
            }

            await NotifyOpenSettingsFailedAsync().ConfigureAwait(true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navigating to settings failed. sectionKey={SectionKey}", sectionKey);
            await NotifyOpenSettingsFailedAsync().ConfigureAwait(true);
            return false;
        }
    }

    private Task NotifyOpenSettingsFailedAsync()
        => _ui.ShowInfoAsync(
            Localize(
                "Navigation_OpenSettingsFailed",
                "Failed to open settings. Please try again later."));

    public async Task<bool> ActivateDiscoverSessionsAsync()
    {
        try
        {
            var opened = await _navigationCoordinator.ActivateDiscoverSessionsAsync().ConfigureAwait(true);
            if (opened)
            {
                return true;
            }

            await NotifyOpenDiscoverSessionsFailedAsync().ConfigureAwait(true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navigating to discover sessions failed");
            await NotifyOpenDiscoverSessionsFailedAsync().ConfigureAwait(true);
            return false;
        }
    }

    private Task NotifyOpenDiscoverSessionsFailedAsync()
        => _ui.ShowInfoAsync(
            Localize(
                "Navigation_OpenDiscoverSessionsFailed",
                "Failed to open Discover sessions. Please try again later."));

    public async Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
    {
        try
        {
            var activated = await _navigationCoordinator
                .ActivateSessionAsync(sessionId, projectId)
                .ConfigureAwait(true);
            if (activated)
            {
                return true;
            }

            if (ShouldSurfaceSessionActivationFailureInfo(sessionId))
            {
                await NotifyOpenSessionFailedAsync().ConfigureAwait(true);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navigating to session failed. sessionId={SessionId}", sessionId);
            await NotifyOpenSessionFailedAsync().ConfigureAwait(true);
            return false;
        }
    }

    private bool ShouldSurfaceSessionActivationFailureInfo(string sessionId)
    {
        // Once selection commits to the target session, the chat callout owns the failure surface.
        var currentSessionId = NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(CurrentSelection);
        if (string.Equals(currentSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        var activation = _shellRuntimeState.ActiveSessionActivation;
        if (activation is { Phase: SessionActivationPhase.Faulted }
            && activation.Matches(sessionId))
        {
            var reason = activation.Reason;
            if (!string.IsNullOrWhiteSpace(reason)
                && (reason.StartsWith("Superseded", StringComparison.Ordinal)
                    || string.Equals(reason, "Canceled", StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private Task NotifyOpenSessionFailedAsync()
        => _ui.ShowInfoAsync(
            Localize(
                "Navigation_OpenSessionFailed",
                "Failed to open this session. Please try again later."));

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

        return _projectPreferences.TryGetProjectCwd(projectId);
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
            await _ui.ShowInfoAsync(
                    Localize(
                        "Nav_ShowSessionsListFailed",
                        "Failed to open the sessions list. Please try again later."))
                .ConfigureAwait(true);
        }
    }

    public Task ShowMoreSessionsForStatusGroupAsync(ConversationStatusGroup group)
    {
        if (!_isDisposed && _statusGroupVms.ContainsKey(group))
        {
            var current = _statusGroupVisibleLimits.GetValueOrDefault(group, VisibleSessionsPerProjectLimit);
            _statusGroupVisibleLimits[group] = current + VisibleSessionsPerProjectLimit;
            RebuildTree();
        }

        return Task.CompletedTask;
    }

    private SessionNavItemViewModel CreateLoadingPlaceholder()
    {
        return new SessionNavItemViewModel(
            sessionId: "__loading__",
            remoteSessionId: null,
            projectId: UnclassifiedProjectId,
            title: Localize("Nav_LoadingSessions", "Loading..."),
            relativeTimeText: string.Empty,
            ui: _ui,
            shell: _shell,
            chatSessionCatalog: _chatSessionCatalogActions,
            navigationState: _navigationState,
            uiDispatcher: _uiDispatcher,
            localizer: _localizer,
            isPlaceholder: true);
    }

    private string Localize(string key, string fallback)
    {
        var localized = _localizer[key];
        return localized is null || localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private void ActivateSessionFromSessionsList(string sessionId, string projectId)
    {
        // Fire-and-forget: dialog pick must not block the list UI thread, but activation
        // still routes through ActivateSessionAsync so failures share the nav owner feedback path.
        _ = ObserveSessionActivationAsync(ActivateSessionAsync(sessionId, projectId));
    }

    private async Task ObserveSessionActivationAsync(Task<bool> activationTask)
    {
        try
        {
            await activationTask.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // ActivateSessionAsync already surfaces user-visible failures; keep this as a safety net.
            _logger.LogWarning(ex, "Session activation from sessions list failed");
        }
    }

    private async Task AddLocalProjectAsync()
    {
        if (!CanAddProject)
        {
            return;
        }

        var pickedPath = await _ui.PickFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            // User cancelled the native picker: no project state changes.
            return;
        }

        var outcome = _addProjectCoordinator.AddProject(
            new ProjectSourceSelection.LocalFolder(pickedPath));
        await ApplyAddProjectOutcomeAsync(outcome).ConfigureAwait(true);
    }

    private async Task SelectRemoteProjectAsync()
    {
        // Always allow opening the selector, even with no configured remote directories:
        // the dialog surfaces an empty state that routes the user to settings (plan §2.5).
        using var selectionViewModel = new RemoteProjectSelectionViewModel(_projectPreferences, _uiDispatcher);
        var result = await _ui.ShowRemoteProjectSelectionAsync(selectionViewModel).ConfigureAwait(true);

        switch (result)
        {
            case RemoteProjectSelectionResult.Confirmed confirmed:
                var outcome = _addProjectCoordinator.AddProject(
                    new ProjectSourceSelection.RemoteDirectory(confirmed.DirectoryId));
                await ApplyAddProjectOutcomeAsync(outcome).ConfigureAwait(true);
                break;

            case RemoteProjectSelectionResult.ManageRequested:
                await NavigateToRemoteProjectSettingsAsync().ConfigureAwait(true);
                break;

            case RemoteProjectSelectionResult.Cancelled:
            default:
                // No selection: leave all project state untouched.
                break;
        }
    }

    private async Task ApplyAddProjectOutcomeAsync(AddProjectOutcome outcome)
    {
        switch (outcome.Status)
        {
            case AddProjectStatus.Added:
                // The authoritative preferences collections changed; the tree rebuild is driven
                // by their CollectionChanged subscriptions. Rebuild explicitly as well so the new
                // node is materialized before activation on synchronous dispatchers.
                RebuildTree();
                if (!string.IsNullOrWhiteSpace(outcome.ProjectId))
                {
                    await PrepareStartForProjectAsync(outcome.ProjectId!).ConfigureAwait(true);
                }

                break;

            case AddProjectStatus.AlreadyExists:
                // Do not insert a duplicate; activate the existing project instead (plan §3.4).
                if (!string.IsNullOrWhiteSpace(outcome.ProjectId))
                {
                    await PrepareStartForProjectAsync(outcome.ProjectId!).ConfigureAwait(true);
                }

                break;

            case AddProjectStatus.RejectedUnknownRemote:
                await _ui.ShowInfoAsync(Localize("AddProject_RemoteProjectMissing", "That remote project no longer exists."))
                    .ConfigureAwait(true);
                break;

            case AddProjectStatus.Invalid:
                await _ui.ShowInfoAsync(Localize(
                        "AddProject_InvalidSelection",
                        "That project selection is not valid. Please choose another folder or remote directory."))
                    .ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    private Task NavigateToRemoteProjectSettingsAsync()
        => ActivateSettingsAsync(SettingsSectionCatalog.AgentAcpKey);

    public void RebuildTree()
    {
        if (_isDisposed)
        {
            return;
        }

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
        if (_isDisposed)
        {
            return;
        }

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
            EnsurePinnedMenuItems();

            // Build the new indexes in local scope first, then swap atomically.
            // Clearing _sessionIndex/_projectIndex upfront would create a window
            // where ApplySelectionProjection (triggered by async callbacks) sees
            // an empty index and projects ControlSelectedItem to null. That null
            // pushed through the binding causes NavigationView to lose its
            // IsChildSelected ancestor visual during display-mode transitions.
            var newSessionIndex = new Dictionary<string, SessionNavItemViewModel>(StringComparer.Ordinal);
            var projects = GetProjectDefinitions();
            var sessions = GetConversationCatalogSnapshot();
            ApplyGroupingPreference();
#if DEBUG
            var debugSelectedSessionId = NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(CurrentSelection);
            if (debugSelectedSessionId is not null)
            {
                var currentCatalogItem = _conversationCatalogPresenter.Snapshot
                    .FirstOrDefault(item => string.Equals(item.ConversationId, debugSelectedSessionId, StringComparison.Ordinal));
                _logger.LogDebug(
                    "Navigation rebuild evaluating selected session. SessionId={SessionId} CatalogCwd={CatalogCwd} BoundProfileId={BoundProfileId} RemoteSessionId={RemoteSessionId} SnapshotCount={SnapshotCount}",
                    debugSelectedSessionId,
                    currentCatalogItem?.Cwd,
                    currentCatalogItem?.BoundProfileId,
                    currentCatalogItem?.RemoteSessionId,
                    _conversationCatalogPresenter.Snapshot.Count);
            }
#endif

            var newProjectIndex = SynchronizeProjectDefinitions(projects);
            var desiredGroups = IsStatusGrouping
                ? SyncStatusGroups(sessions, newSessionIndex)
                : SyncProjectGroups(projects, sessions, newSessionIndex);
            SyncRootGroups(desiredGroups);
            RemoveRetiredNavigationItems(newProjectIndex);

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

            NormalizeSelectionAfterRebuild();

            // Notify that tree has been rebuilt
            TreeRebuilt?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navigation tree rebuild failed and was swallowed to keep the shell stable.");
        }
    }

    private Dictionary<string, ProjectNavItemViewModel> SynchronizeProjectDefinitions(
        IReadOnlyList<(ProjectDefinition Project, bool IsSystem)> projects)
    {
        var index = new Dictionary<string, ProjectNavItemViewModel>(StringComparer.Ordinal);
        foreach (var (definition, isSystem) in projects)
        {
            if (!_projectVms.TryGetValue(definition.ProjectId, out var item))
            {
                item = new ProjectNavItemViewModel(definition, isSystem, PrepareStartForProjectAsync, _navigationState, _uiDispatcher);
                WatchNavTreeStructure(item);
                _projectVms.Add(definition.ProjectId, item);
            }
            else
            {
                item.Title = definition.Name;
            }

            index.Add(definition.ProjectId, item);
        }

        return index;
    }

    private void RemoveRetiredNavigationItems(IReadOnlyDictionary<string, ProjectNavItemViewModel> projects)
    {
        foreach (var id in _sessionVms.Keys.Where(id => !_conversationCatalogIndex.ContainsKey(id)).ToArray())
        {
            var row = _sessionVms[id];
            DetachSessionFromOtherGroups(row, target: null);
            row.Dispose();
            _sessionVms.Remove(id);
        }

        foreach (var id in _projectVms.Keys.Where(id => !projects.ContainsKey(id)).ToArray())
        {
            var item = _projectVms[id];
            foreach (var child in item.Children.ToArray())
            {
                RemoveGroupChild(item, child);
            }

            DisposeItem(item);
            _projectVms.Remove(id);
        }
    }

    private void ApplyGroupingPreference()
    {
        var requested = AppSettingValueCatalog.NormalizeSidebarConversationGrouping(_projectPreferences.SidebarConversationGrouping);
        if (_transitionsFrozen || string.Equals(requested, _appliedGrouping, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var group in GetCachedGroups())
        {
            foreach (var child in group.Children.ToArray())
            {
                RemoveGroupChild(group, child);
            }
        }

        // A new status-mode visit initializes from explicit preferences. Reusing a previous native
        // compact/flyout projection here would overwrite the user's remembered expanded layout.
        foreach (var group in _statusGroupVms.Values)
        {
            Items.Remove(group);
            DisposeItem(group);
        }

        _statusGroupVms.Clear();
        _appliedGrouping = requested;
        _appliedStatusMembership.Clear();
        OnPropertyChanged(nameof(IsStatusGrouping));
    }

    private List<MainNavItemViewModel> SyncProjectGroups(
        List<(ProjectDefinition Project, bool IsSystem)> projects,
        IReadOnlyList<ConversationCatalogDisplayItem> sessions,
        Dictionary<string, SessionNavItemViewModel> targetSessionIndex)
    {
        var sessionsByProject = sessions
            .GroupBy(ResolveEffectiveProjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(GetNavigationSortTimestamp)
                .ThenByDescending(session => session.CatalogUpdatedAt).ToList(), StringComparer.Ordinal);
        var groups = new List<MainNavItemViewModel>(projects.Count);
        foreach (var (project, _) in projects)
        {
            var group = _projectVms[project.ProjectId];
            groups.Add(group);
            SyncSessions(group, sessionsByProject.GetValueOrDefault(project.ProjectId) ?? [], targetSessionIndex);
        }

        return groups;
    }

    private List<MainNavItemViewModel> SyncStatusGroups(
        IReadOnlyList<ConversationCatalogDisplayItem> sessions,
        Dictionary<string, SessionNavItemViewModel> targetSessionIndex)
    {
        var preserveMembership = ShouldPreserveRenderedSessionOrder();
        foreach (var session in sessions)
        {
            if (!_appliedStatusMembership.ContainsKey(session.ConversationId)
                || !preserveMembership && !string.Equals(session.ConversationId, _focusedConversationId, StringComparison.Ordinal))
            {
                _appliedStatusMembership[session.ConversationId] = session.StatusGroup;
            }
        }

        foreach (var id in _appliedStatusMembership.Keys.Where(id => !_conversationCatalogIndex.ContainsKey(id)).ToArray())
        {
            _appliedStatusMembership.Remove(id);
        }

        var groups = new List<MainNavItemViewModel>(StatusGroupOrder.Length);
        foreach (var status in StatusGroupOrder)
        {
            var group = GetOrCreateStatusGroup(status);
            var members = sessions.Where(session => _appliedStatusMembership[session.ConversationId] == status)
                .OrderByDescending(session => session.ActivityAt ?? GetNavigationSortTimestamp(session))
                .ThenBy(session => session.ConversationId, StringComparer.Ordinal)
                .ToList();
            SyncSessions(group, members, targetSessionIndex);
            group.Count = members.Count;
            groups.Add(group);
        }

        return groups;
    }

    private StatusGroupNavItemViewModel GetOrCreateStatusGroup(ConversationStatusGroup group)
    {
        if (!_statusGroupVms.TryGetValue(group, out var item))
        {
            item = new StatusGroupNavItemViewModel(
                group,
                GetStatusGroupTitle(group),
                _projectPreferences.GetStatusGroupExpanded(group),
                _projectPreferences.SetStatusGroupExpanded,
                _navigationState,
                _uiDispatcher);
            WatchNavTreeStructure(item);
            _statusGroupVms.Add(group, item);
        }

        return item;
    }

    private string GetStatusGroupTitle(ConversationStatusGroup group) => group switch
    {
        ConversationStatusGroup.NeedsAttention => Localize("Nav_StatusNeedsAttention", "Needs attention"),
        ConversationStatusGroup.Working => Localize("Nav_StatusWorkingGroup", "Working"),
        _ => Localize("Nav_StatusOther", "Other conversations")
    };

    private IEnumerable<MainNavItemViewModel> GetCachedGroups()
        => _projectVms.Values.Cast<MainNavItemViewModel>().Concat(_statusGroupVms.Values);

    private void SyncRootGroups(IReadOnlyList<MainNavItemViewModel> desiredGroups)
    {
        for (var i = Items.Count - 1; i >= PinnedMenuItemCount; i--)
        {
            if (!desiredGroups.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < desiredGroups.Count; i++)
        {
            var targetIndex = i + PinnedMenuItemCount;
            var group = desiredGroups[i];
            var currentIndex = Items.IndexOf(group);
            if (currentIndex < 0)
            {
                Items.Insert(targetIndex, group);
            }
            else if (currentIndex != targetIndex)
            {
                Items.Move(currentIndex, targetIndex);
            }
        }
    }

    private int EnsurePinnedMenuItems()
    {
        EnsurePinnedMenuItemAt(0, StartItem);
        EnsurePinnedMenuItemAt(1, SessionsLabelItem);
        EnsurePinnedMenuItemAt(2, AddProjectItem);
        return PinnedMenuItemCount;
    }

    private void EnsurePinnedMenuItemAt(int index, MainNavItemViewModel item)
    {
        var currentIndex = Items.IndexOf(item);
        if (currentIndex == index)
        {
            return;
        }

        if (currentIndex >= 0)
        {
            Items.RemoveAt(currentIndex);
        }

        if (index >= Items.Count)
        {
            Items.Add(item);
            return;
        }

        Items.Insert(index, item);
    }

    private void SyncSessions(MainNavItemViewModel group, List<ConversationCatalogDisplayItem> sessions, Dictionary<string, SessionNavItemViewModel> targetSessionIndex)
    {
        var children = group.Children;
        var limit = group is StatusGroupNavItemViewModel statusGroup
            ? _statusGroupVisibleLimits.GetValueOrDefault(statusGroup.Group, VisibleSessionsPerProjectLimit)
            : VisibleSessionsPerProjectLimit;
        var top = BuildVisibleSessionsForGroup(sessions, group, limit);
        // While the pane is unsettled, hold already-rendered rows in place instead of applying a
        // fresh recency order. Reordering a rendered row makes Uno's ItemsRepeater recycle its
        // container (Move is decomposed into Remove+Add); the recycle pool keeps the selected flag
        // and NavigationView's deselect of the previous item no-ops once the container is gone, so
        // the mask strands across rows. See NavigationSessionOrderPolicy for the full rationale.
        top = NavigationSessionOrderPolicy.ResolveAppliedOrder(
            top,
            children.OfType<SessionNavItemViewModel>()
                .Where(session => !session.IsPlaceholder)
                .Select(session => session.SessionId)
                .ToList(),
            ShouldPreserveRenderedSessionOrder(group),
            static session => session.ConversationId);
        var remainingCount = Math.Max(0, sessions.Count - top.Count);
        var visibleIds = top.Select(session => session.ConversationId).ToHashSet(StringComparer.Ordinal);
        foreach (var previous in children.OfType<SessionNavItemViewModel>().Where(row => !row.IsPlaceholder).ToArray())
        {
            if (!visibleIds.Contains(previous.SessionId)) RemoveGroupChild(group, previous);
        }

        int childIndex = 0;
        foreach (var session in top)
        {
            var sessionVm = GetOrCreateSession(session);
            DetachSessionFromOtherGroups(sessionVm, group);
            var existingIndex = children.IndexOf(sessionVm);
            if (existingIndex < 0)
            {
                children.Insert(childIndex, sessionVm);
            }
            else if (existingIndex != childIndex)
            {
                // The order policy above owns the safe time to converge; Move itself does not
                // prevent Uno's repeater from recycling a container.
                children.Move(existingIndex, childIndex);
            }

            targetSessionIndex[session.ConversationId] = sessionVm;
            childIndex++;
        }

        SyncMoreSessions(group, remainingCount, childIndex);
        if (remainingCount > 0)
        {
            childIndex++;
        }

        // Add loading placeholder if needed
        if (IsConversationListLoading && childIndex == 0 && group is
            (ProjectNavItemViewModel { ProjectId: UnclassifiedProjectId }
             or StatusGroupNavItemViewModel { Group: ConversationStatusGroup.Other }))
        {
            if (children.FirstOrDefault() is not SessionNavItemViewModel { IsPlaceholder: true })
            {
                children.Insert(0, CreateLoadingPlaceholder());
            }

            childIndex++;
        }

        while (children.Count > childIndex)
        {
            RemoveGroupChild(group, children[^1]);
        }
    }

    private void SyncMoreSessions(MainNavItemViewModel group, int remainingCount, int index)
    {
        var existing = group.Children.OfType<MoreSessionsNavItemViewModel>().FirstOrDefault();
        if (remainingCount == 0)
        {
            if (existing is not null)
            {
                RemoveGroupChild(group, existing);
            }

            return;
        }

        if (existing is null)
        {
            existing = group is StatusGroupNavItemViewModel status
                ? new MoreSessionsNavItemViewModel(
                    status.Group, remainingCount,
                    new AsyncRelayCommand(() => ShowMoreSessionsForStatusGroupAsync(status.Group)),
                    _navigationState, _uiDispatcher, Localize("Nav_MoreSessionsFormat", "Show more (+{0})"))
                : new MoreSessionsNavItemViewModel(
                    ((ProjectNavItemViewModel)group).ProjectId, remainingCount,
                    new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(((ProjectNavItemViewModel)group).ProjectId)),
                    _navigationState, _uiDispatcher, Localize("Nav_MoreSessionsFormat", "Show more (+{0})"));
            group.Children.Insert(index, existing);
        }
        else
        {
            existing.Count = remainingCount;
            var previousIndex = group.Children.IndexOf(existing);
            if (previousIndex != index)
            {
                group.Children.Move(previousIndex, index);
            }
        }
    }

    private SessionNavItemViewModel GetOrCreateSession(ConversationCatalogDisplayItem session)
    {
        if (!_sessionVms.TryGetValue(session.ConversationId, out var item))
        {
            item = new SessionNavItemViewModel(
                session.ConversationId, session.RemoteSessionId, ResolveEffectiveProjectId(session),
                string.Empty, string.Empty, _ui, _shell, _chatSessionCatalogActions,
                _navigationState, _uiDispatcher, _localizer);
            _sessionVms.Add(session.ConversationId, item);
        }

        UpdateSessionPresentation(item, session);
        return item;
    }

    private void UpdateSessionPresentation(SessionNavItemViewModel item, ConversationCatalogDisplayItem session)
    {
        item.Title = string.IsNullOrWhiteSpace(session.DisplayName)
            ? SessionNamePolicy.CreateDefault(session.ConversationId)
            : session.DisplayName.Trim();
        item.RemoteSessionId = session.RemoteSessionId;
        item.RelativeTimeText = NavTimeFormatter.ToRelativeText(GetDisplayedSessionTimestamp(session), _localizer);
        item.HasUnreadAttention = session.HasUnreadAttention;
        item.StatusIcon = session.StatusIcon;
        var projectId = ResolveEffectiveProjectId(session);
        var projectName = _projectVms.TryGetValue(projectId, out var project)
            ? project.Title
            : Localize("Nav_Unclassified", "Unclassified");
        item.UpdateProject(projectId, projectName, IsStatusGrouping);
    }

    private void DetachSessionFromOtherGroups(SessionNavItemViewModel row, MainNavItemViewModel? target)
    {
        foreach (var group in GetCachedGroups())
        {
            if (!ReferenceEquals(group, target))
            {
                group.Children.Remove(row);
            }
        }
    }

    private void RemoveGroupChild(MainNavItemViewModel group, MainNavItemViewModel child)
    {
        group.Children.Remove(child);
        if (child is not SessionNavItemViewModel { IsPlaceholder: false })
        {
            DisposeItem(child);
        }
    }

    /// <summary>
    /// Whether the pane is currently unsettled, in which case already-rendered session rows must
    /// keep their positions so no realized container is recycled mid-selection.
    /// </summary>
    /// <remarks>
    /// Unsettled means a session activation is in flight (the control has already self-selected the
    /// invoked leaf via <c>SelectsOnInvoked</c> while the outgoing row is still painted selected) or
    /// the conversation catalog is still loading and therefore still churning the sort key. Both are
    /// navigation-owned state; nothing here reads or writes the control's own selection.
    /// </remarks>
    private bool ShouldPreserveRenderedSessionOrder(MainNavItemViewModel? group = null)
        => _transitionsFrozen
           || IsConversationListLoading
           || _shellRuntimeState.IsSessionActivationInProgress
           || ResolveActiveSessionActivationProjectionSessionId() is not null
           || group is not null && group.Children.OfType<SessionNavItemViewModel>()
               .Any(row => string.Equals(row.SessionId, _focusedConversationId, StringComparison.Ordinal));

    private List<ConversationCatalogDisplayItem> BuildVisibleSessionsForGroup(
        List<ConversationCatalogDisplayItem> sessions,
        MainNavItemViewModel group,
        int limit)
    {
        var visible = sessions.Take(limit).ToList();
        if (ShouldPreserveRenderedSessionOrder(group))
        {
            foreach (var row in group.Children.OfType<SessionNavItemViewModel>().Where(row => !row.IsPlaceholder))
            {
                EnsureRequiredVisibleSession(sessions, visible, row.SessionId);
            }
        }

        EnsureRequiredVisibleSession(
            sessions,
            visible,
            NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(CurrentSelection));
        EnsureRequiredVisibleSession(sessions, visible, ResolveActiveSessionActivationProjectionSessionId());
        return visible;
    }

    private static void EnsureRequiredVisibleSession(
        IReadOnlyList<ConversationCatalogDisplayItem> sessions,
        ICollection<ConversationCatalogDisplayItem> visible,
        string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || visible.Any(item => string.Equals(item.ConversationId, sessionId, StringComparison.Ordinal)))
        {
            return;
        }

        var selected = sessions.FirstOrDefault(
            item => string.Equals(item.ConversationId, sessionId, StringComparison.Ordinal));
        if (selected is not null)
        {
            visible.Add(selected);
        }
    }

    private List<(ProjectDefinition Project, bool IsSystem)> GetProjectDefinitions()
    {
        var projects = new List<(ProjectDefinition Project, bool IsSystem)>
        {
            (new ProjectDefinition
            {
                ProjectId = UnclassifiedProjectId,
                Name = Localize("Nav_Unclassified", "Unclassified"),
                RootPath = string.Empty
            }, true)
        };

        projects.AddRange(_projectPreferences.Projects
            .Where(p => p != null
                        && !string.IsNullOrWhiteSpace(p.ProjectId)
                        && !string.IsNullOrWhiteSpace(p.Name)
                        && !string.IsNullOrWhiteSpace(p.RootPath))
            .Select(p => (p, false)));

        // Remote directories the user added to navigation become project nodes too. Identity is
        // the stable semantic id built by ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId;
        // DisplayName and RootPath are projected read-only from the authoritative
        // AgentRemoteDirectories (falling back to the path when the display name is blank).
        // Membership order defines nav order.
        foreach (var directoryId in _projectPreferences.NavigationRemoteDirectoryIds)
        {
            var normalizedId = directoryId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                continue;
            }

            var directory = _projectPreferences.AgentRemoteDirectories.FirstOrDefault(candidate =>
                candidate is not null
                && string.Equals(candidate.DirectoryId, normalizedId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(candidate.RemotePath));
            if (directory is null)
            {
                // Membership without a matching authoritative directory is stale; skip it rather
                // than resolve it as anything else. AppPreferencesViewModel prunes such ids.
                continue;
            }

            projects.Add((new ProjectDefinition
            {
                ProjectId = ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId(directory.DirectoryId),
                Name = string.IsNullOrWhiteSpace(directory.DisplayName) ? directory.RemotePath : directory.DisplayName,
                RootPath = directory.RemotePath
            }, false));
        }

        return projects;
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
                _sessionIndex);
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(ProjectedControlSelectedItem));
        }
    }

    private void ApplySelectionProjection()
    {
        var previousProjection = _projection;
        // Project the committed semantic selection faithfully, except while the
        // coordinator owns a non-terminal session activation. That pending session is a
        // view projection of the same latest-intent chain that drives the chat overlay;
        // it is not committed to CurrentSelection until SwitchConversationAsync succeeds.
        var projectedSelection = ResolveProjectedSelection();

        if (TryMaterializeProjectedSession(projectedSelection))
        {
            return;
        }

        var projectedSessionId = NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(projectedSelection);
        if (projectedSessionId is not null
            && _sessionIndex.TryGetValue(projectedSessionId, out var sessionItem))
        {
            _logger.LogDebug(
                "SelectionProjection sessionId={SessionId} projectId={ProjectId} paneOpen={PaneOpen} projectIndexHas={ProjectIndexHas} semantic={SemanticSelection} previewActive={PreviewActive}",
                projectedSessionId,
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
            _sessionIndex);

        _projection = nextProjection;
        if (previousProjection.IsSettingsSelected != _projection.IsSettingsSelected)
        {
            OnPropertyChanged(nameof(IsSettingsSelected));
        }

        // Two independent reasons to push the control's SelectedItem, and the second is not derivable
        // from the first. The projected instance is stable across rebuilds on purpose - SyncSessions
        // reuses the row view models so a realized container is never recycled needlessly - so a
        // reshuffle, such as inserting a freshly created session at the top of its project, leaves
        // this projection reference-equal while every container below it now renders a different row.
        // Keying only on the instance means the control is never told again, and a OneWay binding
        // gives us no readback to notice. Re-stating the selection once per structural revision keeps
        // pane toggles and no-op refreshes quiet while still re-pushing against a rebuilt source.
        var structureMoved = _navTreeStructureRevision != _publishedNavTreeStructureRevision;
        if (structureMoved
            || !ReferenceEquals(previousProjection.ControlSelectedItem, _projection.ControlSelectedItem))
        {
            _publishedNavTreeStructureRevision = _navTreeStructureRevision;
            OnPropertyChanged(nameof(ProjectedControlSelectedItem));
        }
    }

    private bool TryMaterializeSelectedSession()
        => TryMaterializeSession(NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(CurrentSelection));

    private bool TryMaterializeProjectedSession(NavigationSelectionState selection)
        => TryMaterializeSession(NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(selection));

    /// <summary>
    /// Asks for a rebuild so a catalog-known session that has no nav row yet gains one, and reports
    /// whether the caller should let that rebuild publish the projection instead of publishing now.
    /// </summary>
    /// <remarks>
    /// Goes through the coalescing scheduler rather than calling <see cref="RebuildTreeCore"/> inline.
    /// Both callers run inside selection change notifications, and mutating the bound Items/Children
    /// collections from there re-enters this same path, because <see cref="RebuildTreeCore"/> ends in
    /// <see cref="NormalizeSelectionAfterRebuild"/>. The scheduler already serialises rebuild requests:
    /// a request raised while one is running is folded into the pending flag and re-scheduled by
    /// <see cref="ProcessRebuildTreeRequests"/> when the running pass unwinds.
    /// Returning true keeps this pass from publishing, because the projection available right now
    /// resolves the not-yet-materialised session to a null selected item, and pushing that null
    /// through the binding costs NavigationView its IsChildSelected ancestor visual - the same reason
    /// <see cref="RebuildTreeCore"/> swaps its indexes atomically instead of clearing them upfront.
    /// </remarks>
    private bool TryMaterializeSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || _sessionIndex.ContainsKey(sessionId)
            || !CanMaterializeSelectedSession(sessionId))
        {
            return false;
        }

        RebuildTree();
        return true;
    }

    private NavigationSelectionState ResolveProjectedSelection()
        => NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            CurrentSelection,
            _shellRuntimeState.ActiveSessionActivation,
            _shellRuntimeState.PendingShellContent,
            _shellRuntimeState.LatestActivationToken,
            CanProjectSession);

    private string? ResolveActiveSessionActivationProjectionSessionId()
        => NavigationSelectionProjectionPolicy.ResolveActiveSessionActivationProjectionSessionId(
            _shellRuntimeState.ActiveSessionActivation,
            _shellRuntimeState.PendingShellContent,
            _shellRuntimeState.LatestActivationToken);

    private bool CanProjectSession(string sessionId)
        => _sessionIndex.ContainsKey(sessionId) || CanMaterializeSelectedSession(sessionId);

    private bool CanMaterializeSelectedSession(string sessionId)
    {
        var selected = _conversationCatalogPresenter.Snapshot.FirstOrDefault(
            item => string.Equals(item.ConversationId, sessionId, StringComparison.Ordinal));
        if (selected is null)
        {
            return false;
        }

        if (IsStatusGrouping)
        {
            return true;
        }

        var projectId = ResolveEffectiveProjectId(selected);
        return _projectIndex.ContainsKey(projectId)
            || GetProjectDefinitions().Any(
                project => string.Equals(project.Project.ProjectId, projectId, StringComparison.Ordinal));
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

            var timestamp = GetDisplayedSessionTimestamp(session);
            var relative = NavTimeFormatter.ToRelativeText(timestamp, _localizer);
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

    private ProjectNavItemViewModel CreateUnclassifiedProject()
    {
        var project = new ProjectDefinition
        {
            ProjectId = UnclassifiedProjectId,
            Name = Localize("Nav_Unclassified", "Unclassified"),
            RootPath = string.Empty
        };
        var vm = new ProjectNavItemViewModel(project, isSystemProject: true, PrepareStartForProjectAsync, _navigationState, _uiDispatcher) { IsExpanded = true };
        WatchNavTreeStructure(vm);
        _projectIndex[vm.ProjectId] = vm;
        _projectVms[vm.ProjectId] = vm;
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

        return sessions.Select(GetOrCreateSession).ToList();
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

    private DateTime GetDisplayedSessionTimestamp(ConversationCatalogDisplayItem item)
        => IsStatusGrouping ? item.ActivityAt ?? GetNavigationSortTimestamp(item) : GetNavigationSortTimestamp(item);

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
            RemoteDirectories: _projectPreferences.AgentRemoteDirectories,
            UnclassifiedProjectId: UnclassifiedProjectId,
            NavigationRemoteDirectoryIds: _projectPreferences.NavigationRemoteDirectoryIds));
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
