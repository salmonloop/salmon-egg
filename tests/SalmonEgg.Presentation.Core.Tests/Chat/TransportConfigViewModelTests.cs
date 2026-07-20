using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class TransportConfigViewModelTests
{
    [Fact]
    public void Validate_WhenStdioCommandMissing_ShouldMentionLauncherSupport()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.Stdio
        };

        var result = viewModel.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("Stdio transport requires a command or launcher.", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldAcceptSshBridgeCommand()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.Stdio,
            StdioCommand = "ssh",
            StdioArgumentsText = "-T -o BatchMode=yes user@host /opt/acp/bin/agent stdio"
        };

        var result = viewModel.Validate();

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Validate_WhenWebSocketUrlMissingScheme_ShouldReject()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "http://example.com/message"
        };

        var result = viewModel.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("WebSocket URL must start with ws:// or wss://.", result.ErrorMessage);
    }

    [Fact]
    public void Validate_WhenHttpSseUrlUsesWsScheme_ShouldReject()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.HttpSse,
            RemoteUrl = "ws://example.com/sse"
        };

        var result = viewModel.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("HTTP SSE URL must start with http:// or https://.", result.ErrorMessage);
    }

    [Fact]
    public void Validate_WhenRemoteUrlMissing_ShouldReject()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = " "
        };

        var result = viewModel.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("Remote transport requires a URL.", result.ErrorMessage);
    }
}
