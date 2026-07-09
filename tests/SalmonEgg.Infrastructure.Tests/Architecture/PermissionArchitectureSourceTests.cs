using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class PermissionArchitectureSourceTests
{
    [Fact]
    public void LegacyLocalPermissionAbstractions_AreRemoved()
    {
        var repoRoot = FindRepoRoot();

        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SalmonEgg.Domain", "Services", "Security", "IPermissionManager.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SalmonEgg.Infrastructure", "Services", "Security", "PermissionManager.cs")));
    }

    [Fact]
    public void AcpPermissionFlow_DoesNotDependOnLegacyLocalPermissionManager()
    {
        var acpClientSource = LoadRepoFile("src", "SalmonEgg.Acp", "Client", "AcpClient.cs");
        var dependencyInjectionSource = LoadRepoFile("SalmonEgg", "SalmonEgg", "DependencyInjection.cs");

        Assert.DoesNotContain("IPermissionManager", acpClientSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionManager()", acpClientSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IPermissionManager", dependencyInjectionSource, StringComparison.Ordinal);
    }

    private static string LoadRepoFile(params string[] segments)
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine([repoRoot, .. segments]));
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
