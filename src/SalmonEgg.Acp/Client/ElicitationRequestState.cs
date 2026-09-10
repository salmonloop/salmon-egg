using System;
using System.Threading;

namespace SalmonEgg.Acp.Client;

/// <summary>
/// Read-only lifetime of one inbound elicitation, owned by the client that received it.
/// </summary>
/// <remarks>
/// Completion is independent of consent: an early completion never authorizes opening a URL.
/// Consumers must stop interacting when <see cref="ConnectionClosed"/> is cancelled.
/// </remarks>
public sealed class ElicitationRequestState
{
    private string? _responseAction;
    private int _cancellationRequested;
    private int _isCompleted;
    private int _isUnavailable;

    internal ElicitationRequestState(CancellationToken connectionClosed = default)
    {
        ConnectionClosed = connectionClosed;
    }

    /// <summary>Whether this exact request can still receive the user's response.</summary>
    public bool CanRespond => CanCancel && !IsCancellationRequested;

    /// <summary>Whether cancellation may still be sent, including retrying a failed cancel.</summary>
    public bool CanCancel => ResponseAction is null && Volatile.Read(ref _isUnavailable) == 0
        && !ConnectionClosed.IsCancellationRequested;

    /// <summary>The successfully sent response action, or null until a response succeeds.</summary>
    public string? ResponseAction => Volatile.Read(ref _responseAction);

    /// <summary>Whether the Agent reported completion of this URL interaction.</summary>
    public bool IsCompleted => Volatile.Read(ref _isCompleted) != 0;

    /// <summary>Cancelled when the connection that owns this request ends.</summary>
    public CancellationToken ConnectionClosed { get; }

    internal bool IsResponseInFlight { get; set; }

    internal bool IsCancellationRequested => Volatile.Read(ref _cancellationRequested) != 0;

    /// <summary>Raised when response availability or external completion changes.</summary>
    public event EventHandler? Changed;

    internal void MarkResponseSent(string action)
    {
        Interlocked.CompareExchange(ref _responseAction, action, null);
    }

    internal void RequestCancellation()
    {
        Interlocked.Exchange(ref _cancellationRequested, 1);
    }

    internal void Invalidate()
    {
        Interlocked.Exchange(ref _isUnavailable, 1);
    }

    internal void MarkCompleted()
    {
        Interlocked.Exchange(ref _isCompleted, 1);
    }

    internal void NotifyChanged()
    {
        var subscribers = Changed;
        if (subscribers is null)
        {
            return;
        }

        // Observer exceptions cannot undo a committed transition or skip the remaining consumers.
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch
            {
                // This is a best-effort notification; the authoritative snapshot remains readable.
            }
        }
    }
}
