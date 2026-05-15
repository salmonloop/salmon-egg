using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
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
    private readonly ILogger<StartViewModel> _logger;
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

    public IRelayCommand<QuickSuggestionViewModel> ExecuteSuggestionCommand { get; }

    public IRelayCommand<SessionModeViewModel?> SelectStartModeCommand { get; }

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
        IConversationCatalogReadModel? conversationCatalog = null)
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
        StartProjectOptions = new ReadOnlyObservableCollection<StartProjectOptionViewModel>(_startProjectOptions);
        _chatLaunchWorkflow = chatLaunchWorkflow ?? new ChatLaunchWorkflow(
            new ChatLaunchWorkflowChatFacadeAdapter(
                Chat,
                chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore))),
            sessionManager,
            _preferences,
            navigationCoordinator,
            ResolveDefaultCwd);

        StartSessionAndSendCommand = new AsyncRelayCommand(StartSessionAndSendAsync, CanStartSessionAndSend);
        ExecuteSuggestionCommand = new RelayCommand<QuickSuggestionViewModel>(ExecuteSuggestion);
        SelectStartModeCommand = new RelayCommand<SessionModeViewModel?>(SelectStartMode);
        StartVoiceInputCommand = new AsyncRelayCommand(StartVoiceInputAsync, () => CanStartVoiceInput);
        StopVoiceInputCommand = new AsyncRelayCommand(StopVoiceInputAsync, () => CanStopVoiceInput);

        InitializeSuggestions();
        RefreshStartProjectOptions();
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _nav.PropertyChanged += OnNavigationPropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += OnProjectPreferencesChanged;
        _conversationCatalog.PropertyChanged += OnConversationCatalogPropertyChanged;
        Chat.PropertyChanged += OnChatPropertyChanged;
        ((INotifyCollectionChanged)Chat.NewSessionDraftModeOptions).CollectionChanged += OnStartModeOptionsChanged;
        ApplyPendingProjectIntent();
    }

    private void InitializeSuggestions()
    {
        Suggestions.Add(new QuickSuggestionViewModel("\uE943", "分析代码库", "深入理解项目架构与逻辑", "请帮我分析一下当前代码库的架构和核心逻辑。", ExecuteSuggestionCommand));
        Suggestions.Add(new QuickSuggestionViewModel("\uE762", "推荐开发任务", "明确接下来该做什么", "根据当前进度，推荐几个接下来可以进行的开发任务或优化点。", ExecuteSuggestionCommand));
        Suggestions.Add(new QuickSuggestionViewModel("\uEBE8", "解决最近报错", "提交错误日志让我看看", "我刚才遇到了一些报错，请帮我分析并解决它们。", ExecuteSuggestionCommand));
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
        TrackComposerUnloadCleanup(Chat.DiscardNewSessionDraftAsync());
    }

    private void TrackComposerUnloadCleanup(Task cleanupTask)
    {
        _composerUnloadCleanupTask = cleanupTask;
        _composerUnloadCleanupObservationTask = cleanupTask.ContinueWith(
            static (task, state) =>
            {
                var logger = (ILogger<StartViewModel>)state!;
                logger.LogWarning(
                    task.Exception,
                    "Failed to discard ACP new-session draft when the start composer unloaded.");
            },
            _logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task StartSessionAndSendAsync()
    {
        var promptText = (StartPrompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptText))
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
        => !IsStarting && !string.IsNullOrWhiteSpace(StartPrompt);

    private void OnPreferencesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AppPreferencesViewModel.LastSelectedProjectId), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedStartProjectId));
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

    private string? ResolveDefaultCwd()
    {
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
    }

    private IReadOnlyList<StartProjectOptionViewModel> BuildStartProjectOptions()
    {
        var options = new List<StartProjectOptionViewModel>
        {
            new(NavigationProjectIds.Unclassified, "未归类"),
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

            options.Add(new StartProjectOptionViewModel(project.ProjectId, project.Name));
        }

        return options;
    }

    private string GetSelectedStartProjectId()
        => HasSelectableProject(_selectedStartProjectIdOverride)
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

    private static bool IsSelectableProject(ProjectDefinition? project)
        => project is not null
            && !string.IsNullOrWhiteSpace(project.ProjectId)
            && !string.IsNullOrWhiteSpace(project.Name)
            && !string.IsNullOrWhiteSpace(project.RootPath);

    private static string NormalizeProjectSelectionValue(string? projectId)
        => string.IsNullOrWhiteSpace(projectId)
            ? NavigationProjectIds.Unclassified
            : projectId;

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
            if (_isComposerLoaded)
            {
                QueueEnsureNewSessionDraft();
            }
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.SelectedNewSessionDraftMode), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedStartMode));
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsNewSessionDraftLoading), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.IsNewSessionDraftReady), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.NewSessionDraftModeOptions), StringComparison.Ordinal))
        {
            RefreshStartModeProjection();
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
            }
        }
    }

    private void OnStartModeOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshStartModeProjection();

    private void RefreshStartModeProjection()
    {
        OnPropertyChanged(nameof(StartModeOptions));
        OnPropertyChanged(nameof(SelectedStartMode));
        RefreshStartModeState();
    }

    private void QueueEnsureNewSessionDraft()
    {
        if (!_isComposerLoaded)
        {
            return;
        }

        _newSessionDraftCts?.Cancel();
        _newSessionDraftCts?.Dispose();
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
            await Chat.EnsureNewSessionDraftForProfileAsync(
                    ResolvePreviewCwd(),
                    Chat.SelectedAcpProfile?.Id,
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
    }

    private void RefreshStartModeState()
    {
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
        }
    }

    private string? ResolvePreviewCwd()
        => _projectPreferences.TryGetProjectRootPath(SelectedStartProjectId);

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
