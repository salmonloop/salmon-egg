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
        var rootBuildProperties = File.ReadAllText(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var projectFile = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "SalmonEgg.csproj"));
        var packageManifestTemplate = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "Package.appxmanifest"));
        var applicationManifestTemplate = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "app.manifest"));

        // The display version lives at the repository root so the GUI and the CLI ship the same
        // release identity. The condition keeps release automation able to override it per build.
        Assert.Contains(
            "<SalmonEggDisplayVersion Condition=\"'$(SalmonEggDisplayVersion)' == ''\">",
            rootBuildProperties,
            StringComparison.Ordinal);
        Assert.Contains(
            "<SalmonEggPackageVersion Condition=\"'$(SalmonEggPackageVersion)' == ''\">$(SalmonEggDisplayVersion).0</SalmonEggPackageVersion>",
            rootBuildProperties,
            StringComparison.Ordinal);

        // Second-owner ban: the GUI project may only consume the shared properties. Redeclaring one
        // here is how the packaged MSIX and the CLI artifacts silently drift onto two versions.
        Assert.DoesNotContain("<SalmonEggDisplayVersion>", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<SalmonEggPackageVersion>", projectFile, StringComparison.Ordinal);

        Assert.Contains("<Version>$(SalmonEggPackageVersion)</Version>", projectFile, StringComparison.Ordinal);
        Assert.Contains(
            "<ApplicationDisplayVersion>$(SalmonEggDisplayVersion)</ApplicationDisplayVersion>",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains("GenerateVersionedApplicationManifests", projectFile, StringComparison.Ordinal);
        Assert.Contains(
            "<WindowsAppxManifestPath>$(SalmonEggGeneratedPackageManifest)</WindowsAppxManifestPath>",
            projectFile,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<CustomAppxManifest Include=\"$(SalmonEggGeneratedPackageManifest)\"",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ApplicationManifest>$(SalmonEggGeneratedApplicationManifest)</ApplicationManifest>",
            projectFile,
            StringComparison.Ordinal);

        Assert.Contains("__SALMONEGG_PACKAGE_VERSION__", packageManifestTemplate, StringComparison.Ordinal);
        Assert.Contains("__SALMONEGG_PACKAGE_VERSION__", applicationManifestTemplate, StringComparison.Ordinal);
    }


    [Fact]
    public void PackageManifest_DeclaresStableMsixIdentityAndApplicationId()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageManifestTemplate = File.ReadAllText(
            Path.Combine(repositoryRoot, "SalmonEgg", "SalmonEgg", "Package.appxmanifest"));

        // Cross-platform contract: identity is package metadata, not a Windows runtime dependency.
        Assert.Contains("Name=\"SalmonEgg.SalmonEgg\"", packageManifestTemplate, StringComparison.Ordinal);
        Assert.Contains("Id=\"App\"", packageManifestTemplate, StringComparison.Ordinal);
        Assert.Contains("Publisher=\"CN=0B694F0E-510C-433A-A6F7-1484D6A39E19\"", packageManifestTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_UsesProjectVersionAndValidatesMsixSigningIdentity()
    {
        var repositoryRoot = FindRepositoryRoot();
        var releaseWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release-packaging.yml"));

        Assert.Contains("$displayVersion", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Version=\"1.0.0\"", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("Import-PfxCertificate", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("$certificate.HasPrivateKey", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("$certificate.Subject -ne $expectedPublisher", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("PackageCertificateThumbprint", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageCertificateKeyFile", releaseWorkflow, StringComparison.Ordinal);
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
