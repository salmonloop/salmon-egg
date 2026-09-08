using System;

namespace SalmonEgg.Infrastructure.Network;

/// <summary>
/// A transport can identify the operation that caused a state transition. StateChanges remains a
/// projection of the same stream; consumers subscribe to one shape, never both. The adapter may
/// identify an explicit send failure without hiding reader failures or waiting for send completion.
/// </summary>
internal interface ITransportStateSource
{
    IObservable<TransportStateChange> StateTransitions { get; }
}

internal readonly record struct TransportStateChange(
    TransportState State,
    TransportStateChangeOrigin Origin = TransportStateChangeOrigin.Unknown,
    Exception? Exception = null);

internal enum TransportStateChangeOrigin
{
    Unknown,
    Connection,
    SendFailure,
    ReceiveFailure
}
