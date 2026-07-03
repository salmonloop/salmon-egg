using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class DesktopBuildContractTests
{
    [Fact]
    public void DesktopTarget_DoesNotForceX64PlatformTarget()
    {
        var project = LoadText("SalmonEgg/SalmonEgg/SalmonEgg.csproj");

        Assert.DoesNotContain("<PlatformTarget>x64</PlatformTarget>", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<PlatformTarget Condition=\"'$(PlatformTarget)' == ''\">AnyCPU</PlatformTarget>", project, StringComparison.Ordinal);
    }

    private static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
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
