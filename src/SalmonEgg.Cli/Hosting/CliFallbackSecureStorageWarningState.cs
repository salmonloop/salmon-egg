using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Cli.Output;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Hosting;

internal sealed class CliFallbackSecureStorageWarningState : ILogger<FallbackSecureStorage>
{
    private int _fallbackUsed;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel) && eventId.Id == FallbackSecureStorage.SecretFallbackUsedEventId)
        {
            Interlocked.Exchange(ref _fallbackUsed, 1);
        }
    }

    public Task WriteIfNeededAsync(ICliOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Interlocked.Exchange(ref _fallbackUsed, 0) == 0
            ? Task.CompletedTask
            : output.WriteErrorAsync(
                "Warning: platform secure storage is unavailable; plaintext fallback storage is in use.");
    }
}
