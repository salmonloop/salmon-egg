using System;
using System.Collections.Generic;
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
    // True for the duration of an in-flight send on this execution context. A fatal send fault makes
    // the inner transport report the break as TransportState.Error from inside the send call, which
    // would otherwise be reported twice: once as General by the state subscription and once with a
    // precise kind by the send catch. The reentrant push is a causal child of the send, so the scope
    // that identifies it is the execution context, not the thread — an async transport resumes the
    // send on a different thread, and concurrent sends each need their own answer. An Error raised on
    // the transport's own reader thread carries no send scope and still reports normally.
    private readonly AsyncLocal<bool> _sendInProgress = new();

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

        _subscriptions.Add(_inner.StateChanges.Subscribe(
            state =>
            {
                _isConnected = state == TransportState.Connected;
                if (state == TransportState.Error)
                {
                    // A push raised from inside the current send is that send's own failure; its catch
                    // reports it with a precise kind, so do not also report it as General.
                    if (_sendInProgress.Value)
                    {
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

    public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        // Claim the send so a reentrant Error push from the inner transport is attributed to this
        // send rather than reported separately as General.
        var wasConnected = _isConnected;
        _sendInProgress.Value = true;
        try
        {
            await _inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // Distinguish "there was no connection to send on" from "the send itself failed", the
            // same split the stdio transport reports. A fatal send fault also arrives as
            // TransportState.Error, which flips IsConnected to false so the ACP client faults its
            // in-flight requests instead of leaving them to hang; a transient fault leaves the
            // connection intact and only reports SendFailed.
            var kind = wasConnected ? TransportErrorKind.SendFailed : TransportErrorKind.NotConnected;
            RaiseError("Failed to send message", ex, kind);
            return false;
        }
        finally
        {
            _sendInProgress.Value = false;
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
