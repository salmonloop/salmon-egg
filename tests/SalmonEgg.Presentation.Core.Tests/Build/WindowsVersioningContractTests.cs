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

        // The release identity is derived from the git tag by MinVer in the repository-root
        // Directory.Build.props, so the GUI and the CLI ship the same identity without anyone editing
        // a file. The four-part Store version is the three-part display version plus ".0" (the MSIX
        // revision digit); Windows Installer consumers read the three-part form instead.
        Assert.Contains(
            "<Target Name=\"SalmonEggDeriveReleaseIdentity\" AfterTargets=\"MinVer\">",
            rootBuildProperties,
            StringComparison.Ordinal);
        Assert.Contains(
            "<SalmonEggDisplayVersion Condition=\"'$(SalmonEggDisplayVersion)' == ''\">$(MinVerMajor).$(MinVerMinor).$(MinVerPatch)</SalmonEggDisplayVersion>",
            rootBuildProperties,
            StringComparison.Ordinal);
        Assert.Contains(
            "<SalmonEggPackageVersion Condition=\"'$(SalmonEggPackageVersion)' == ''\">$(SalmonEggDisplayVersion).0</SalmonEggPackageVersion>",
            rootBuildProperties,
            StringComparison.Ordinal);

        // MinVer itself stamps AssemblyVersion as Major.0.0.0, which would make the About page,
        // diagnostics and telemetry service.version report "1.0.0" while the Store shows 1.3.1.
        // The derive target must override it unconditionally (a Condition on "already empty" never
        // fires because MinVer has already assigned the property).
        Assert.Contains(
            "<AssemblyVersion>$(SalmonEggPackageVersion)</AssemblyVersion>",
            rootBuildProperties,
            StringComparison.Ordinal);

        // Second-owner ban: the GUI project may only consume the shared derive target. Redeclaring a
        // version here is how the packaged MSIX and the CLI artifacts silently drift onto two versions.
        Assert.DoesNotContain("<SalmonEggDisplayVersion>", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<SalmonEggPackageVersion>", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", projectFile, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"MinVer\" />", projectFile, StringComparison.Ordinal);

        // The manifest templates are stamped with $(SalmonEggPackageVersion) at execution time, and
        // MinVer only fills that value inside its own target. Chaining the generation after MinVer is
        // what keeps the tag-derived version from being replaced by a pre-MinVer default, so the
        // ordering is part of the contract, not an implementation detail.
        Assert.Contains(
            "<Target Name=\"GenerateVersionedApplicationManifests\"",
            projectFile,
            StringComparison.Ordinal);
        Assert.Contains("AfterTargets=\"MinVer\"", projectFile, StringComparison.Ordinal);
        // Both the derive target and this one hang off AfterTargets="MinVer"; MSBuild does not define
        // the order between two targets on the same hook, so the dependency is what guarantees the
        // manifest is generated after the tag-derived version exists.
        Assert.Contains(
            "DependsOnTargets=\"SalmonEggDeriveReleaseIdentity\"",
            projectFile,
            StringComparison.Ordinal);
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
        // The release identity is tag-derived, so the workflow must run MinVer to read it and must
        // check out the full history for the tag to be reachable at all.
        Assert.Contains("-t:MinVer", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", releaseWorkflow, StringComparison.Ordinal);
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
