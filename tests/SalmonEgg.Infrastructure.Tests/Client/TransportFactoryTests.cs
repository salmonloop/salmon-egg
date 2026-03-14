using System;
using System.Reflection;
using Moq;
using Serilog;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Client;

public sealed class TransportFactoryTests
{
    private readonly ILogger _logger = Mock.Of<ILogger>();

    [Fact]
    public void CreateTransport_WebSocket_Should_Return_NetworkTransportAdapter()
    {
        var factory = new TransportFactory(_logger);

        var transport = factory.CreateTransport(TransportType.WebSocket, url: "wss://example.com/socket");

        Assert.IsType<NetworkTransportAdapter>(transport);
    }

    [Fact]
    public void CreateTransport_HttpSse_Should_Return_NetworkTransportAdapter()
    {
        var factory = new TransportFactory(_logger);

        var transport = factory.CreateTransport(TransportType.HttpSse, url: "https://example.com/events");

        Assert.IsType<NetworkTransportAdapter>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_Should_Return_StdioTransport()
    {
        var factory = new TransportFactory(_logger);

        var transport = factory.CreateTransport(TransportType.Stdio, command: "agent", args: "--mode test");

        Assert.IsType<StdioTransport>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_Should_Throw_When_Command_Missing()
    {
        var factory = new TransportFactory(_logger);

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.Stdio, command: null, args: null));
    }

    [Fact]
    public void CreateTransport_WebSocket_Should_Throw_When_Url_Invalid()
    {
        var factory = new TransportFactory(_logger);

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.WebSocket, url: "not-a-url"));
    }

    [Fact]
    public void CreateTransport_HttpSse_Should_Throw_When_Url_Empty()
    {
        var factory = new TransportFactory(_logger);

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.HttpSse, url: " "));
    }

    [Fact]
    public void CreateTransport_Should_Throw_When_Type_Unsupported()
    {
        var factory = new TransportFactory(_logger);

        Assert.Throws<NotSupportedException>(() =>
            factory.CreateTransport((TransportType)999));
    }

    [Fact]
    public void CreateDefaultTransport_Should_Return_StdioTransport()
    {
        var factory = new TransportFactory(_logger);

        var transport = factory.CreateDefaultTransport();

        Assert.IsType<StdioTransport>(transport);
    }

    [Fact]
    public void CreateTransport_WithHttpConfig_Should_ApplyHeadersAndProxy()
    {
        var factory = new TransportFactory(_logger);
        var config = new ServerConfiguration
        {
            Transport = TransportType.HttpSse,
            ServerUrl = "https://example.com/sse",
            Authentication = new AuthenticationConfig
            {
                Token = "token-123",
                ApiKey = "key-456"
            },
            Proxy = new ProxyConfig
            {
                Enabled = true,
                ProxyUrl = "http://proxy:8080"
            }
        };

        var transport = factory.CreateTransport(config);
        var adapter = Assert.IsType<NetworkTransportAdapter>(transport);
        var inner = GetPrivateField<object>(adapter, "_inner");
        Assert.IsType<HttpSseTransport>(inner);
        var options = GetPrivateField<HttpTransportOptions>(inner, "_options");

        Assert.NotNull(options);
        Assert.Equal("http://proxy:8080", options.ProxyUrl);
        Assert.Equal("Bearer token-123", options.Headers["Authorization"]);
        Assert.Equal("key-456", options.Headers["X-API-Key"]);
    }

    [Fact]
    public void CreateTransport_WithWebSocketConfig_Should_ApplyHeadersAndProxy()
    {
        var factory = new TransportFactory(_logger);
        var config = new ServerConfiguration
        {
            Transport = TransportType.WebSocket,
            ServerUrl = "wss://example.com/socket",
            Authentication = new AuthenticationConfig
            {
                Token = "token-abc",
                ApiKey = "key-def"
            },
            Proxy = new ProxyConfig
            {
                Enabled = true,
                ProxyUrl = "http://proxy:8080"
            }
        };

        var transport = factory.CreateTransport(config);
        var adapter = Assert.IsType<NetworkTransportAdapter>(transport);
        var inner = GetPrivateField<object>(adapter, "_inner");
        Assert.IsType<WebSocketTransport>(inner);
        var options = GetPrivateField<HttpTransportOptions>(inner, "_options");

        Assert.NotNull(options);
        Assert.Equal("http://proxy:8080", options.ProxyUrl);
        Assert.Equal("Bearer token-abc", options.Headers["Authorization"]);
        Assert.Equal("key-def", options.Headers["X-API-Key"]);
    }

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }
}
