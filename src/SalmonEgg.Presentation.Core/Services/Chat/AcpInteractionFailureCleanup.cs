using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class AcpInteractionFailureCleanup
{
    internal static TimeSpan DisconnectTimeout { get; } = TimeSpan.FromSeconds(5);

    internal static async Task DisconnectAsync(
        IChatService service, IAcpChatCoordinatorSink sink, string errorMessage,
        Action<IChatService>? removeService = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        logger ??= NullLogger.Instance;
        var generation = sink.ConnectionGeneration;
        var instanceId = sink.ConnectionInstanceId;
        Task<bool>? disconnect = null;
        try
        {
            disconnect = service.DisconnectAsync();
            await disconnect.WaitAsync(DisconnectTimeout).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            LogCleanupFailure(logger, "disconnect", error);
            if (disconnect is not null) _ = ObserveDisconnectAsync(disconnect);
        }
        finally
        {
            try { service.Dispose(); }
            catch (Exception error)
            {
                LogCleanupFailure(logger, "dispose", error);
            }
            try { removeService?.Invoke(service); }
            catch (Exception error) { LogCleanupFailure(logger, "remove", error); }
        }

        await sink.Dispatcher.EnqueueAsync(() =>
        {
            // Teardown can finish after a newer connection. Only its original foreground identity
            // may receive the failure; the old service is released even when it is no longer shown.
            if (!ReferenceEquals(sink.CurrentChatService, service) || sink.ConnectionGeneration != generation
                || !string.Equals(sink.ConnectionInstanceId, instanceId, StringComparison.Ordinal)) return;
            sink.ReplaceChatService(null);
            sink.UpdateConnectionState(isConnecting: false, isConnected: false, isInitialized: false, errorMessage);
        }).ConfigureAwait(false);
    }

    private static async Task ObserveDisconnectAsync(Task<bool> disconnect)
    {
        try { await disconnect.ConfigureAwait(false); }
        catch (Exception) { /* The failure and forced disposal were already reported by the owner. */ }
    }

    private static void LogCleanupFailure(ILogger logger, string operation, Exception error)
    {
        try
        {
            logger.LogWarning("Interaction failure cleanup failed. Operation={Operation} ExceptionType={ExceptionType}",
                operation, error.GetType().FullName);
        }
        catch (Exception)
        {
            // A faulty host logger cannot prevent the original interaction failure from reaching
            // its UI state. There is no independent logging sink to report this secondary failure.
        }
    }
}
