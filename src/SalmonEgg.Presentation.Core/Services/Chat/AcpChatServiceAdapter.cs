using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Acp.Client;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Wraps IChatService so ACP session updates are serialized through AcpEventAdapter
/// before being published to UI subscribers.
/// </summary>
public sealed class AcpChatServiceAdapter : IChatService, IStdioInvocationSource, IAcpSessionUpdateBufferController, IDisposable
{
    private readonly IChatService _inner;
    private readonly AcpEventAdapter _eventAdapter;
    private readonly object _resyncSync = new();
    private readonly HashSet<Task> _resyncTasks = new();
    private ResolvedCredentialBinding? _connectionCredentials;
    private bool _disposed;

    public AcpChatServiceAdapter(IChatService inner, AcpEventAdapter eventAdapter)
        : this(inner, eventAdapter, connectionCredentials: null)
    {
    }

    internal AcpChatServiceAdapter(
        IChatService inner,
        AcpEventAdapter eventAdapter,
        ResolvedCredentialBinding? connectionCredentials)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _eventAdapter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));
        _connectionCredentials = connectionCredentials;

        _inner.SessionUpdateReceived += OnInnerSessionUpdateReceived;
        _eventAdapter.UpdateDispatched += OnBufferedUpdateDispatched;
    }

    // Secret imports can change credentials without changing the profile revision. Compare the
    // actual connection snapshots in memory; never put secrets or their hashes in cache keys/logs.
    internal bool UsesCredentialSnapshot(ResolvedCredentialBinding? snapshot)
    {
        var current = _connectionCredentials;
        if (current is null || snapshot is null)
        {
            return current is null && snapshot is null;
        }

        if (!Equals(current.Endpoint, snapshot.Endpoint)
            || !string.Equals(current.HeaderName, snapshot.HeaderName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.HeaderValue, snapshot.HeaderValue, StringComparison.Ordinal)
            || current.Environment.Count != snapshot.Environment.Count)
        {
            return false;
        }

        foreach (var pair in current.Environment)
        {
            if (!snapshot.Environment.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public string? CurrentSessionId => _inner.CurrentSessionId;

    public bool IsInitialized => _inner.IsInitialized;

    public bool IsConnected => _inner.IsConnected;

    public bool PublishesConfigurationResponses => _inner.PublishesConfigurationResponses;

    public int NegotiatedProtocolVersion => _inner.NegotiatedProtocolVersion;

    public SalmonEgg.Domain.Models.StdioInvocationSnapshot? StdioInvocation
        => (_inner as IStdioInvocationSource)?.StdioInvocation;

    public AgentInfo? AgentInfo => _inner.AgentInfo;

    public AgentCapabilities? AgentCapabilities => _inner.AgentCapabilities;

    public IReadOnlyList<SessionUpdateEntry> SessionHistory => _inner.SessionHistory;

    public Plan? CurrentPlan => _inner.CurrentPlan;

    public SessionModeState? CurrentMode => _inner.CurrentMode;

    public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

    internal event Action<BufferedSessionUpdate>? BufferedSessionUpdateReceived;

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

    public event EventHandler<ElicitationRequestEventArgs>? ElicitationRequestReceived
    {
        add => _inner.ElicitationRequestReceived += value;
        remove => _inner.ElicitationRequestReceived -= value;
    }

    public event EventHandler<ElicitationCompletedEventArgs>? ElicitationCompleted
    {
        add => _inner.ElicitationCompleted += value;
        remove => _inner.ElicitationCompleted -= value;
    }

    public event EventHandler<string>? ErrorOccurred
    {
        add => _inner.ErrorOccurred += value;
        remove => _inner.ErrorOccurred -= value;
    }

    internal event Func<string?, Task>? ResyncRequired;

    internal Task<bool> RequestResyncAsync(string? remoteSessionId)
    {
        Func<string?, Task>? handlers;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_resyncSync)
        {
            handlers = ResyncRequired;
            if (_disposed || handlers is null)
            {
                return Task.FromResult(false);
            }

            _resyncTasks.Add(completion.Task);
        }

        // Handlers can enter the ViewModel subscription owner. Invoke them outside the task-table
        // lock so a concurrent retirement can cancel its source and observe this completion.
        _ = CompleteResyncAsync(handlers, remoteSessionId, completion);
        return completion.Task;
    }

    internal Task DrainResyncAsync()
    {
        lock (_resyncSync)
        {
            return Task.WhenAll(_resyncTasks);
        }
    }

    private static async Task<bool> InvokeResyncHandlersAsync(Func<string?, Task> handlers, string? remoteSessionId)
    {
        foreach (Func<string?, Task> handler in handlers.GetInvocationList())
        {
            await handler(remoteSessionId).ConfigureAwait(false);
        }

        return true;
    }

    private async Task CompleteResyncAsync(
        Func<string?, Task> handlers, string? remoteSessionId, TaskCompletionSource<bool> completion)
    {
        try
        {
            completion.TrySetResult(await InvokeResyncHandlersAsync(handlers, remoteSessionId).ConfigureAwait(false));
        }
        catch (OperationCanceledException error)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally
        {
            _ = completion.Task.Exception;
            lock (_resyncSync) _resyncTasks.Remove(completion.Task);
        }
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

    public async Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
    {
        var response = await _inner.SetSessionConfigOptionAsync(@params).ConfigureAwait(false);
        if (PublishesConfigurationResponses)
        {
            await _eventAdapter.WaitForAvailableUpdatesAsync().ConfigureAwait(false);
        }
        return response;
    }

    public Task CancelSessionAsync(SessionCancelParams @params)
        => _inner.CancelSessionAsync(@params);

    public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
        => _inner.AuthenticateAsync(@params, cancellationToken);

    public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
        => _inner.RespondToPermissionRequestAsync(messageId, outcome, optionId);

    public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        => _inner.RespondToFileSystemRequestAsync(messageId, success, content, message);

    public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        => _inner.RespondToAskUserRequestAsync(messageId, answers);

    public Task<bool> RespondToElicitationRequestAsync(object messageId, ElicitationAcceptContent? content)
        => _inner.RespondToElicitationRequestAsync(messageId, content);

    public Task<bool> DeclineElicitationRequestAsync(object messageId)
        => _inner.DeclineElicitationRequestAsync(messageId);

    public Task<bool> CancelElicitationRequestAsync(object messageId)
        => _inner.CancelElicitationRequestAsync(messageId);

    public Task<bool> DisconnectAsync()
        => _inner.DisconnectAsync();

    public Task<List<SalmonEgg.Acp.Protocol.SessionMode>?> GetAvailableModesAsync()
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

    public void ReleaseUnscopedBufferedUpdates(bool lowTrust = false, string? reason = null)
        => _eventAdapter.ReleaseUnscopedBufferedUpdates(lowTrust, reason);

    public void SuppressAllBufferedUpdates(string? reason = null)
        => _eventAdapter.SuppressAllBufferedUpdates(reason);

    public void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null)
        => _eventAdapter.SuppressBufferedUpdates(hydrationAttemptId, reason);

    public bool TryMarkHydrated(long hydrationAttemptId, bool lowTrust = false, string? reason = null)
        => _eventAdapter.MarkHydrated(hydrationAttemptId, lowTrust, reason);

    public Task WaitForBufferedUpdatesDrainedAsync(long hydrationAttemptId, CancellationToken cancellationToken = default)
        => _eventAdapter.WaitForDrainIdleAsync(hydrationAttemptId, cancellationToken);

    internal Task WaitForSessionUpdatesDrainedAsync(string? sessionId, CancellationToken cancellationToken)
        => _eventAdapter.WaitForSessionUpdatesDrainedAsync(sessionId, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_resyncSync)
        {
            _disposed = true;
            ResyncRequired = null;
        }
        _connectionCredentials = null;
        _inner.SessionUpdateReceived -= OnInnerSessionUpdateReceived;
        _eventAdapter.UpdateDispatched -= OnBufferedUpdateDispatched;
        BufferedSessionUpdateReceived = null;

        // 本适配器是链最外层装饰器，独占其内层 IChatService（进而独占 ACP 客户端/传输）。
        // 释放沿所有权链下传；优雅断连由调用方在 Dispose 前先行 await 的 DisconnectAsync 负责。
        _inner.Dispose();
    }

    private void OnInnerSessionUpdateReceived(object? sender, SessionUpdateEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _eventAdapter.OnSessionUpdate(args);
    }

    private void OnBufferedUpdateDispatched(BufferedSessionUpdate update)
    {
        if (!_disposed) BufferedSessionUpdateReceived?.Invoke(update);
    }
}
