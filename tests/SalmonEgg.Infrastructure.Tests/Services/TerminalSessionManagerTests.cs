using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class TerminalSessionManagerTests
{
    [Fact]
    public void CreateProcessStartInfo_UsesArgumentListWithoutStringQuoting()
    {
        var request = new TerminalCreateRequest
        {
            Command = "agent-command",
            Args =
            [
                "--empty",
                string.Empty,
                "value with spaces",
                "quote\"value",
                @"path\with\slashes"
            ]
        };

        var startInfo = TerminalSessionManager.CreateProcessStartInfo(request);

        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(request.Args, startInfo.ArgumentList);
    }
}
