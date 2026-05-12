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
    private StartComposerState _composerState = StartComposerState.Default;
    private StartComposerSnapshot _composerSnapshot = StartComposerPolicy.Compute(StartComposerState.Default);
    private string? _selectedStartProjectIdOverride;
    private bool _suppressMirroredChatDraftUpdates;
    private bool _isVoicePromptBridgeActive;
    private string? _chatDraftBeforeVoiceBridge;
    private CancellationTokenSource? _newSessionDraftCts;
    private bool _isComposerLoaded;

    public ChatViewModel Chat { get; }

    private bool _isStarting;

    public bool IsStarting
    {
        get => _isStarting;
        set
        {
            if (SetProperty(ref _isStarting, value))
            {
                OnPropertyChanged(nameof(IsInputEnabled));
                OnPropertyChanged(nameof(IsStartModeSelectorEnabled));
                RefreshVoiceProjection();
                StartSessionAndSendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [ObservableProperty]
    private string _startPrompt = string.Empty;

    public StartComposerStage ComposerStage => _composerSnapshot.Stage;

    public bool IsComposerExpanded => _composerSnapshot.IsExpanded;

    public bool ShowHeroSuggestions => _composerSnapshot.ShowHeroSuggestions;

    public bool ShowPreflightSuggestions => _composerSnapshot.ShowPreflightSuggestions;

    public bool ShowHeroChrome => _composerSnapshot.ShowHeroChrome;

    public bool FreezeComposerInteractions => _composerSnapshot.FreezeComposerInteractions;

    public IAsyncRelayCommand StartSessionAndSendCommand { get; }

    public System.Collections.ObjectModel.ObservableCollection<QuickSuggestionViewModel> Suggestions { get; } = new();

    public ReadOnlyObservableCollection<StartProjectOptionViewModel> StartProjectOptions { get; }

    public ReadOnlyObservableCollection<SessionModeViewModel> StartModeOptions => Chat.NewSessionDraftModeOptions;

    public IRelayCommand<QuickSuggestionViewModel> ExecuteSuggestionCommand { get; }

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
            OnPropertyChanged(nameof(SelectedStartProjectId));
            QueueEnsureNewSessionDraft();
        }
    }

    public SessionModeViewModel? SelectedStartMode
    {
        get => Chat.SelectedNewSessionDraftMode;
        set => Chat.SelectedNewSessionDraftMode = value;
    }

    public bool IsStartModeSelectorVisible => StartModeOptions.Count > 0;

    public bool IsStartModeSelectorEnabled => !IsStarting && !Chat.IsNewSessionDraftLoading && StartModeOptions.Count > 0;

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
        StartVoiceInputCommand = new AsyncRelayCommand(StartVoiceInputAsync, () => CanStartVoiceInput);
        StopVoiceInputCommand = new AsyncRelayCommand(StopVoiceInputAsync, () => CanStopVoiceInput);

        InitializeSuggestions();
        RefreshStartProjectOptions();
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += OnProjectPreferencesChanged;
        _conversationCatalog.PropertyChanged += OnConversationCatalogPropertyChanged;
        Chat.PropertyChanged += OnChatPropertyChanged;
        ((INotifyCollectionChanged)Chat.NewSessionDraftModeOptions).CollectionChanged += OnStartModeOptionsChanged;
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
        OnComposerSuggestionApplied();
        StartPrompt = suggestion.Prompt;
    }

    public void OnComposerLoaded()
    {
        _isComposerLoaded = true;
        DispatchComposerAction(new Loaded());
        DispatchComposerAction(new DraftChanged(!string.IsNullOrWhiteSpace(StartPrompt)));
        OnPropertyChanged(nameof(SelectedStartProjectId));
        QueueEnsureNewSessionDraft();
    }

    public void OnComposerUnloaded()
    {
        _isComposerLoaded = false;
        CancelNewSessionDraftRefresh();
        _ = Chat.DiscardNewSessionDraftAsync();
        DispatchComposerAction(new Unloaded());
    }

    public void OnComposerActivated() => DispatchComposerAction(new Activated());

    public void OnComposerFocusEntered() => DispatchComposerAction(new FocusEntered());

    public void OnComposerFocusExited() => DispatchComposerAction(new FocusExited());

    public void OnComposerPopupOpened() => DispatchComposerAction(new PopupOpened());

    public void OnComposerPopupClosed() => DispatchComposerAction(new PopupClosed());

    public void OnComposerPopupClosedWithFocusState(bool focusWithinComposer)
    {
        DispatchComposerAction(new PopupClosed());
        DispatchComposerAction(focusWithinComposer ? new FocusEntered() : new FocusExited());
    }

    public void OnComposerOutsidePointerPressed() => DispatchComposerAction(new OutsidePointerPressed());

    public void OnComposerSuggestionApplied() => DispatchComposerAction(new SuggestionApplied());

    private async Task StartSessionAndSendAsync()
    {
        var promptText = (StartPrompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return;
        }

        DispatchComposerAction(new SubmitStarted());
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
            DispatchComposerAction(new SubmitCompleted());
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnStartPromptChanged(string value)
    {
        DispatchComposerAction(new DraftChanged(!string.IsNullOrWhiteSpace(value)));
        StartSessionAndSendCommand.NotifyCanExecuteChanged();
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

        BeginVoicePromptBridge();
        await Chat.StartVoiceInputCommand.ExecuteAsync(null);
        if (!Chat.IsVoiceInputListening)
        {
            EndVoicePromptBridge();
        }
    }

    private async Task StopVoiceInputAsync()
    {
        if (!CanStopVoiceInput)
        {
            return;
        }

        await Chat.StopVoiceInputCommand.ExecuteAsync(null);
        EndVoicePromptBridge();
    }

    private void BeginVoicePromptBridge()
    {
        if (_isVoicePromptBridgeActive)
        {
            return;
        }

        _chatDraftBeforeVoiceBridge = Chat.CurrentPrompt;
        _isVoicePromptBridgeActive = true;
        PushStartDraftToChat();
    }

    private void EndVoicePromptBridge()
    {
        if (!_isVoicePromptBridgeActive)
        {
            return;
        }

        PullChatDraftToStart();
        RestoreChatDraftAfterVoiceBridge();
        _isVoicePromptBridgeActive = false;
        _chatDraftBeforeVoiceBridge = null;
    }

    private void RestoreChatDraftAfterVoiceBridge()
    {
        _suppressMirroredChatDraftUpdates = true;
        try
        {
            Chat.CurrentPrompt = _chatDraftBeforeVoiceBridge ?? string.Empty;
        }
        finally
        {
            _suppressMirroredChatDraftUpdates = false;
        }
    }

    private void PushStartDraftToChat()
    {
        _suppressMirroredChatDraftUpdates = true;
        try
        {
            Chat.CurrentPrompt = StartPrompt ?? string.Empty;
        }
        finally
        {
            _suppressMirroredChatDraftUpdates = false;
        }
    }

    private void PullChatDraftToStart()
    {
        if (_suppressMirroredChatDraftUpdates)
        {
            return;
        }

        StartPrompt = Chat.CurrentPrompt ?? string.Empty;
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

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.CurrentPrompt), StringComparison.Ordinal)
            && _isVoicePromptBridgeActive)
        {
            PullChatDraftToStart();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ChatViewModel.IsVoiceInputListening), StringComparison.Ordinal))
        {
            if (!Chat.IsVoiceInputListening)
            {
                EndVoicePromptBridge();
            }

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
        OnPropertyChanged(nameof(IsStartModeSelectorVisible));
        OnPropertyChanged(nameof(IsStartModeSelectorEnabled));
    }

    private void QueueEnsureNewSessionDraft()
    {
        if (!_isComposerLoaded)
        {
            return;
        }

        _ = EnsureNewSessionDraftAsync();
    }

    private async Task EnsureNewSessionDraftAsync()
    {
        _newSessionDraftCts?.Cancel();
        _newSessionDraftCts?.Dispose();
        _newSessionDraftCts = new CancellationTokenSource();
        var cancellationToken = _newSessionDraftCts.Token;

        try
        {
            await Chat.EnsureNewSessionDraftAsync(ResolvePreviewCwd(), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to prepare start-session draft.");
        }
    }

    private void CancelNewSessionDraftRefresh()
    {
        _newSessionDraftCts?.Cancel();
        _newSessionDraftCts?.Dispose();
        _newSessionDraftCts = null;
        OnPropertyChanged(nameof(IsStartModeSelectorEnabled));
    }

    private string? ResolvePreviewCwd()
        => _projectPreferences.TryGetProjectRootPath(SelectedStartProjectId);

    private void DispatchComposerAction(StartComposerAction action)
    {
        var nextState = StartComposerReducer.Reduce(_composerState, action);
        if (nextState == _composerState)
        {
            return;
        }

        _composerState = nextState;
        UpdateComposerSnapshot();
    }

    private void UpdateComposerSnapshot()
    {
        var nextSnapshot = StartComposerPolicy.Compute(_composerState);
        if (nextSnapshot == _composerSnapshot)
        {
            return;
        }

        var previousSnapshot = _composerSnapshot;
        _composerSnapshot = nextSnapshot;

        if (previousSnapshot.Stage != nextSnapshot.Stage)
        {
            OnPropertyChanged(nameof(ComposerStage));
        }

        if (previousSnapshot.IsExpanded != nextSnapshot.IsExpanded)
        {
            OnPropertyChanged(nameof(IsComposerExpanded));
        }

        if (previousSnapshot.ShowHeroSuggestions != nextSnapshot.ShowHeroSuggestions)
        {
            OnPropertyChanged(nameof(ShowHeroSuggestions));
        }

        if (previousSnapshot.ShowPreflightSuggestions != nextSnapshot.ShowPreflightSuggestions)
        {
            OnPropertyChanged(nameof(ShowPreflightSuggestions));
        }

        if (previousSnapshot.ShowHeroChrome != nextSnapshot.ShowHeroChrome)
        {
            OnPropertyChanged(nameof(ShowHeroChrome));
        }

        if (previousSnapshot.FreezeComposerInteractions != nextSnapshot.FreezeComposerInteractions)
        {
            OnPropertyChanged(nameof(FreezeComposerInteractions));
        }
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
