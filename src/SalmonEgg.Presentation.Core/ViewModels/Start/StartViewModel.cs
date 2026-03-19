using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed partial class StartViewModel : ObservableObject
{
    private readonly ISessionManager _sessionManager;
    private readonly AppPreferencesViewModel _preferences;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly MainNavigationViewModel _nav;
    private readonly ILogger<StartViewModel> _logger;

    public ChatViewModel Chat { get; }

    private bool _isStarting;

    public bool IsStarting
    {
        get => _isStarting;
        set => SetProperty(ref _isStarting, value);
    }

    public IAsyncRelayCommand StartSessionAndSendCommand { get; }

    public StartViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        INavigationCoordinator navigationCoordinator,
        MainNavigationViewModel nav,
        ILogger<StartViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        StartSessionAndSendCommand = new AsyncRelayCommand(StartSessionAndSendAsync, () => !IsStarting);
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

            var switched = await Chat.TrySwitchToSessionAsync(sessionId).ConfigureAwait(true);
            if (!switched)
            {
                _logger.LogWarning("Start session failed: unable to switch to new session (SessionId={SessionId})", sessionId);
                return;
            }

            await _navigationCoordinator.ActivateSessionAsync(sessionId, _preferences.LastSelectedProjectId).ConfigureAwait(true);

            if (!Chat.IsConnected)
            {
                await Chat.TryAutoConnectAsync().ConfigureAwait(true);
            }

            if (!Chat.IsConnected)
            {
                if (Chat.IsConnecting || Chat.IsInitializing)
                {
                    _logger.LogInformation("Start session paused: connection is still in progress.");
                    return;
                }

                await _navigationCoordinator.ActivateSettingsAsync("General").ConfigureAwait(true);
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
