using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Logging;

namespace SalmonEgg.Infrastructure.Client;

internal sealed class DomainAcpTransportAdapter : IAcpTransport
{
    private readonly ITransport _inner;
    private bool _disposed;

    public DomainAcpTransportAdapter(ITransport inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.MessageReceived += OnMessageReceived;
        _inner.ErrorOccurred += OnErrorOccurred;
    }

    public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred;

    public bool IsConnected => _inner.IsConnected;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        => _inner.ConnectAsync(cancellationToken);

    public Task<bool> DisconnectAsync()
        => _inner.DisconnectAsync();

    public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        => _inner.SendMessageAsync(message, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.MessageReceived -= OnMessageReceived;
        _inner.ErrorOccurred -= OnErrorOccurred;

        // 适配器 1:1 包裹底层 Domain 传输，其生命周期随适配器归还。
        _inner.Dispose();
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(
            this,
            new AcpTransportMessageReceivedEventArgs(e.Message, e.ReceivedAt));
    }

    private void OnErrorOccurred(object? sender, TransportErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(
            this,
            new AcpTransportErrorEventArgs(
                e.ErrorMessage,
                e.Exception,
                MapErrorKind(e.Kind)));
    }

    private static AcpTransportErrorKind MapErrorKind(TransportErrorKind kind)
        => kind switch
        {
            TransportErrorKind.AgentStderr => AcpTransportErrorKind.AgentStderr,
            TransportErrorKind.ProcessStartFailed => AcpTransportErrorKind.ProcessStartFailed,
            TransportErrorKind.ProcessExited => AcpTransportErrorKind.ProcessExited,
            TransportErrorKind.SendFailed => AcpTransportErrorKind.SendFailed,
            TransportErrorKind.StdoutReadFailed => AcpTransportErrorKind.StdoutReadFailed,
            TransportErrorKind.StderrReadFailed => AcpTransportErrorKind.StderrReadFailed,
            TransportErrorKind.DisconnectFailed => AcpTransportErrorKind.DisconnectFailed,
            TransportErrorKind.NotConnected => AcpTransportErrorKind.NotConnected,
            TransportErrorKind.StdoutProtocolViolation => AcpTransportErrorKind.StdoutProtocolViolation,
            _ => AcpTransportErrorKind.General
        };
}

internal sealed class DomainAcpClientSessionStore : IAcpClientSessionStore
{
    private readonly ISessionManager _inner;

    public DomainAcpClientSessionStore(ISessionManager inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool ContainsSession(string sessionId)
        => _inner.GetSession(sessionId) is not null;

    public async Task CreateSessionAsync(string sessionId, string cwd)
    {
        await _inner.CreateSessionAsync(sessionId, cwd).ConfigureAwait(false);
    }

    public bool RemoveSession(string sessionId)
        => _inner.RemoveSession(sessionId);

    public bool UpdateCurrentMode(string sessionId, string modeId)
    {
        if (_inner.GetSession(sessionId) is not { } session)
        {
            return false;
        }

        // 模式切换的写入与「当前模式对象」的重新解析必须一起发生，这个不可分性属于会话自身，
        // 因此交给 Session 在其内部临界区完成，而不是在这里拿到引用后逐字段改。
        session.SetCurrentModeId(modeId);
        return true;
    }

    public Task<bool> CancelSessionAsync(string sessionId)
        => _inner.CancelSessionAsync(sessionId);
}

internal sealed class DomainAcpClientLogger : IAcpClientLogger
{
    private readonly IErrorLogger _inner;

    public DomainAcpClientLogger(IErrorLogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Log(
        AcpClientLogLevel level,
        string code,
        string message,
        string? source = null,
        Exception? exception = null)
    {
        _inner.LogError(new ErrorLogEntry(
            code,
            message,
            MapSeverity(level),
            source,
            exception: exception));
    }

    private static ErrorSeverity MapSeverity(AcpClientLogLevel level)
        => level switch
        {
            AcpClientLogLevel.Trace => ErrorSeverity.Info,
            AcpClientLogLevel.Information => ErrorSeverity.Info,
            AcpClientLogLevel.Warning => ErrorSeverity.Warning,
            _ => ErrorSeverity.Error
        };
}
