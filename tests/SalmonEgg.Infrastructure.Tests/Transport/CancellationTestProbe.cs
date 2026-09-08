using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

internal sealed class CancellationTestProbe : IAcpClientLogger
{
    public ConcurrentQueue<string> Errors { get; } = new();
    public TaskCompletionSource Settlement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void RecordError(object? sender, string error) => Errors.Enqueue(error);

    public void Log(AcpClientLogLevel level, string code, string message, string? source = null, Exception? exception = null)
    {
        if (code == "CANCELLED_REQUEST_SETTLED")
        {
            Settlement.TrySetResult();
        }
    }

    public static void AssertMatchingNotification(JsonElement request, JsonElement notification)
    {
        Assert.Equal("2.0", notification.GetProperty("jsonrpc").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
        Assert.Equal("$/cancel_request", notification.GetProperty("method").GetString());
        var originalId = request.GetProperty("id");
        var cancelledId = notification.GetProperty("params").GetProperty("requestId");
        Assert.Equal(originalId.ValueKind, cancelledId.ValueKind);
        Assert.Equal(originalId.GetRawText(), cancelledId.GetRawText());
    }
}
