using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DomainTransport = SalmonEgg.Domain.Interfaces.Transport.ITransport;
using SalmonEgg.Domain.Interfaces.Transport;
using NetworkTransport = SalmonEgg.Infrastructure.Network.ITransport;
using SalmonEgg.Infrastructure.Network;

namespace SalmonEgg.Infrastructure.Client;

public sealed class NetworkTransportAdapter : DomainTransport, IDisposable
{
    private readonly NetworkTransport _inner;
    private readonly string _url;
    private readonly List<IDisposable> _subscriptions = new();
    // Written from the transport's own notification thread and from connect/disconnect continuations,
    // read by callers deciding whether to fault in-flight work. Volatile so the latest value is the
    // one they see; there is no invariant spanning it and another field, so no lock is needed.
    private volatile bool _isConnected;
    private bool _disposed;
    // Identity-only event deduplication, not connection state. Weak keys cannot retain faults from
    // abandoned sends. The fatal transition must be reported immediately, before a quick reconnect
    // can hide the disconnect from the SDK's pending-request cleanup.
    private readonly ConditionalWeakTable<Exception, object> _reportedSendFailures = new();

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    public NetworkTransportAdapter(NetworkTransport inner, string url)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _url = string.IsNullOrWhiteSpace(url) ? throw new ArgumentException("URL cannot be empty", nameof(url)) : url.Trim();

        _subscriptions.Add(_inner.Messages.Subscribe(
            message =>
            {
                if (!string.IsNullOrEmpty(message))
                {
                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
                }
            },
            ex => RaiseError("Transport message stream error", ex, TransportErrorKind.General)));

        var stateChanges = _inner is ITransportStateSource stateSource
            ? stateSource.StateTransitions
            : _inner.StateChanges.Select(static state => new TransportStateChange(state));
        _subscriptions.Add(stateChanges.Subscribe(
            change =>
            {
                var state = change.State;
                _isConnected = state == TransportState.Connected;
                if (state == TransportState.Error)
                {
                    // ExecutionContext is not an ownership boundary:
                    // a reader Task.Run started during initialize inherits the send's AsyncLocals.
                    if (change.Origin == TransportStateChangeOrigin.SendFailure)
                    {
                        if (change.Exception is not null)
                        {
                            if (!_reportedSendFailures.TryAdd(change.Exception, new object()))
                            {
                                return;
                            }
                        }
                        RaiseError("Failed to send message", change.Exception, TransportErrorKind.SendFailed);
                        return;
                    }
                    RaiseError("Transport entered error state", null, TransportErrorKind.General);
                }
            },
            ex => RaiseError("Transport state stream error", ex, TransportErrorKind.General)));
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.ConnectAsync(_url, cancellationToken).ConfigureAwait(false);
            _isConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            RaiseError("Failed to connect transport", ex, TransportErrorKind.General);
            _isConnected = false;
            return false;
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            await _inner.DisconnectAsync().ConfigureAwait(false);
            _isConnected = false;
            return true;
        }
        catch (Exception ex)
        {
            RaiseError("Failed to disconnect transport", ex, TransportErrorKind.DisconnectFailed);
            return false;
        }
    }

    public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        => SendMessageAsync(message, TransportSendOptions.Default, cancellationToken);

    public async Task<bool> SendMessageAsync(
        string message,
        TransportSendOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var wasConnected = _isConnected;
        try
        {
            await _inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Preserve the caller's cancellation identity so the SDK can notify the peer. An actual
            // break is still a connection failure even when cancellation races that failed write.
            if (!_reportedSendFailures.Remove(ex) && wasConnected && !_isConnected)
            {
                RaiseError("Transport disconnected during send", null, TransportErrorKind.General);
            }
            throw;
        }
        catch (Exception ex)
        {
            if (_reportedSendFailures.Remove(ex))
            {
                return false;
            }
            // Distinguish "there was no connection to send on" from "the send itself failed", the
            // same split the stdio transport reports. A fatal send fault also arrives as
            // TransportState.Error, which flips IsConnected to false so the ACP client faults its
            // in-flight requests instead of leaving them to hang; a transient fault leaves the
            // connection intact and only reports SendFailed.
            var kind = wasConnected ? TransportErrorKind.SendFailed : TransportErrorKind.NotConnected;
            if (options != TransportSendOptions.DiagnosticOnly || !_isConnected)
            {
                RaiseError("Failed to send message", ex, kind);
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch
            {
            }
        }
        _subscriptions.Clear();

        if (_inner is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
            }
        }
    }

    private void RaiseError(string message, Exception? exception, TransportErrorKind kind)
    {
        ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(message, exception, kind));
    }
}
