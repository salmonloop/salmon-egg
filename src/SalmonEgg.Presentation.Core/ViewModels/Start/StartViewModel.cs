using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly ILogger<StartViewModel> _logger;
    private readonly ObservableCollection<StartProjectOptionViewModel> _startProjectOptions = new();
    private StartComposerState _composerState = StartComposerState.Default;
    private StartComposerSnapshot _composerSnapshot = StartComposerPolicy.Compute(StartComposerState.Default);

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

    public IRelayCommand<QuickSuggestionViewModel> ExecuteSuggestionCommand { get; }

    public bool IsInputEnabled => !IsStarting;

    public string SelectedStartProjectId
    {
        get => GetSelectedStartProjectId();
        set
        {
            var normalizedSelection = NormalizeProjectSelectionValue(value);
            if (string.Equals(GetSelectedStartProjectId(), normalizedSelection, StringComparison.Ordinal))
            {
                return;
            }

            _projectSelectionStore.RememberSelectedProject(normalizedSelection);
        }
    }

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
        IChatConnectionStore? chatConnectionStore = null)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        ArgumentNullException.ThrowIfNull(sessionManager);
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        ArgumentNullException.ThrowIfNull(navigationCoordinator);
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        InitializeSuggestions();
        RefreshStartProjectOptions();
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        ((INotifyCollectionChanged)_projectPreferences.Projects).CollectionChanged += OnProjectPreferencesChanged;
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
        DispatchComposerAction(new Loaded());
        DispatchComposerAction(new DraftChanged(!string.IsNullOrWhiteSpace(StartPrompt)));
    }

    public void OnComposerUnloaded() => DispatchComposerAction(new Unloaded());

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
            await _chatLaunchWorkflow.StartSessionAndSendAsync(promptText).ConfigureAwait(true);
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
    }

    private string? ResolveDefaultCwd()
    {
        var pending = _nav.ConsumePendingProjectRootPath();
        var lastSelectedRoot = _projectPreferences.TryGetProjectRootPath(_projectPreferences.LastSelectedProjectId ?? string.Empty);

        // Fallback: if no project selected, keep it unclassified.
        return SessionCwdResolver.Resolve(pending, lastSelectedRoot);
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
    {
        var selectedProjectId = _projectPreferences.LastSelectedProjectId;
        return HasSelectableProject(selectedProjectId)
            ? selectedProjectId!
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
}
