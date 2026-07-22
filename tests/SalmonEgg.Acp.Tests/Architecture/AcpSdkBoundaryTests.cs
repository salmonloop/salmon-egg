using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Acp.Tests.Architecture;

public sealed class AcpSdkBoundaryTests
{
    [Fact]
    public void AcpSdkProject_RemainsIndependentFromSalmonEggBusinessProjects()
    {
        var project = XDocument.Parse(LoadFile(@"src\SalmonEgg.Acp\SalmonEgg.Acp.csproj"));

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.DoesNotContain(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), item => item?.Contains("SalmonEgg.Domain", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), item => item?.Contains("SalmonEgg.Application", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), item => item?.Contains("SalmonEgg.Infrastructure", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), item => item?.Contains("SalmonEgg.Presentation", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void DomainReferencesAcpSdkInsteadOfOwningProtocolPathRules()
    {
        var domainProject = XDocument.Parse(LoadFile(@"src\SalmonEgg.Domain\SalmonEgg.Domain.csproj"));

        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Protocol\ProtocolPathRules.cs")));
        Assert.Empty(EnumerateFilesIfDirectoryExists(@"src\SalmonEgg.Domain\Models\Protocol", "*.cs"));
        Assert.False(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\JsonRpc")));
        Assert.False(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Content")));
        Assert.False(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Plan")));
        Assert.False(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Tool")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Interfaces\IMessageParser.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Interfaces\IMessageValidator.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Serialization\MessageParser.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Serialization\MessageValidator.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Serialization\AcpJsonContext.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Services\IAcpClient.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Services\ICapabilityManager.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Services\Security\PermissionOption.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Mcp\McpServerConfig.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Mcp\McpServerSupportPolicy.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\AcpMessage.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\AcpError.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Services\IAcpProtocolService.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Services\IConnectionManager.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Client\AcpClient.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Serialization\AcpMessageParser.cs")));
        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Network\ConnectionManager.cs")));
        Assert.DoesNotContain("IAcpProtocolService", LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs"));
        Assert.DoesNotContain("IConnectionManager", LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs"));
        Assert.DoesNotContain("AcpMessageParser", LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs"));
        Assert.DoesNotContain("ConnectionManager(", LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs"));
        Assert.DoesNotContain("public bool Enabled", LoadFile(@"src\SalmonEgg.Acp\Mcp\McpServerConfig.cs"));
        Assert.DoesNotContain("enum StopReason", LoadFile(@"src\SalmonEgg.Domain\Models\Session\SessionTypes.cs"));
        Assert.Contains("enum StopReason", LoadFile(@"src\SalmonEgg.Acp\Protocol\StopReasonTypes.cs"));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\JsonRpc\MessageParser.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Serialization\AcpJsonContext.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Client\AcpClient.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Client\IAcpClient.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Client\ICapabilityManager.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Mcp\McpServerSupportPolicy.cs")));
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Acp\Protocol\PermissionOption.cs")));
        Assert.Contains(@"..\SalmonEgg.Acp\SalmonEgg.Acp.csproj", domainProject.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")));
    }

    [Fact]
    public void Domain_DoesNotOwnPlatformOrFileSystemProbing()
    {
        var domainSources = Directory
            .EnumerateFiles(RepoPath(@"src\SalmonEgg.Domain"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.False(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Utilities\PathResolver.cs")));
        Assert.DoesNotContain(domainSources, source => source.Contains("RuntimeInformation", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("OSPlatform.", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("Environment.CurrentDirectory", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("File.Exists(", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("Directory.Exists(", StringComparison.Ordinal));
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

    private static string[] EnumerateFilesIfDirectoryExists(string relativePath, string searchPattern)
    {
        var path = RepoPath(relativePath);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories).ToArray()
            : [];
    }

    private static string RepoPath(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath.Replace('\\', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
