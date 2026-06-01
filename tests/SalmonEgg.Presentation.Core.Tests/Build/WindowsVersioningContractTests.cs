using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class WindowsVersioningContractTests
{
    [Fact]
    public void WindowsVersioning_UsesSingleDisplayVersionSourceAndGeneratedManifestTemplates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFile = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "SalmonEgg.csproj"));
        var packageManifestTemplate = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "Package.appxmanifest"));
        var applicationManifestTemplate = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "app.manifest"));

        Assert.Contains("<SalmonEggDisplayVersion>", projectFile, StringComparison.Ordinal);
        Assert.Contains(
            "<SalmonEggPackageVersion>$(SalmonEggDisplayVersion).0</SalmonEggPackageVersion>",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains("<Version>$(SalmonEggPackageVersion)</Version>", projectFile, StringComparison.Ordinal);
        Assert.Contains(
            "<ApplicationDisplayVersion>$(SalmonEggDisplayVersion)</ApplicationDisplayVersion>",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains("GenerateVersionedWindowsManifests", projectFile, StringComparison.Ordinal);
        Assert.Contains(
            "<WindowsAppxManifestPath>$(SalmonEggGeneratedPackageManifest)</WindowsAppxManifestPath>",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ApplicationManifest>$(SalmonEggGeneratedApplicationManifest)</ApplicationManifest>",
            projectFile,
            StringComparison.Ordinal);

        Assert.Contains("__SALMONEGG_PACKAGE_VERSION__", packageManifestTemplate, StringComparison.Ordinal);
        Assert.Contains("__SALMONEGG_PACKAGE_VERSION__", applicationManifestTemplate, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SalmonEgg.sln"))
                || File.Exists(Path.Combine(current.FullName, "SalmonEgg.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
