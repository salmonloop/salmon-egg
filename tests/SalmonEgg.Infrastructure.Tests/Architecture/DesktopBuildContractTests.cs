using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

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

    [Fact]
    public void SolutionBuild_IncludesExecutableAppProject()
    {
        var solution = LoadText("SalmonEgg.sln");

        Assert.Contains(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_AllowsPrereleaseVisualStudioToolchains()
    {
        var script = LoadText("build.bat");

        Assert.Contains("vswhere.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmBuildScripts_DoNotPassGlobalTrimPropertiesToReferencedProjects()
    {
        var windowsScript = LoadText("build.bat");
        var unixScript = LoadText("build.sh");

        Assert.Contains("--framework net10.0-browserwasm", windowsScript, StringComparison.Ordinal);
        Assert.Contains("--framework net10.0-browserwasm", unixScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PublishTrimmed", windowsScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PublishTrimmed", unixScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:TrimMode", windowsScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:TrimMode", unixScript, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTarget_EmbedsGeneratedVersionedApplicationManifest()
    {
        var project = XDocument.Parse(LoadText("SalmonEgg/SalmonEgg/SalmonEgg.csproj"));
        var desktopPropertyGroup = project.Root!
            .Elements("PropertyGroup")
            .Where(static group => (string?)group.Attribute("Condition") == "'$(TargetFramework)' == 'net10.0-desktop'")
            .First();

        Assert.Equal("$(SalmonEggGeneratedApplicationManifest)", (string?)desktopPropertyGroup.Element("ApplicationManifest"));
        Assert.EndsWith("app.manifest", (string?)desktopPropertyGroup.Element("SalmonEggGeneratedApplicationManifest"), StringComparison.Ordinal);

        var manifestGenerationTarget = project.Root
            .Elements("Target")
            .Where(static target => target.Descendants("WriteLinesToFile")
                .Any(static write => (string?)write.Attribute("File") == "$(SalmonEggGeneratedApplicationManifest)"))
            .Single();

        // The condition mirrors MinVer's own skip conditions: a design-time or skipped MinVer would
        // make the DependsOnTargets reference resolve to a missing target (MSB4057).
        Assert.Equal(
            "'$(SalmonEggGeneratedApplicationManifest)' != '' AND '$(DesignTimeBuild)' != 'true' AND '$(MinVerSkip)' != 'true'",
            (string?)manifestGenerationTarget.Attribute("Condition"));
        // The version substitution needs MinVer's output, and the WindowsAppSDK mt.exe merge consumes
        // the manifest before BeforeCompile, so the target must pull MinVer in itself rather than
        // hang off AfterTargets="MinVer" (that hook fires too late on a clean obj).
        Assert.Equal("MinVer", (string?)manifestGenerationTarget.Attribute("DependsOnTargets"));
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
