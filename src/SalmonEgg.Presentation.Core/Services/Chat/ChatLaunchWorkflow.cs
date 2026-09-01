using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Narrow chat facade for the Start launch workflow.
/// Intentionally does not expose session switching so NavigationCoordinator remains the single switch owner.
/// </summary>
public interface IChatLaunchWorkflowChatFacade
{
    bool ShowTransportConfigPanel { get; set; }

    Task<ChatLaunchConnectionOutcome> EnsureConnectedForLaunchAsync(CancellationToken cancellationToken = default);

    Task PromoteNewSessionDraftForLaunchAsync(CancellationToken cancellationToken = default);

    void PrepareDraftForLaunch(string promptText);

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
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly MainNavigationViewModel? _navigationViewModel;
    private readonly ConversationCatalogFacade? _catalogFacade;
    private readonly ILogger<ChatLaunchWorkflow> _logger;

    public ChatLaunchWorkflow(
        IChatLaunchWorkflowChatFacade chat,
        ISessionManager sessionManager,
        INavigationCoordinator navigationCoordinator,
        ILogger<ChatLaunchWorkflow>? logger = null,
        ConversationCatalogFacade? catalogFacade = null,
        MainNavigationViewModel? navigationViewModel = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _logger = logger ?? NullLogger<ChatLaunchWorkflow>.Instance;
        _catalogFacade = catalogFacade;
        _navigationViewModel = navigationViewModel;
    }

    public async Task<ChatLaunchCompletion> StartSessionAndSendAsync(
        ChatLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedPrompt = (request.PromptText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return ChatLaunchCompletion.Incomplete;
        }

        var cwd = request.Cwd;
        if (string.IsNullOrWhiteSpace(cwd))
        {
            // Defense in depth: callers resolve the cwd through AcpSessionNewCwdResolver and reject
            // an unresolved one with the resolver's own reason before reaching here. This guard only
            // keeps the public contract honest for a caller that skips that step, since the session
            // manager rejects an empty cwd and retrying cannot make a root appear.
            _logger.LogWarning("Start workflow stopped: no working directory resolved for the launch.");
            return ChatLaunchCompletion.Failed;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        try
        {
            await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(true);
        }
        catch
        {
            sessionId = Guid.NewGuid().ToString("N");
            await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(true);
        }

        if (_catalogFacade != null)
        {
            await _catalogFacade.RegisterConversationAsync(sessionId, cancellationToken).ConfigureAwait(true);
        }

        // Navigation owns the session switch for the Start path.
        // Calling chat activation directly here would reintroduce the current double-owner bug.
        var activated = await _navigationCoordinator
            .ActivateSessionAsync(sessionId, request.ProjectId)
            .ConfigureAwait(true);
        if (!activated)
        {
            _logger.LogWarning("Start workflow stopped: navigation activation failed (SessionId={SessionId})", sessionId);
            return ChatLaunchCompletion.Failed;
        }

        var connectionOutcome = await _chat.EnsureConnectedForLaunchAsync(cancellationToken).ConfigureAwait(true);
        switch (connectionOutcome)
        {
            case ChatLaunchConnectionOutcome.Connected:
                break;

            case ChatLaunchConnectionOutcome.InProgress:
                _logger.LogInformation("Start workflow paused: connection is still in progress.");
                return ChatLaunchCompletion.Incomplete;

            case ChatLaunchConnectionOutcome.RequiresConfiguration:
                // Prefer the navigation VM owner so settings activation failures surface ShowInfo.
                // Fall back to the coordinator only for lean unit fixtures that omit the VM.
                if (_navigationViewModel is not null)
                {
                    await _navigationViewModel
                        .ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey)
                        .ConfigureAwait(true);
                }
                else
                {
                    await _navigationCoordinator
                        .ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey)
                        .ConfigureAwait(true);
                }

                _chat.ShowTransportConfigPanel = true;
                return ChatLaunchCompletion.Incomplete;

            default:
                return ChatLaunchCompletion.Failed;
        }

        _chat.PrepareDraftForLaunch(normalizedPrompt);
        await _chat.PromoteNewSessionDraftForLaunchAsync(cancellationToken).ConfigureAwait(true);
        return _chat.TrySendPromptForLaunch()
            ? ChatLaunchCompletion.PromptDispatched
            : ChatLaunchCompletion.Failed;
    }
}

public sealed class ChatLaunchWorkflowChatFacadeAdapter : IChatLaunchWorkflowChatFacade
{
    private readonly ChatViewModel _chatViewModel;
    private readonly IChatConnectionStore? _connectionStore;

    public ChatLaunchWorkflowChatFacadeAdapter(ChatViewModel chatViewModel)
        : this(chatViewModel, connectionStore: null)
    {
    }

    public ChatLaunchWorkflowChatFacadeAdapter(
        ChatViewModel chatViewModel,
        IChatConnectionStore? connectionStore)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _connectionStore = connectionStore;
    }

    public bool ShowTransportConfigPanel
    {
        get => _chatViewModel.ShowTransportConfigPanel;
        set => _chatViewModel.ShowTransportConfigPanel = value;
    }

    public void PrepareDraftForLaunch(string promptText)
    {
        _chatViewModel.CurrentPrompt = promptText ?? string.Empty;
    }

    public async Task<ChatLaunchConnectionOutcome> EnsureConnectedForLaunchAsync(CancellationToken cancellationToken = default)
    {
        var connectionState = await ReadConnectionStateAsync().ConfigureAwait(true);
        if (connectionState.Phase == ConnectionPhase.Connected)
        {
            return ChatLaunchConnectionOutcome.Connected;
        }

        await _chatViewModel.TryAutoConnectAsync(cancellationToken).ConfigureAwait(true);

        connectionState = await ReadConnectionStateAsync().ConfigureAwait(true);
        if (connectionState.Phase == ConnectionPhase.Connected)
        {
            return ChatLaunchConnectionOutcome.Connected;
        }

        return (connectionState.Phase == ConnectionPhase.Connecting || connectionState.Phase == ConnectionPhase.Initializing)
            ? ChatLaunchConnectionOutcome.InProgress
            : ChatLaunchConnectionOutcome.RequiresConfiguration;
    }

    public Task PromoteNewSessionDraftForLaunchAsync(CancellationToken cancellationToken = default)
        => _chatViewModel.PromoteNewSessionDraftForLaunchAsync(cancellationToken);

    public bool TrySendPromptForLaunch()
    {
        if (_chatViewModel.SendPromptCommand?.CanExecute(null) != true)
        {
            return false;
        }

        _chatViewModel.SendPromptCommand.Execute(null);
        return true;
    }

    private async Task<ChatConnectionState> ReadConnectionStateAsync()
    {
        if (_connectionStore is null)
        {
            return ChatConnectionState.Empty;
        }

        return await _connectionStore.GetCurrentStateAsync().ConfigureAwait(false);
    }
}
