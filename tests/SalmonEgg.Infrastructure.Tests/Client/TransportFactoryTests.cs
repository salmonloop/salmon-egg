using System;
using System.IO;
using System.Runtime.InteropServices;
using Moq;
using Serilog;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Client;

public sealed class TransportFactoryTests
{
    private readonly ILogger _logger = Mock.Of<ILogger>();

    [Fact]
    public void CreateTransport_WebSocket_Should_Return_NetworkTransportAdapter()
    {
        var factory = CreateFactory();

        var transport = factory.CreateTransport(TransportType.WebSocket, url: "wss://example.com/socket");

        Assert.IsType<NetworkTransportAdapter>(transport);
    }

    [Fact]
    public void CreateTransport_HttpSse_Should_Return_NetworkTransportAdapter()
    {
        var factory = CreateFactory();

        var transport = factory.CreateTransport(TransportType.HttpSse, url: "https://example.com/events");

        Assert.IsType<NetworkTransportAdapter>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_Should_Return_StdioTransport()
    {
        var factory = CreateFactory(supportsStdioTransport: true);

        var transport = factory.CreateTransport(TransportType.Stdio, command: "agent", args: "--mode test");

        Assert.IsType<StdioTransport>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_WithSshBridgeCommand_Should_Return_StdioTransport()
    {
        var factory = CreateFactory(supportsStdioTransport: true);

        var transport = factory.CreateTransport(
            TransportType.Stdio,
            command: "ssh",
            args: "-T -o BatchMode=yes user@host /opt/acp/bin/agent stdio");

        Assert.IsType<StdioTransport>(transport);
    }

    [Fact]
    public async Task CreateTransport_Stdio_WithQuotedScriptPath_CanConnect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var factory = CreateFactory(supportsStdioTransport: true);
        var tempDir = Path.Combine(Path.GetTempPath(), $"salmonegg-stdio-test-{Guid.NewGuid():N}", "with space");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "slow agent.ps1");
        await File.WriteAllTextAsync(scriptPath, "Start-Sleep -Seconds 2");

        try
        {
            var transport = factory.CreateTransport(
                TransportType.Stdio,
                command: "powershell.exe",
                args: $"-NoLogo -NoProfile -File \"{scriptPath}\"");

            var connected = await transport.ConnectAsync();
            Assert.True(
                connected,
                "Stdio transport should connect when script path contains spaces and is quoted.");
            await transport.DisconnectAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(scriptPath)!, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    [Fact]
    public void CreateTransport_Stdio_Should_Throw_When_Command_Missing()
    {
        var factory = CreateFactory(supportsStdioTransport: true);

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.Stdio, command: null, args: null));
    }

    [Fact]
    public void CreateTransport_Stdio_Should_Throw_When_SubprocessTransportUnsupported()
    {
        var factory = CreateFactory(supportsStdioTransport: false);

        Assert.Throws<NotSupportedException>(() =>
            factory.CreateTransport(TransportType.Stdio, command: "agent", args: "--stdio"));
    }

    [Fact]
    public void CreateTransport_WebSocket_Should_Throw_When_Url_Invalid()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.WebSocket, url: "not-a-url"));
    }

    [Fact]
    public void CreateTransport_WebSocket_Should_Throw_When_EndpointPolicyRejectsUrl()
    {
        var factory = new EndpointValidatingTransportFactory(
            CreateFactory(),
            new RejectingTransportEndpointAccessPolicy(
                "Browser HTTPS pages cannot connect to ws:// WebSocket endpoints. Use wss://."));

        var ex = Assert.Throws<NotSupportedException>(() =>
            factory.CreateTransport(TransportType.WebSocket, url: "ws://129.146.110.11:3011/message"));

        Assert.Contains("Browser HTTPS", ex.Message, StringComparison.Ordinal);
        Assert.Contains("wss://", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTransport_HttpSse_Should_Throw_When_Url_Empty()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreateTransport(TransportType.HttpSse, url: " "));
    }

    [Fact]
    public void CreateTransport_Should_Throw_When_Type_Unsupported()
    {
        var factory = CreateFactory();

        Assert.Throws<NotSupportedException>(() =>
            factory.CreateTransport((TransportType)999));
    }

    [Fact]
    public void Constructor_Should_Require_StdioTransportFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TransportFactory(_logger, CreateTransportSupportPolicy(), null!));
    }

    private TransportFactory CreateFactory(bool supportsStdioTransport = true)
        => new(
            _logger,
            CreateTransportSupportPolicy(supportsStdioTransport),
            new DesktopStdioTransportFactory());

    private static ITransportSupportPolicy CreateTransportSupportPolicy(bool supportsStdioTransport = true)
        => new TransportSupportPolicy(CreateCapabilities(supportsStdioTransport).Object);

    private static Mock<IPlatformCapabilityService> CreateCapabilities(bool supportsStdioTransport)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsStdioTransport).Returns(supportsStdioTransport);
        capabilities.SetupGet(c => c.SupportsLocalTerminal).Returns(true);
        capabilities.SetupGet(c => c.SupportsInteractiveTerminalSurface).Returns(true);
        return capabilities;
    }

    private sealed class RejectingTransportEndpointAccessPolicy : ITransportEndpointAccessPolicy
    {
        private readonly string _message;

        public RejectingTransportEndpointAccessPolicy(string message)
        {
            _message = message;
        }

        public TransportEndpointAccessResult Validate(TransportType transport, string? endpoint)
            => new(false, _message);
    }
}
