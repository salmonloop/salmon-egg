using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.ViewModels.Composer;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed partial class StartViewModel : ObservableObject
{
    private readonly AppPreferencesViewModel _preferences;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly INavigationProjectSelectionStore _projectSelectionStore;
    private readonly MainNavigationViewModel _nav;
    private readonly IChatLaunchWorkflow _chatLaunchWorkflow;
    private readonly IConversationCatalogReadModel _conversationCatalog;
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private readonly ILogger<StartViewModel> _logger;
    private readonly SelectorProjectionPresenter _selectorProjectionPresenter = new();
    private readonly ModeSelectorPolicy _modeSelectorPolicy = new();
    private readonly AgentSelectorPolicy _agentSelectorPolicy = new();
    private readonly ProjectSelectorPolicy _projectSelectorPolicy = new();
    private readonly ObservableCollection<StartProjectOptionViewModel> _startProjectOptions = new();
    private StartSessionModeSnapshot _startSessionModeSnapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
        IsStarting: false,
        IsConnectionReady: false,
        IsDraftRefreshPending: false,
        IsDraftLoading: false,
        IsDraftReady: false,
        ModeCount: 0));
    private string? _selectedStartProjectIdOverride;
    private CancellationTokenSource? _newSessionDraftCts;
    private Task _composerUnloadCleanupTask = Task.CompletedTask;
    private Task _composerUnloadCleanupObservationTask = Task.CompletedTask;
    private bool _isNewSessionDraftRefreshPending;
    private bool _isComposerLoaded;

    public ChatViewModel Chat { get; }

    internal Task ComposerUnloadCleanupTask => _composerUnloadCleanupTask;

    private bool _isStarting;

    public bool IsStarting
    {
        get => _isStarting;
        set
        {
            if (SetProperty(ref _isStarting, value))
            {
                OnPropertyChanged(nameof(IsInputEnabled));
                OnPropertyChanged(nameof(CanStartSessionAndSendUi));
                RefreshStartModeState();
                RefreshAllSelectorProjections();
                RefreshVoiceProjection();
                StartSessionAndSendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StartPrompt
    {
        get => Chat.CurrentPrompt ?? string.Empty;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(Chat.CurrentPrompt, next, StringComparison.Ordinal))
            {
                return;
            }

            Chat.CurrentPrompt = next;
            RefreshStartPromptProjection(next);
        }
    }

    public IAsyncRelayCommand StartSessionAndSendCommand { get; }

    public bool CanStartSessionAndSendUi => StartSessionAndSendCommand.CanExecute(null);

    public System.Collections.ObjectModel.ObservableCollection<QuickSuggestionViewModel> Suggestions { get; } = new();

    public ReadOnlyObservableCollection<StartProjectOptionViewModel> StartProjectOptions { get; }

    public ReadOnlyObservableCollection<SessionModeViewModel> StartModeOptions => Chat.NewSessionDraftModeOptions;

    public SelectorProjectionResult StartAgentSelectorProjection => ResolveStartAgentSelectorProjection();

    public SelectorProjectionResult StartModeSelectorProjection => ResolveStartModeSelectorProjection();

    public SelectorProjectionResult StartProjectSelectorProjection => ResolveStartProjectSelectorProjection();

    public ComposerSelectorSlotsPresentation ComposerSelectorSlots
        => new(
            Agent: new(
                IsVisible: true,
                IsEnabled: true,
                Items: StartAgentSelectorItems,
                SelectedItem: SelectedStartAgentSelectorItem,
                SelectionCommand: SelectStartAgentDisplayCommand),
            Mode: new(
                IsVisible: true,
                IsEnabled: IsStartModeSelectorEnabled,
                Items: StartModeSelectorItems,
                SelectedItem: SelectedStartModeSelectorItem,
                SelectionCommand: SelectStartModeDisplayCommand),
            Project: new(
                IsVisible: true,
                IsEnabled: true,
                Items: StartProjectSelectorItems,
                SelectedItem: SelectedStartProjectSelectorItem,
                SelectionCommand: SelectStartProjectDisplayCommand));

    public IReadOnlyList<ComposerSelectorItemViewModel> StartAgentSelectorItems
        => StartAgentSelectorProjection.DisplayItems;

    public IReadOnlyList<ComposerSelectorItemViewModel> StartModeSelectorItems
        => StartModeSelectorProjection.DisplayItems;

    public IReadOnlyList<ComposerSelectorItemViewModel> StartProjectSelectorItems
        => StartProjectSelectorProjection.DisplayItems;

    public ComposerSelectorItemViewModel? SelectedStartAgentSelectorItem
        => StartAgentSelectorProjection.SelectedDisplayItem;

    public ComposerSelectorItemViewModel? SelectedStartModeSelectorItem
        => StartModeSelectorProjection.SelectedDisplayItem;

    public ComposerSelectorItemViewModel? SelectedStartProjectSelectorItem
        => StartProjectSelectorProjection.SelectedDisplayItem;

    public IRelayCommand<QuickSuggestionViewModel> ExecuteSuggestionCommand { get; }

    public IRelayCommand<SessionModeViewModel?> SelectStartModeCommand { get; }

    public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartModeDisplayCommand { get; }

    public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartAgentDisplayCommand { get; }

    public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartProjectDisplayCommand { get; }

    public bool IsInputEnabled => !IsStarting;

    public string SelectedStartProjectId
    {
        get => GetSelectedStartProjectId();
        set
        {
            var normalizedSelection = NormalizeProjectSelectionValue(value);
            if (string.Equals(_selectedStartProjectIdOverride, normalizedSelection, StringComparison.Ordinal)
                && string.Equals(GetSelectedStartProjectId(), normalizedSelection, StringComparison.Ordinal))
            {
                return;
            }

            _projectSelectionStore.RememberSelectedProject(normalizedSelection);
            _selectedStartProjectIdOverride = normalizedSelection;
            _nav.ClearPendingProjectForNewSession();
            OnPropertyChanged(nameof(SelectedStartProjectId));
            RefreshAllSelectorProjections();
            RefreshStartSessionDraftErrorProjection();
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartSessionAndSendUi));
            QueueEnsureNewSessionDraft();
        }
    }

    public SessionModeViewModel? SelectedStartMode
    {
        get => Chat.SelectedNewSessionDraftMode;
        set => Chat.SelectedNewSessionDraftMode = value;
    }

    public StartSessionModeStage StartModeStage => _startSessionModeSnapshot.Stage;

    public bool IsStartModeSelectorEnabled => _startSessionModeSnapshot.IsEnabled;

    public bool HasStartSessionDraftError
        => Chat.HasNewSessionDraftError && !IsExpectedRemoteDirectorySelectionState();

    public string StartSessionDraftErrorMessage
        => HasStartSessionDraftError ? Chat.NewSessionDraftErrorMessage : string.Empty;

    public string StartDraftAutomationState
        => string.Join(
            ";",
            $"Stage={StartModeStage}",
            $"Ready={Chat.IsNewSessionDraftReady}",
            $"Loading={Chat.IsNewSessionDraftLoading}",
            $"Pending={_isNewSessionDraftRefreshPending}",
            $"Error={HasStartSessionDraftError}",
            $"ModeCount={StartModeOptions.Count.ToString(CultureInfo.InvariantCulture)}",
            $"SelectedProfile={Chat.SelectedAcpProfile?.Id ?? string.Empty}",
            $"SelectedIntent={Chat.SelectedProfileIntentId ?? string.Empty}",
            $"ConnectionId={Chat.ConnectionInstanceId ?? string.Empty}",
            $"Message={StartSessionDraftErrorMessage}");

    public bool IsVoiceInputSupported => Chat.IsVoiceInputSupported;

    public bool CanStartVoiceInput => !IsStarting && Chat.CanStartVoiceInput;

    public bool CanStopVoiceInput => !IsStarting && Chat.CanStopVoiceInput;

    public bool ShowVoiceInputStartButton => Chat.ShowVoiceInputStartButton;

    public bool ShowVoiceInputStopButton => Chat.ShowVoiceInputStopButton;

    public IAsyncRelayCommand StartVoiceInputCommand { get; }

    public IAsyncRelayCommand StopVoiceInputCommand { get; }

    public StartViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        INavigationProjectPreferences projectPreferences,
        INavigationProjectSelectionStore projectSelectionStore,
        INavigationCoordinator navigationCoordinator,
        MainNavigationViewModel nav,
        ILogger<StartViewModel> logger,
        IChatLaunchWorkflow? chatLaunchWorkflow = null,
        IChatConnectionStore? chatConnectionStore = null,
        IConversationCatalogReadModel? conversationCatalog = null,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        ArgumentNullException.ThrowIfNull(sessionManager);
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        ArgumentNullException.ThrowIfNull(navigationCoordinator);
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _conversationCatalog = conversationCatalog ?? NoOpConversationCatalogReadModel.Instance;
        _localizer = localizer;
        StartProjectOptions = new ReadOnlyObservableCollection<StartProjectOptionViewModel>(_startProjectOptions);
        _chatLaunchWorkflow = chatLaunchWorkflow ?? new ChatLaunchWorkflow(
            new ChatLaunchWorkflowChatFacadeAdapter(
                Chat,
                chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore))),
            sessionManager,
            navigationCoordinator,
            ResolveDefaultCwd);

        StartSessionAndSendCommand = new AsyncRelayCommand(StartSessionAndSendAsync, CanStartSessionAndSend);
        ExecuteSuggestionCommand = new RelayCommand<QuickSuggestionViewModel>(ExecuteSuggestion);
        SelectStartModeCommand = new RelayCommand<SessionModeViewModel?>(SelectStartMode);
        SelectStartModeDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartModeDisplay);
        SelectStartAgentDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartAgentDisplay);
        SelectStartProjectDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartProjectDisplay);
        StartVoiceInputCommand = new AsyncRelayCommand(StartVoiceInputAsync, () => CanStartVoiceInput);
        StopVoiceInputCommand = new AsyncRelayCommand(StopVoiceInputAsync, () => CanStopVoiceInput);

        InitializeSuggestions();
        RefreshStartProjectOptions();
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _nav.PropertyChanged += OnNavigationPropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += OnProjectPreferencesChanged;
        ((INotifyCollectionChanged)_preferences.AgentRemoteDirectories).CollectionChanged += OnAgentRemoteDirectoriesChanged;
        _conversationCatalog.PropertyChanged += OnConversationCatalogPropertyChanged;
        Chat.PropertyChanged += OnChatPropertyChanged;
        ((INotifyCollectionChanged)Chat.NewSessionDraftModeOptions).CollectionChanged += OnStartModeOptionsChanged;
        ApplyPendingProjectIntent();
    }

    private void InitializeSuggestions()
    {
        Suggestions.Add(new QuickSuggestionViewModel("StartView.Suggestion.AnalyzeCodebase", "\uE943", "分析代码库", "深入理解项目架构与逻辑", "请帮我分析一下当前代码库的架构和核心逻辑。", ExecuteSuggestionCommand));
        Suggestions.Add(new QuickSuggestionViewModel("StartView.Suggestion.RecommendTasks", "\uE762", "推荐开发任务", "明确接下来该做什么", "根据当前进度，推荐几个接下来可以进行的开发任务或优化点。", ExecuteSuggestionCommand));
        Suggestions.Add(new QuickSuggestionViewModel("StartView.Suggestion.ResolveErrors", "\uEBE8", "解决最近报错", "提交错误日志让我看看", "我刚才遇到了一些报错，请帮我分析并解决它们。", ExecuteSuggestionCommand));
    }

    private void ExecuteSuggestion(QuickSuggestionViewModel? suggestion)
    {
        if (suggestion == null) return;
        StartPrompt = suggestion.Prompt;
    }

    private void SelectStartMode(SessionModeViewModel? mode)
    {
        if (mode != null)
        {
            SelectedStartMode = mode;
        }
    }

    private void SelectStartModeDisplay(ComposerSelectorItemViewModel? item)
    {
        if (!CanCommitSelectorItem(item, ComposerSelectorKind.Mode, StartModeSelectorItems))
        {
            return;
        }

        var mode = StartModeOptions.FirstOrDefault(candidate =>
            string.Equals(candidate.ModeId, item!.SemanticValue, StringComparison.Ordinal));
        if (mode is not null)
        {
            SelectedStartMode = mode;
        }
    }

    private void SelectStartAgentDisplay(ComposerSelectorItemViewModel? item)
    {
        if (!CanCommitSelectorItem(item, ComposerSelectorKind.Agent, StartAgentSelectorItems))
        {
            return;
        }

        var agent = Chat.AcpProfileList.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, item!.SemanticValue, StringComparison.Ordinal));
        if (agent is not null)
        {
            Chat.SelectedAcpProfile = agent;
        }
    }

    private void SelectStartProjectDisplay(ComposerSelectorItemViewModel? item)
    {
        if (!CanCommitSelectorItem(item, ComposerSelectorKind.Project, StartProjectSelectorItems))
        {
            return;
        }

        SelectedStartProjectId = item!.SemanticValue ?? NavigationProjectIds.Unclassified;
    }

    private static bool CanCommitSelectorItem(
        ComposerSelectorItemViewModel? item,
        ComposerSelectorKind expectedKind,
        IReadOnlyList<ComposerSelectorItemViewModel> currentItems)
        => item is not null
            && item.Kind == expectedKind
            && !item.IsPlaceholder
            && item.IsSelectable
            && !string.IsNullOrWhiteSpace(item.SemanticValue)
            && currentItems.Any(candidate =>
                string.Equals(candidate.SemanticValue, item.SemanticValue, StringComparison.Ordinal)
                && string.Equals(candidate.Identity, item.Identity, StringComparison.Ordinal));

    public void OnComposerLoaded()
    {
        _isComposerLoaded = true;
        OnPropertyChanged(nameof(SelectedStartProjectId));
        QueueEnsureNewSessionDraft();
    }

    public void OnComposerUnloaded()
    {
        _isComposerLoaded = false;
        CancelNewSessionDraftRefresh();
        TrackNewSessionDraftDiscard(Chat.DiscardNewSessionDraftAsync());
    }

    private void TrackNewSessionDraftDiscard(Task cleanupTask)
    {
        _composerUnloadCleanupTask = cleanupTask;
        _composerUnloadCleanupObservationTask = cleanupTask.ContinueWith(
            static (task, state) =>
            {
                var logger = (ILogger<StartViewModel>)state!;
                logger.LogWarning(
                    task.Exception,
                    "Failed to discard ACP new-session draft.");
            },
            _logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task StartSessionAndSendAsync()
    {
        var promptText = (StartPrompt ?? string.Empty).Trim();
        if (!CanStartSessionAndSend())
        {
            return;
        }

        IsStarting = true;
        StartSessionAndSendCommand.NotifyCanExecuteChanged();
        var submitSucceeded = false;
        try
        {
            await _chatLaunchWorkflow
                .StartSessionAndSendAsync(
                    promptText,
                    NormalizeProjectSelectionValue(SelectedStartProjectId))
                .ConfigureAwait(true);
            submitSucceeded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Start session failed");
        }
        finally
        {
            if (submitSucceeded)
            {
                StartPrompt = string.Empty;
            }

            IsStarting = false;
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshStartPromptProjection(string value)
    {
        OnPropertyChanged(nameof(StartPrompt));
        StartSessionAndSendCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanStartSessionAndSendUi));
    }

    private bool CanStartSessionAndSend()
        => _startSessionModeSnapshot.CanSubmitPrompt
            && !StartAgentSelectorProjection.IsSubmitBlocked
            && !StartModeSelectorProjection.IsSubmitBlocked
            && !StartProjectSelectorProjection.IsSubmitBlocked
            && !string.IsNullOrWhiteSpace(StartPrompt);

    private SelectorProjectionResult ResolveStartAgentSelectorProjection()
    {
        var identity = $"agent|{Chat.SelectedAcpProfile?.Id ?? string.Empty}|{Chat.ConnectionInstanceId ?? string.Empty}";
        var hasAgentSlot = Chat.AcpProfileList.Count > 0;
        var policy = _agentSelectorPolicy.Project(new AgentSelectorPolicyInput(
            identity,
            Chat.AcpProfileList,
            Chat.SelectedAcpProfile?.Id,
            Chat.IsConnecting || Chat.IsInitializing,
            Chat.HasConnectionError,
            !hasAgentSlot || (Chat.SelectedAcpProfile is not null && Chat.IsConnected),
            ResolveAgentSelectorPlaceholderLabels()));

        return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Agent,
            policy.RealItems,
            policy.SelectedSemanticValue,
            policy.Placeholder,
            policy.ReplaceSelectionWithPlaceholder,
            policy.DisableRealItems,
            policy.SelectorEnabled && IsInputEnabled));
    }

    private SelectorProjectionResult ResolveStartModeSelectorProjection()
    {
        var identity = BuildStartModeIdentity();
        var showRemoteDirectoryPrompt = IsExpectedRemoteDirectorySelectionState();
        var hasDraftError = Chat.HasNewSessionDraftError && !showRemoteDirectoryPrompt;
        IReadOnlyList<SessionModeViewModel> modeOptions = showRemoteDirectoryPrompt
            ? Array.Empty<SessionModeViewModel>()
            : StartModeOptions;
        var policy = _modeSelectorPolicy.Project(new ModeSelectorPolicyInput(
            identity,
            identity,
            modeOptions,
            showRemoteDirectoryPrompt ? null : SelectedStartMode?.ModeId,
            !showRemoteDirectoryPrompt && Chat.IsNewSessionDraftReady,
            !showRemoteDirectoryPrompt && (_isNewSessionDraftRefreshPending || Chat.IsNewSessionDraftLoading),
            hasDraftError,
            (!showRemoteDirectoryPrompt && StartModeOptions.Count > 0) || hasDraftError,
            ResolveModeSelectorPlaceholderLabels(showRemoteDirectoryPrompt)));

        return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Mode,
            policy.RealItems,
            policy.SelectedSemanticValue,
            policy.Placeholder,
            policy.ReplaceSelectionWithPlaceholder,
            policy.DisableRealItems,
            policy.SelectorEnabled && IsStartModeSelectorEnabled && IsInputEnabled));
    }

    private SelectorProjectionResult ResolveStartProjectSelectorProjection()
    {
        var selectedProjectId = SelectedStartProjectId;
        var hasLegalFallback = StartProjectOptions.Any(option =>
            string.Equals(option.ProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal));
        var policy = _projectSelectorPolicy.Project(new ProjectSelectorPolicyInput(
            $"project|{selectedProjectId}",
            StartProjectOptions,
            selectedProjectId,
            PendingProjectIntentResolved: HasSelectableProject(selectedProjectId)
                || string.Equals(selectedProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal),
            hasLegalFallback,
            ResolveProjectSelectorPlaceholderLabels()));

        return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Project,
            policy.RealItems,
            policy.SelectedSemanticValue,
            policy.Placeholder,
            policy.ReplaceSelectionWithPlaceholder,
            policy.DisableRealItems,
            policy.SelectorEnabled && IsInputEnabled));
    }

    private string BuildStartModeIdentity()
        => string.Join(
            "|",
            Chat.SelectedAcpProfile?.Id ?? string.Empty,
            Chat.ConnectionInstanceId ?? string.Empty,
            ResolvePreviewCwd() ?? string.Empty,
            StartModeOptions.Count.ToString(CultureInfo.InvariantCulture));

    private void RefreshAllSelectorProjections()
    {
        OnPropertyChanged(nameof(StartAgentSelectorProjection));
        OnPropertyChanged(nameof(StartModeSelectorProjection));
        OnPropertyChanged(nameof(StartProjectSelectorProjection));
        OnPropertyChanged(nameof(ComposerSelectorSlots));
        OnPropertyChanged(nameof(StartAgentSelectorItems));
        OnPropertyChanged(nameof(StartModeSelectorItems));
        OnPropertyChanged(nameof(StartProjectSelectorItems));
        OnPropertyChanged(nameof(SelectedStartAgentSelectorItem));
        OnPropertyChanged(nameof(SelectedStartModeSelectorItem));
        OnPropertyChanged(nameof(SelectedStartProjectSelectorItem));
        OnPropertyChanged(nameof(StartDraftAutomationState));
    }

    private void OnPreferencesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AppPreferencesViewModel.LastSelectedProjectId), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedStartProjectId));
            RefreshAllSelectorProjections();
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartSessionAndSendUi));
        }
    }

    private void OnNavigationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainNavigationViewModel.PendingProjectIdForNewSession), StringComparison.Ordinal))
        {
            return;
        }

        ApplyPendingProjectIntent();
    }

    private void ApplyPendingProjectIntent()
    {
        var pendingProjectId = _nav.PeekPendingProjectIdForNewSession();
        if (string.IsNullOrWhiteSpace(pendingProjectId))
        {
            return;
        }

        SelectedStartProjectId = pendingProjectId;
    }

    private void OnProjectPreferencesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshStartProjectOptions();
        QueueEnsureNewSessionDraft();
    }

    private void OnAgentRemoteDirectoriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshStartProjectOptions();
        QueueEnsureNewSessionDraft();
    }

    private string? ResolveDefaultCwd()
    {
        var selectedOption = ResolveSelectedProjectOption();
        if (!string.IsNullOrWhiteSpace(selectedOption?.RemoteCwd))
        {
            return selectedOption.RemoteCwd;
        }

        var selectedRoot = _projectPreferences.TryGetProjectRootPath(SelectedStartProjectId);
        _ = _nav.ConsumePendingProjectRootPath();
        return SessionCwdResolver.Resolve(selectedRoot, null);
    }

    private void RefreshStartProjectOptions()
    {
        var options = BuildStartProjectOptions();

        _startProjectOptions.Clear();
        foreach (var option in options)
        {
            _startProjectOptions.Add(option);
        }

        OnPropertyChanged(nameof(SelectedStartProjectId));
        RefreshAllSelectorProjections();
    }

    private bool IsSelectedProfileRemote()
        => Chat.SelectedAcpProfile?.Transport is TransportType.WebSocket or TransportType.HttpSse;

    private static string BuildRemoteDirectoryProjectId(string directoryId)
        => $"remote-directory:{directoryId}";

    private StartProjectOptionViewModel? ResolveSelectedProjectOption()
        => StartProjectOptions.FirstOrDefault(option =>
            string.Equals(option.ProjectId, SelectedStartProjectId, StringComparison.Ordinal));

    private bool IsRemoteDirectorySelectionRequiredForStart()
    {
        if (!IsSelectedProfileRemote())
        {
            return false;
        }

        var selectedOption = ResolveSelectedProjectOption();
        return string.IsNullOrWhiteSpace(selectedOption?.RemoteCwd);
    }

    private bool IsExpectedRemoteDirectorySelectionState()
    {
        if (!IsRemoteDirectorySelectionRequiredForStart())
        {
            return false;
        }

        return !Chat.HasNewSessionDraftError
            || string.Equals(
                Chat.NewSessionDraftErrorMessage,
                AcpSessionNewCwdResolver.MissingRemoteCwdMessage,
                StringComparison.Ordinal);
    }

    private IReadOnlyList<StartProjectOptionViewModel> BuildStartProjectOptions()
    {
        var isRemoteProfile = IsSelectedProfileRemote();
        var options = new List<StartProjectOptionViewModel>
        {
            new(NavigationProjectIds.Unclassified, Localize("Nav_Unclassified", "未归类"), isSelectable: !isRemoteProfile),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in _projectPreferences.Projects
                     .Where(IsSelectableProject)
                     .OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(project.ProjectId))
            {
                continue;
            }

            options.Add(new StartProjectOptionViewModel(project.ProjectId, project.Name, isSelectable: !isRemoteProfile));
        }

        if (isRemoteProfile)
        {
            foreach (var directory in _preferences.AgentRemoteDirectories
                         .Where(d => !string.IsNullOrWhiteSpace(d.DirectoryId) && !string.IsNullOrWhiteSpace(d.RemotePath))
                         .OrderBy(d => string.IsNullOrWhiteSpace(d.DisplayName) ? d.RemotePath : d.DisplayName, StringComparer.Ordinal))
            {
                options.Add(new StartProjectOptionViewModel(
                    BuildRemoteDirectoryProjectId(directory.DirectoryId),
                    string.IsNullOrWhiteSpace(directory.DisplayName) ? directory.RemotePath : directory.DisplayName,
                    isSelectable: true,
                    remoteCwd: directory.RemotePath));
            }
        }

        return options;
    }

    private string GetSelectedStartProjectId()
        => HasSelectableProject(_selectedStartProjectIdOverride) || HasSelectableOption(_selectedStartProjectIdOverride)
            ? _selectedStartProjectIdOverride!
            : string.Equals(_selectedStartProjectIdOverride, NavigationProjectIds.Unclassified, StringComparison.Ordinal)
                ? NavigationProjectIds.Unclassified
                : ResolveDefaultStartProjectId();

    private string ResolveDefaultStartProjectId()
    {
        var explicitProjectId = NormalizeProjectSelectionValue(_nav.PeekPendingProjectIdForNewSession());
        if (HasSelectableProject(explicitProjectId))
        {
            return explicitProjectId;
        }

        var recentProjectId = _conversationCatalog.Snapshot
            .Select(static conversation => conversation.ProjectAffinityOverrideProjectId)
            .FirstOrDefault(HasSelectableProject);
        return HasSelectableProject(recentProjectId)
            ? recentProjectId!
            : NavigationProjectIds.Unclassified;
    }

    private bool HasSelectableProject(string? projectId)
        => _projectPreferences.Projects.Any(project =>
            string.Equals(project.ProjectId, projectId, StringComparison.Ordinal)
            && IsSelectableProject(project));

    private bool HasSelectableOption(string? projectId)
        => !string.IsNullOrWhiteSpace(projectId)
            && _startProjectOptions.Any(option =>
                string.Equals(option.ProjectId, projectId, StringComparison.Ordinal)
                && option.IsSelectable);

    private static bool IsSelectableProject(ProjectDefinition? project)
        => project is not null
            && !string.IsNullOrWhiteSpace(project.ProjectId)
            && !string.IsNullOrWhiteSpace(project.Name)
            && !string.IsNullOrWhiteSpace(project.RootPath);

    private static string NormalizeProjectSelectionValue(string? projectId)
        => string.IsNullOrWhiteSpace(projectId)
            ? NavigationProjectIds.Unclassified
            : projectId;

    private ModeSelectorPlaceholderLabels ResolveModeSelectorPlaceholderLabels(bool remoteSelectionRequired = false)
        => new(
            Unresolved: remoteSelectionRequired
                ? Localize("Selector_Mode_RemoteSelectionRequired", "请选择远程工作目录")
                : Localize("Selector_Mode_Unresolved", "模式尚未就绪"),
            Loading: Localize("Selector_Mode_Loading", "正在加载模式..."),
            Error: Localize("Selector_Mode_Error", "模式不可用"),
            Default: Localize("Selector_Mode_Default", "默认模式"));

    private AgentSelectorPlaceholderLabels ResolveAgentSelectorPlaceholderLabels()
        => new(
            Loading: Localize("Selector_Agent_Loading", "正在连接 Agent..."),
            Error: Localize("Selector_Agent_Error", "Agent 不可用"),
            Unresolved: Localize("Selector_Agent_Unresolved", "选择 Agent"),
            Empty: Localize("Selector_Agent_Empty", "未选择 Agent"));

    private ProjectSelectorPlaceholderLabels ResolveProjectSelectorPlaceholderLabels()
        => new(
            Unresolved: Localize("Selector_Project_Unresolved", "项目不可用"),
            Fallback: Localize("Nav_Unclassified", "未归类"),
            RemoteSelectionRequired: Localize("Selector_Project_RemoteSelectionRequired", "请选择远程工作目录"));

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private async Task StartVoiceInputAsync()
    {
        if (!CanStartVoiceInput)
        {
            return;
        }

        await Chat.StartVoiceInputCommand.ExecuteAsync(null);
    }

    private async Task StopVoiceInputAsync()
    {
        if (!CanStopVoiceInput)
        {
            return;
        }

        await Chat.StopVoiceInputCommand.ExecuteAsync(null);
    }

    private void OnChatPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ChatViewModel.SelectedAcpProfile), StringComparison.Ordinal))
        {
            RefreshStartProjectOptions();
            RefreshStartSessionDraftErrorProjection();
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartSessionAndSendUi));
            if (_isComposerLoaded)
            {
                QueueEnsureNewSessionDraft();
            }
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.SelectedProfileIntentId), StringComparison.Ordinal))
        {
            if (_isComposerLoaded)
            {
                QueueEnsureNewSessionDraft();
            }

            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.SelectedNewSessionDraftMode), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedStartMode));
            RefreshAllSelectorProjections();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsNewSessionDraftLoading), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.IsNewSessionDraftReady), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.NewSessionDraftModeOptions), StringComparison.Ordinal))
        {
            if (Chat.IsNewSessionDraftReady)
            {
                SetNewSessionDraftRefreshPending(false);
            }

            RefreshStartModeProjection();
            RefreshStartSessionDraftErrorProjection();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.HasNewSessionDraftError), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.NewSessionDraftErrorMessage), StringComparison.Ordinal))
        {
            RefreshStartSessionDraftErrorProjection();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsConnected), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.IsConnecting), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.IsInitializing), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.ConnectionInstanceId), StringComparison.Ordinal))
        {
            if (_isComposerLoaded && (Chat.IsConnecting || Chat.IsInitializing))
            {
                SetNewSessionDraftRefreshPending(true);
                return;
            }

            if (_isComposerLoaded && Chat.IsConnected)
            {
                QueueEnsureNewSessionDraft();
                return;
            }

            SetNewSessionDraftRefreshPending(false);
            RefreshStartModeState();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.CurrentPrompt), StringComparison.Ordinal))
        {
            RefreshStartPromptProjection(Chat.CurrentPrompt ?? string.Empty);
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsVoiceInputListening), StringComparison.Ordinal))
        {
            RefreshVoiceProjection();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsVoiceInputSupported), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.CanStartVoiceInput), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.CanStopVoiceInput), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.ShowVoiceInputStartButton), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.ShowVoiceInputStopButton), StringComparison.Ordinal))
        {
            RefreshVoiceProjection();
        }
    }

    private void RefreshVoiceProjection()
    {
        OnPropertyChanged(nameof(IsVoiceInputSupported));
        OnPropertyChanged(nameof(CanStartVoiceInput));
        OnPropertyChanged(nameof(CanStopVoiceInput));
        OnPropertyChanged(nameof(ShowVoiceInputStartButton));
        OnPropertyChanged(nameof(ShowVoiceInputStopButton));
        StartVoiceInputCommand.NotifyCanExecuteChanged();
        StopVoiceInputCommand.NotifyCanExecuteChanged();
    }

    private void OnConversationCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(IConversationCatalogReadModel.Snapshot), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(IConversationCatalogReadModel.ConversationListVersion), StringComparison.Ordinal))
        {
            if (!HasSelectableProject(_selectedStartProjectIdOverride))
            {
                OnPropertyChanged(nameof(SelectedStartProjectId));
                RefreshAllSelectorProjections();
            }
        }
    }

    private void OnStartModeOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshStartModeProjection();

    private void RefreshStartModeProjection()
    {
        OnPropertyChanged(nameof(StartModeOptions));
        OnPropertyChanged(nameof(SelectedStartMode));
        RefreshAllSelectorProjections();
        RefreshStartModeState();
    }

    private void RefreshStartSessionDraftErrorProjection()
    {
        OnPropertyChanged(nameof(HasStartSessionDraftError));
        OnPropertyChanged(nameof(StartSessionDraftErrorMessage));
        RefreshAllSelectorProjections();
        OnPropertyChanged(nameof(StartDraftAutomationState));
    }

    private void QueueEnsureNewSessionDraft()
    {
        if (!_isComposerLoaded)
        {
            return;
        }

        _newSessionDraftCts?.Cancel();
        _newSessionDraftCts?.Dispose();
        _newSessionDraftCts = null;

        if (IsRemoteDirectorySelectionRequiredForStart())
        {
            SetNewSessionDraftRefreshPending(false);
            TrackNewSessionDraftDiscard(Chat.DiscardNewSessionDraftAsync());
            RefreshStartModeProjection();
            RefreshStartSessionDraftErrorProjection();
            return;
        }

        var refreshCts = new CancellationTokenSource();
        _newSessionDraftCts = refreshCts;
        SetNewSessionDraftRefreshPending(true);

        _ = EnsureNewSessionDraftAsync(refreshCts);
    }

    private async Task EnsureNewSessionDraftAsync(CancellationTokenSource refreshCts)
    {
        var cancellationToken = refreshCts.Token;

        try
        {
            var preferredProfileId = await Chat.ResolvePreferredNewSessionDraftProfileIdAsync(cancellationToken)
                .ConfigureAwait(true);
            await Chat.EnsureNewSessionDraftForProfileAsync(
                    ResolvePreviewCwd(),
                    preferredProfileId,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to prepare start-session draft.");
        }
        finally
        {
            if (ReferenceEquals(_newSessionDraftCts, refreshCts))
            {
                _newSessionDraftCts = null;
                SetNewSessionDraftRefreshPending(false);
            }

            refreshCts.Dispose();
        }
    }

    private void CancelNewSessionDraftRefresh()
    {
        var refreshCts = _newSessionDraftCts;
        _newSessionDraftCts = null;
        refreshCts?.Cancel();
        refreshCts?.Dispose();
        SetNewSessionDraftRefreshPending(false);
    }

    private void SetNewSessionDraftRefreshPending(bool value)
    {
        if (_isNewSessionDraftRefreshPending == value)
        {
            return;
        }

        _isNewSessionDraftRefreshPending = value;
        RefreshStartModeState();
        RefreshAllSelectorProjections();
    }

    private void RefreshStartModeState()
    {
        RefreshAllSelectorProjections();
        var nextSnapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: IsStarting,
            IsConnectionReady: Chat.IsConnected && !Chat.IsConnecting && !Chat.IsInitializing,
            IsDraftRefreshPending: _isNewSessionDraftRefreshPending,
            IsDraftLoading: Chat.IsNewSessionDraftLoading,
            IsDraftReady: Chat.IsNewSessionDraftReady,
            ModeCount: StartModeOptions.Count));
        if (nextSnapshot == _startSessionModeSnapshot)
        {
            return;
        }

        var previousSnapshot = _startSessionModeSnapshot;
        _startSessionModeSnapshot = nextSnapshot;

        if (previousSnapshot.Stage != nextSnapshot.Stage)
        {
            OnPropertyChanged(nameof(StartModeStage));
        }

        if (previousSnapshot.IsEnabled != nextSnapshot.IsEnabled)
        {
            OnPropertyChanged(nameof(IsStartModeSelectorEnabled));
            OnPropertyChanged(nameof(ComposerSelectorSlots));
        }

        if (previousSnapshot.CanSubmitPrompt != nextSnapshot.CanSubmitPrompt)
        {
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartSessionAndSendUi));
        }
    }

    private string? ResolvePreviewCwd()
    {
        var selectedOption = ResolveSelectedProjectOption();
        if (!string.IsNullOrWhiteSpace(selectedOption?.RemoteCwd))
        {
            return selectedOption.RemoteCwd;
        }

        return _projectPreferences.TryGetProjectRootPath(SelectedStartProjectId);
    }

    private sealed class NoOpConversationCatalogReadModel : IConversationCatalogReadModel
    {
        public static NoOpConversationCatalogReadModel Instance { get; } = new();

        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 0;

        public IReadOnlyList<ConversationCatalogItem> Snapshot { get; } = Array.Empty<ConversationCatalogItem>();

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }
    }
}
