using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Narrow chat facade for the Start launch workflow.
/// Intentionally does not expose session switching so NavigationCoordinator remains the single switch owner.
/// </summary>
public interface IChatLaunchWorkflowChatFacade
{
    string? CurrentSessionId { get; }

    bool IsConnected { get; }

    bool IsConnecting { get; }

    bool IsInitializing { get; }

    bool ShowTransportConfigPanel { get; set; }

    Task TryAutoConnectAsync(CancellationToken cancellationToken = default);

    bool CanSendPrompt();

    void SendPrompt();

    Task<ChatLaunchConnectionOutcome> EnsureConnectedForLaunchAsync(CancellationToken cancellationToken = default);

    bool TrySendPromptForLaunch();
}

public enum ChatLaunchConnectionOutcome
{
    Connected,
    InProgress,
    RequiresConfiguration
}

public sealed class ChatLaunchWorkflow : IChatLaunchWorkflow
{
    private readonly IChatLaunchWorkflowChatFacade _chat;
    private readonly ISessionManager _sessionManager;
    private readonly AppPreferencesViewModel _preferences;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly Func<string?> _resolveDefaultCwd;
    private readonly ILogger<ChatLaunchWorkflow> _logger;

    public ChatLaunchWorkflow(
        IChatLaunchWorkflowChatFacade chat,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        INavigationCoordinator navigationCoordinator,
        Func<string?> resolveDefaultCwd,
        ILogger<ChatLaunchWorkflow>? logger = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _resolveDefaultCwd = resolveDefaultCwd ?? throw new ArgumentNullException(nameof(resolveDefaultCwd));
        _logger = logger ?? NullLogger<ChatLaunchWorkflow>.Instance;
    }

    public async Task StartSessionAndSendAsync(string promptText, CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = (promptText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var cwd = _resolveDefaultCwd();

        try
        {
            await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(true);
        }
        catch
        {
            sessionId = Guid.NewGuid().ToString("N");
            await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(true);
        }

        // Navigation owns the session switch for the Start path.
        // Calling Chat.TrySwitchToSessionAsync here would reintroduce the current double-owner bug.
        var activated = await _navigationCoordinator
            .ActivateSessionAsync(sessionId, _preferences.LastSelectedProjectId)
            .ConfigureAwait(true);
        if (!activated)
        {
            _logger.LogWarning("Start workflow stopped: navigation activation failed (SessionId={SessionId})", sessionId);
            return;
        }

        var connectionOutcome = await _chat.EnsureConnectedForLaunchAsync(cancellationToken).ConfigureAwait(true);
        switch (connectionOutcome)
        {
            case ChatLaunchConnectionOutcome.Connected:
                break;

            case ChatLaunchConnectionOutcome.InProgress:
                _logger.LogInformation("Start workflow paused: connection is still in progress.");
                return;

            case ChatLaunchConnectionOutcome.RequiresConfiguration:
                await _navigationCoordinator.ActivateSettingsAsync("General").ConfigureAwait(true);
                _chat.ShowTransportConfigPanel = true;
                return;

            default:
                return;
        }

        if (_chat.TrySendPromptForLaunch())
        {
            return;
        }
    }
}

public sealed class ChatLaunchWorkflowChatFacadeAdapter : IChatLaunchWorkflowChatFacade
{
    private readonly ChatViewModel _chatViewModel;

    public ChatLaunchWorkflowChatFacadeAdapter(ChatViewModel chatViewModel)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
    }

    public string? CurrentSessionId => _chatViewModel.CurrentSessionId;

    public bool IsConnected => _chatViewModel.IsConnected;

    public bool IsConnecting => _chatViewModel.IsConnecting;

    public bool IsInitializing => _chatViewModel.IsInitializing;

    public bool ShowTransportConfigPanel
    {
        get => _chatViewModel.ShowTransportConfigPanel;
        set => _chatViewModel.ShowTransportConfigPanel = value;
    }

    public Task TryAutoConnectAsync(CancellationToken cancellationToken = default) =>
        _chatViewModel.TryAutoConnectAsync(cancellationToken);

    public bool CanSendPrompt() => _chatViewModel.SendPromptCommand?.CanExecute(null) == true;

    public void SendPrompt()
    {
        if (_chatViewModel.SendPromptCommand?.CanExecute(null) == true)
        {
            _chatViewModel.SendPromptCommand.Execute(null);
        }
    }

    public async Task<ChatLaunchConnectionOutcome> EnsureConnectedForLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (_chatViewModel.IsConnected)
        {
            return ChatLaunchConnectionOutcome.Connected;
        }

        await _chatViewModel.TryAutoConnectAsync(cancellationToken).ConfigureAwait(true);

        if (_chatViewModel.IsConnected)
        {
            return ChatLaunchConnectionOutcome.Connected;
        }

        return _chatViewModel.IsConnecting || _chatViewModel.IsInitializing
            ? ChatLaunchConnectionOutcome.InProgress
            : ChatLaunchConnectionOutcome.RequiresConfiguration;
    }

    public bool TrySendPromptForLaunch()
    {
        if (_chatViewModel.SendPromptCommand?.CanExecute(null) != true)
        {
            return false;
        }

        _chatViewModel.SendPromptCommand.Execute(null);
        return true;
    }
}
