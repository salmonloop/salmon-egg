using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Wraps IChatService so ACP session updates are serialized through AcpEventAdapter
/// before being published to UI subscribers.
/// </summary>
public sealed class AcpChatServiceAdapter : IChatService, IAcpSessionUpdateBufferController, IDisposable
{
    private readonly IChatService _inner;
    private readonly AcpEventAdapter _eventAdapter;
    private bool _disposed;

    public AcpChatServiceAdapter(IChatService inner, AcpEventAdapter eventAdapter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _eventAdapter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));

        _inner.SessionUpdateReceived += OnInnerSessionUpdateReceived;
    }

    public string? CurrentSessionId => _inner.CurrentSessionId;

    public bool IsInitialized => _inner.IsInitialized;

    public bool IsConnected => _inner.IsConnected;

    public AgentInfo? AgentInfo => _inner.AgentInfo;

    public AgentCapabilities? AgentCapabilities => _inner.AgentCapabilities;

    public IReadOnlyList<SessionUpdateEntry> SessionHistory => _inner.SessionHistory;

    public Plan? CurrentPlan => _inner.CurrentPlan;

    public SessionModeState? CurrentMode => _inner.CurrentMode;

    public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

    public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived
    {
        add => _inner.PermissionRequestReceived += value;
        remove => _inner.PermissionRequestReceived -= value;
    }

    public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived
    {
        add => _inner.FileSystemRequestReceived += value;
        remove => _inner.FileSystemRequestReceived -= value;
    }

    public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived
    {
        add => _inner.TerminalRequestReceived += value;
        remove => _inner.TerminalRequestReceived -= value;
    }

    public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived
    {
        add => _inner.TerminalStateChangedReceived += value;
        remove => _inner.TerminalStateChangedReceived -= value;
    }

    public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived
    {
        add => _inner.AskUserRequestReceived += value;
        remove => _inner.AskUserRequestReceived -= value;
    }

    public event EventHandler<string>? ErrorOccurred
    {
        add => _inner.ErrorOccurred += value;
        remove => _inner.ErrorOccurred -= value;
    }

    public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
        => _inner.InitializeAsync(@params);

    public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
        => _inner.CreateSessionAsync(@params);

    public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
        => _inner.LoadSessionAsync(@params);

    public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
        => _inner.LoadSessionAsync(@params, cancellationToken);

    public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params)
        => _inner.ResumeSessionAsync(@params);

    public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken)
        => _inner.ResumeSessionAsync(@params, cancellationToken);

    public Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
        => _inner.CloseSessionAsync(@params, cancellationToken);

    public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
        => _inner.ListSessionsAsync(@params, cancellationToken);

    public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        => _inner.SendPromptAsync(@params, cancellationToken);

    public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
        => _inner.SetSessionModeAsync(@params);

    public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
        => _inner.SetSessionConfigOptionAsync(@params);

    public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
        => _inner.CancelSessionAsync(@params);

    public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
        => _inner.AuthenticateAsync(@params, cancellationToken);

    public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
        => _inner.RespondToPermissionRequestAsync(messageId, outcome, optionId);

    public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        => _inner.RespondToFileSystemRequestAsync(messageId, success, content, message);

    public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        => _inner.RespondToAskUserRequestAsync(messageId, answers);

    public Task<bool> DisconnectAsync()
        => _inner.DisconnectAsync();

    public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
        => _inner.GetAvailableModesAsync();

    public void ClearHistory()
        => _inner.ClearHistory();

    public void PublishBufferedUpdate(SessionUpdateEventArgs update)
    {
        ArgumentNullException.ThrowIfNull(update);
        SessionUpdateReceived?.Invoke(this, update);
    }

    public long BeginHydrationBufferingScope(string? sessionId)
        => _eventAdapter.BeginHydrationBuffering(sessionId);

    public void SuppressBufferedUpdates(string? reason = null)
        => _eventAdapter.SuppressBufferedUpdates(reason);

    public void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null)
        => _eventAdapter.SuppressBufferedUpdates(hydrationAttemptId, reason);

    public bool MarkHydrated(bool lowTrust = false, string? reason = null)
        => _eventAdapter.MarkHydrated(lowTrust, reason);

    public bool MarkHydrated(long hydrationAttemptId, bool lowTrust = false, string? reason = null)
        => _eventAdapter.MarkHydrated(hydrationAttemptId, lowTrust, reason);

    public bool TryMarkHydrated(long hydrationAttemptId, bool lowTrust = false, string? reason = null)
        => _eventAdapter.MarkHydrated(hydrationAttemptId, lowTrust, reason);

    public Task WaitForBufferedUpdatesDrainedAsync(long hydrationAttemptId, CancellationToken cancellationToken = default)
        => _eventAdapter.WaitForDrainIdleAsync(hydrationAttemptId, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.SessionUpdateReceived -= OnInnerSessionUpdateReceived;
    }

    private void OnInnerSessionUpdateReceived(object? sender, SessionUpdateEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _eventAdapter.OnSessionUpdate(args);
    }
}
