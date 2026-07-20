using Xunit;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class ServerConfigurationDisplayTests
{
    [Fact]
    public void TransportDisplayName_ForStdio_Should_DescribeSubprocessTransport()
    {
        var configuration = new ServerConfiguration
        {
            Transport = TransportType.Stdio
        };

        Assert.Equal("Stdio (subprocess)", configuration.TransportDisplayName);
    }
}
