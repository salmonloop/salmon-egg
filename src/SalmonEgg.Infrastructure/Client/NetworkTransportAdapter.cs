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
    private bool _isConnected;
    private bool _disposed;
    // Managed thread id of an in-flight send, or 0 when none. A fatal send fault makes the inner
    // transport push TransportState.Error from inside the same synchronous send call, which would
    // otherwise report the break twice: once as General from the state subscription and once with a
    // precise kind from the send catch. Matching on the thread id suppresses only that reentrant
    // push; an Error arriving on the transport's own reader thread still reports normally.
    private volatile int _sendingThreadId;

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
                    // A reentrant push from inside the current send call is that send's own failure;
                    // its catch reports it with a precise kind, so do not also report it as General.
                    if (_sendingThreadId == Environment.CurrentManagedThreadId)
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
        _sendingThreadId = Environment.CurrentManagedThreadId;
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
            _sendingThreadId = 0;
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
