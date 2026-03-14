using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed partial class StartViewModel : ObservableObject
{
    private readonly ISessionManager _sessionManager;
    private readonly AppPreferencesViewModel _preferences;
    private readonly IShellNavigationService _shellNavigation;
    private readonly MainNavigationViewModel _nav;
    private readonly ILogger<StartViewModel> _logger;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;

    public ChatViewModel Chat { get; }

    private bool _isStarting;

    public bool IsStarting
    {
        get => _isStarting;
        set => SetProperty(ref _isStarting, value);
    }

    public IAsyncRelayCommand StartSessionAndSendCommand { get; }

    public ObservableCollection<ProjectOptionViewModel> ProjectOptions { get; } = new();

    [ObservableProperty]
    private ProjectOptionViewModel? _selectedProjectOption;

    public StartViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        IShellNavigationService shellNavigation,
        MainNavigationViewModel nav,
        ILogger<StartViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _shellNavigation = shellNavigation ?? throw new ArgumentNullException(nameof(shellNavigation));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        StartSessionAndSendCommand = new AsyncRelayCommand(StartSessionAndSendAsync, () => !IsStarting);

        _projectsChangedHandler = (_, _) => RefreshProjectOptions();
        _preferences.Projects.CollectionChanged += _projectsChangedHandler;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        RefreshProjectOptions();
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPreferencesViewModel.LastSelectedProjectId))
        {
            SyncSelectedProjectOption();
        }
    }

    private void RefreshProjectOptions()
    {
        ProjectOptions.Clear();
        ProjectOptions.Add(new ProjectOptionViewModel(null, "未归类", null));

        foreach (var project in _preferences.Projects.Where(p => p != null))
        {
            ProjectOptions.Add(new ProjectOptionViewModel(project.ProjectId, project.Name, project.RootPath));
        }

        SyncSelectedProjectOption();
    }

    private void SyncSelectedProjectOption()
    {
        var targetId = _preferences.LastSelectedProjectId;
        var match = ProjectOptions.FirstOrDefault(p => string.Equals(p.ProjectId, targetId, StringComparison.Ordinal));
        SelectedProjectOption = match ?? ProjectOptions.FirstOrDefault();
    }

    partial void OnSelectedProjectOptionChanged(ProjectOptionViewModel? value)
    {
        _preferences.LastSelectedProjectId = string.IsNullOrWhiteSpace(value?.ProjectId)
            ? null
            : value.ProjectId;
    }

    private async Task StartSessionAndSendAsync()
    {
        var promptText = (Chat.CurrentPrompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return;
        }

        IsStarting = true;
        StartSessionAndSendCommand.NotifyCanExecuteChanged();
        try
        {
            var cwd = ResolveDefaultCwd();
            var sessionId = Guid.NewGuid().ToString("N");

            try
            {
                await _sessionManager.CreateSessionAsync(sessionId, cwd);
            }
            catch
            {
                // If somehow collides, fall back to another id.
                sessionId = Guid.NewGuid().ToString("N");
                await _sessionManager.CreateSessionAsync(sessionId, cwd);
            }

            await Chat.TrySwitchToSessionAsync(sessionId).ConfigureAwait(true);

            _shellNavigation.NavigateToChat();
            _nav.SelectSession(sessionId);

            if (!Chat.IsConnected)
            {
                await Chat.TryAutoConnectAsync().ConfigureAwait(true);
            }

            if (!Chat.IsConnected)
            {
                _shellNavigation.NavigateToSettings("General");
                Chat.ShowTransportConfigPanel = true;
                return;
            }

            if (Chat.SendPromptCommand != null && Chat.SendPromptCommand.CanExecute(null))
            {
                Chat.SendPromptCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Start session failed");
        }
        finally
        {
            IsStarting = false;
            StartSessionAndSendCommand.NotifyCanExecuteChanged();
        }
    }

    private string? ResolveDefaultCwd()
    {
        var pending = _nav.ConsumePendingProjectRootPath();
        string? lastSelectedRoot = null;

        var projectId = _preferences.LastSelectedProjectId;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = _preferences.Projects.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.Ordinal));
            if (project != null && !string.IsNullOrWhiteSpace(project.RootPath))
            {
                lastSelectedRoot = project.RootPath;
            }
        }

        // Fallback: if no project selected, keep it unclassified.
        return SessionCwdResolver.Resolve(pending, lastSelectedRoot);
    }
}
