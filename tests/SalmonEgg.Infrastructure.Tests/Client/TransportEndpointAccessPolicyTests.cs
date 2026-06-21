using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Client;

namespace SalmonEgg.Infrastructure.Tests.Client;

public sealed class TransportEndpointAccessPolicyTests
{
    [Fact]
    public void Validate_WebSocketWsFromSecureBrowserOrigin_Should_BeRejectedWithActionableMessage()
    {
        var policy = new TransportEndpointAccessPolicy(
            new TestTransportEndpointAccessContext(browserHosted: true, secureOrigin: true));

        var result = policy.Validate(TransportType.WebSocket, "ws://129.146.110.11:3011/message");

        Assert.False(result.IsAllowed);
        Assert.Contains("HTTPS", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("ws://", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("wss://", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("ws://129.146.110.11:3011/message", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WebSocketWsFromHttpBrowserOrigin_Should_BeAllowedForLocalDevelopment()
    {
        var policy = new TransportEndpointAccessPolicy(
            new TestTransportEndpointAccessContext(browserHosted: true, secureOrigin: false));

        var result = policy.Validate(TransportType.WebSocket, "ws://127.0.0.1:3011/message");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Validate_WebSocketWsFromDesktopHost_Should_BeAllowed()
    {
        var policy = new TransportEndpointAccessPolicy(
            new TestTransportEndpointAccessContext(browserHosted: false, secureOrigin: true));

        var result = policy.Validate(TransportType.WebSocket, "ws://129.146.110.11:3011/message");

        Assert.True(result.IsAllowed);
    }

    private sealed class TestTransportEndpointAccessContext : ITransportEndpointAccessContext
    {
        public TestTransportEndpointAccessContext(bool browserHosted, bool secureOrigin)
        {
            IsBrowserHosted = browserHosted;
            IsSecureOrigin = secureOrigin;
        }

        public bool IsBrowserHosted { get; }

        public bool IsSecureOrigin { get; }
    }
}
