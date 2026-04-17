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
        Assert.Equal("Stdio 传输必须指定命令或启动器", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldAcceptSshBridgeCommand()
    {
        var viewModel = new TransportConfigViewModel
        {
            SelectedTransportType = TransportType.Stdio,
            StdioCommand = "ssh",
            StdioArgs = "-T -o BatchMode=yes user@host /opt/acp/bin/agent stdio"
        };

        var result = viewModel.Validate();

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }
}
