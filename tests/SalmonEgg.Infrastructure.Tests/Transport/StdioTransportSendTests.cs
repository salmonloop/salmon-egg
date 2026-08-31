using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioTransportSendTests
{
    // Regression: SendMessageAsync used to write the shared stdin StreamWriter with no serialization,
    // so overlapping in-flight ACP requests raced and threw
    // "InvalidOperationException: The stream is currently in use by a previous operation on the stream",
    // which the catch then escalated to a permanent disconnect. The send gate must serialize writes so
    // concurrent callers all succeed and the transport stays connected.
    [Fact]
    public async Task SendMessageAsync_ConcurrentCallers_AllSucceedWithoutStreamInUseFault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // cat is the portable long-lived stdin reader; skip on Windows hosts.
            return;
        }

        using var transport = new StdioTransport("/bin/cat");
        var sendFailures = new ConcurrentBag<TransportErrorEventArgs>();
        transport.ErrorOccurred += (_, error) =>
        {
            if (error.Kind == TransportErrorKind.SendFailed)
            {
                sendFailures.Add(error);
            }
        };

        var connected = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(connected);

        try
        {
            var sends = Enumerable
                .Range(0, 64)
                .Select(i => transport.SendMessageAsync(
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{i},\"method\":\"ping\"}}",
                    TestContext.Current.CancellationToken))
                .ToArray();

            var results = await Task.WhenAll(sends);

            Assert.All(results, sent => Assert.True(sent));
            Assert.Empty(sendFailures);
            // A transient overlap must never have been escalated to a disconnect.
            Assert.True(transport.IsConnected);
        }
        finally
        {
            await transport.DisconnectAsync();
        }
    }

    [Fact]
    public async Task SendMessageAsync_AfterDisconnect_ReturnsFalseWithoutThrowing()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var transport = new StdioTransport("/bin/cat");
        var connected = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(connected);

        await transport.DisconnectAsync();

        var sent = await transport.SendMessageAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}",
            TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.False(transport.IsConnected);
    }
}
