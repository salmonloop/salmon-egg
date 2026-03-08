using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// Defines the abstraction for network transport layers (WebSocket, HTTP SSE).
    /// </summary>
    public interface ITransport
    {
        /// <summary>
        /// Establishes a connection to the specified URL.
        /// </summary>
        /// <param name="url">The server URL to connect to.</param>
        /// <param name="ct">Cancellation token to cancel the connection attempt.</param>
        /// <returns>A task representing the asynchronous connection operation.</returns>
        Task ConnectAsync(string url, CancellationToken ct);

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        /// <returns>A task representing the asynchronous disconnection operation.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="ct">Cancellation token to cancel the send operation.</param>
        /// <returns>A task representing the asynchronous send operation.</returns>
        Task SendAsync(string message, CancellationToken ct);

        /// <summary>
        /// Gets an observable stream of incoming messages from the server.
        /// </summary>
        IObservable<string> Messages { get; }

        /// <summary>
        /// Gets an observable stream of transport state changes.
        /// </summary>
        IObservable<TransportState> StateChanges { get; }
    }
}
