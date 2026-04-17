using NUnit.Framework;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

[TestFixture]
public sealed class ServerConfigurationDisplayTests
{
    [Test]
    public void TransportDisplayName_ForStdio_Should_DescribeSubprocessTransport()
    {
        var configuration = new ServerConfiguration
        {
            Transport = TransportType.Stdio
        };

        Assert.That(configuration.TransportDisplayName, Is.EqualTo("Stdio（子进程）"));
    }
}
