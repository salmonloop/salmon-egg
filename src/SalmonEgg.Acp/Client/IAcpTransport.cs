using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Acp.Client
{
    /// <summary>
    /// Transport contract for ACP. Holds the process, socket, or HttpClient resources owned exclusively by the
    /// underlying transport; its lifetime belongs to <see cref="AcpClient"/>, hence it derives from
    /// <see cref="IDisposable"/>.
    /// </summary>
    public interface IAcpTransport : IDisposable
    {
        event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;

        event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred;

        bool IsConnected { get; }

        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        Task<bool> DisconnectAsync();

        Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends one message with explicit failure ownership. Implementations must honor cancellation.
        /// The default preserves existing transports; override it to keep best-effort send failures
        /// out of <see cref="ErrorOccurred"/> while still reporting connection and reader failures.
        /// </summary>
        Task<bool> SendMessageAsync(
            string message,
            AcpTransportSendOptions options,
            CancellationToken cancellationToken)
            => SendMessageAsync(message, cancellationToken);
    }

    /// <summary>Controls only errors caused by this individual send, never unrelated transport errors.</summary>
    public enum AcpTransportSendOptions
    {
        /// <summary>Report send failures through the transport's normal error events.</summary>
        Default,

        /// <summary>
        /// Return false or throw for a transient send failure so the caller can record diagnostics.
        /// Connection loss, process exit, and reader failures must still raise normal error events.
        /// </summary>
        DiagnosticOnly
    }

    public sealed class AcpTransportMessageReceivedEventArgs : EventArgs
    {
        public AcpTransportMessageReceivedEventArgs(string message, DateTime? receivedAt = null)
        {
            Message = message;
            ReceivedAt = receivedAt ?? DateTime.UtcNow;
        }

        public string Message { get; }

        public DateTime ReceivedAt { get; }
    }

    public enum AcpTransportErrorKind
    {
        General,
        AgentStderr,
        ProcessStartFailed,
        ProcessExited,
        SendFailed,
        StdoutReadFailed,
        StderrReadFailed,
        DisconnectFailed,
        NotConnected,

        /// <summary>
        /// The peer wrote something to the protocol stream that was never an ACP frame. ACP
        /// reserves stdout for protocol messages and directs diagnostics to stderr, so this is
        /// misrouted logging rather than a transport failure — treated like <see cref="AgentStderr"/>.
        /// </summary>
        StdoutProtocolViolation
    }

    public sealed class AcpTransportErrorEventArgs : EventArgs
    {
        public AcpTransportErrorEventArgs(
            string errorMessage,
            Exception? exception = null,
            AcpTransportErrorKind kind = AcpTransportErrorKind.General)
        {
            ErrorMessage = CreateErrorMessage(errorMessage, exception);
            Exception = exception;
            Kind = kind;
            ErrorTime = DateTime.UtcNow;
        }

        public Exception? Exception { get; }

        public string ErrorMessage { get; }

        public DateTime ErrorTime { get; }

        public AcpTransportErrorKind Kind { get; }

        private static string CreateErrorMessage(string errorMessage, Exception? exception)
        {
            var trimmedMessage = errorMessage?.Trim() ?? string.Empty;
            var exceptionMessage = exception?.Message?.Trim();

            if (string.IsNullOrWhiteSpace(trimmedMessage))
            {
                return exceptionMessage ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(exceptionMessage)
                || trimmedMessage.Contains(exceptionMessage, StringComparison.Ordinal))
            {
                return trimmedMessage;
            }

            return trimmedMessage + ": " + exceptionMessage;
        }
    }
}
