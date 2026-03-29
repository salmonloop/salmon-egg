using System;
using System.Reflection;
using Moq;
using Serilog;
using SalmonEgg.Domain.Models;
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
    public void CreateTransport_Stdio_Should_Preserve_Quoted_Path_Arguments()
    {
        var factory = new TransportFactory(_logger);
        const string scriptPath = @"C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.GuiTests.Windows\Fixtures\Slow Replay Agent.ps1";

        var transport = factory.CreateTransport(
            TransportType.Stdio,
            command: "powershell.exe",
            args: $"-NoLogo -NoProfile -File \"{scriptPath}\"");

        var stdioTransport = Assert.IsType<StdioTransport>(transport);
        var argsField = typeof(StdioTransport).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(argsField);

        var parsedArgs = Assert.IsType<string[]>(argsField!.GetValue(stdioTransport));
        Assert.Equal(
            new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                scriptPath
            },
            parsedArgs);
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
}
