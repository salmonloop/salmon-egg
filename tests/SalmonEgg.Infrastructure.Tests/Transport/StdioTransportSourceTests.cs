using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioTransportSourceTests
{
    [Fact]
    public void StdioTransport_DoesNotCreateDefaultPayloadTraceFile()
    {
        var source = LoadStdioTransportSource();

        Assert.DoesNotContain("acp_transport_", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_debugFileWriter", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StdioTransport_DoesNotWriteFullProtocolPayloadsToLogs()
    {
        var source = LoadStdioTransportSource();

        Assert.DoesNotContain("TX: {message}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RX: {line}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("STDERR: {line}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("{Message}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("{Line}", source, StringComparison.Ordinal);
    }

    private static string LoadStdioTransportSource()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "SalmonEgg.Infrastructure.Desktop",
            "Transport",
            "StdioTransport.cs"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }
}
