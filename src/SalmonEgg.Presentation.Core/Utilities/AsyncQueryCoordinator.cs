using System;
using System.Threading;

namespace SalmonEgg.Presentation.Utilities;

public sealed class AsyncQueryCoordinator : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _version;
    private bool _disposed;

    public QueryTicket Begin()
    {
        ThrowIfDisposed();
        CancelInternal();

        var version = Interlocked.Increment(ref _version);
        var cts = new CancellationTokenSource();
        _cts = cts;
        return new QueryTicket(version, cts.Token);
    }

    public bool IsActive(QueryTicket ticket)
    {
        return !_disposed
               && !ticket.Token.IsCancellationRequested
               && ticket.Version == _version;
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        CancelInternal();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelInternal();
    }

    private void CancelInternal()
    {
        if (_cts == null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch
        {
        }
        finally
        {
            _cts = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AsyncQueryCoordinator));
        }
    }

    public readonly struct QueryTicket
    {
        public QueryTicket(int version, CancellationToken token)
        {
            Version = version;
            Token = token;
        }

        public int Version { get; }
        public CancellationToken Token { get; }
    }
}
