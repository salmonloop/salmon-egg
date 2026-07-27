using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Acp.Tests.Architecture;

public sealed class AcpSdkBoundaryTests
{
    // 本文件只保留 AGENTS.md §5.6 允许的架构回归护栏(依赖方向、开放枚举形态、Domain 纯度)。
    // 「使用未引用工程的类型」由编译器保证,不用源码扫描重复;
    // 「已移除文件不得复活」的摆放断言属 §5.5 禁止的实现摆放清单,已删除——
    // 其意图由依赖方向锁 + DomainAssemblyBoundaryTests 反射边界共同承担。

    [Fact]
    public void AcpSdkProject_RemainsIndependentFromSalmonEggBusinessProjects()
    {
        var project = XDocument.Parse(LoadFile(@"src\SalmonEgg.Acp\SalmonEgg.Acp.csproj"));

        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    public void DomainDoesNotReferenceAcpSdkAndKeepsStopReasonAsExtensibleValueType()
    {
        var domainProject = XDocument.Parse(LoadFile(@"src\SalmonEgg.Domain\SalmonEgg.Domain.csproj"));

        // Domain owns app-local models only; ACP wire types are projected at Application/host boundaries.
        Assert.DoesNotContain(
            @"..\SalmonEgg.Acp\SalmonEgg.Acp.csproj",
            domainProject.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")));
        Assert.Empty(domainProject.Descendants("ProjectReference"));

        // StopReason lives in the SDK as an extensible value type so unknown wire values can
        // round-trip losslessly (ACP #[non_exhaustive] + Other(String) contract). 禁止回退为
        // Domain 私有 closed enum——那会破坏协议宽松度契约。
        Assert.DoesNotContain("enum StopReason", LoadFile(@"src\SalmonEgg.Domain\Models\Session\SessionTypes.cs"));
        Assert.DoesNotContain("readonly struct StopReason", LoadFile(@"src\SalmonEgg.Domain\Models\Session\SessionTypes.cs"));
        Assert.Contains("readonly struct StopReason", LoadFile(@"src\SalmonEgg.Acp\Protocol\StopReasonTypes.cs"));
    }

    [Fact]
    public void Domain_DoesNotOwnPlatformOrFileSystemProbing()
    {
        var domainSources = Directory
            .EnumerateFiles(RepoPath(@"src\SalmonEgg.Domain"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(domainSources, source => source.Contains("RuntimeInformation", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("OSPlatform.", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("Environment.CurrentDirectory", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("File.Exists(", StringComparison.Ordinal));
        Assert.DoesNotContain(domainSources, source => source.Contains("Directory.Exists(", StringComparison.Ordinal));
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

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
