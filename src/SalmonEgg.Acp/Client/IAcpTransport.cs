using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Acp.Client
{
    /// <summary>
    /// ACP 传输契约。持有底层传输独占的进程/套接字/HttpClient 资源，
    /// 生命周期由 <see cref="AcpClient"/> 拥有，故继承 <see cref="IDisposable"/>。
    /// </summary>
    public interface IAcpTransport : IDisposable
    {
        event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;

        event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred;

        bool IsConnected { get; }

        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        Task<bool> DisconnectAsync();

        Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default);
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
        NotConnected
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
