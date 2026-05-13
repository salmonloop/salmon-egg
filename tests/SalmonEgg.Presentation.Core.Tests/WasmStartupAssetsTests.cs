using System.Xml.Linq;

namespace SalmonEgg.Presentation.Core.Tests;

public sealed class WasmStartupAssetsTests
{
    [Fact]
    public void WasmAppManifest_UsesSalmonEggSplashAsset()
    {
        var manifest = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\AppManifest.js");

        Assert.Contains("displayName: \"SalmonEgg\"", manifest, StringComparison.Ordinal);
        Assert.Contains("splashScreenImage: \"splash_screen.scale-200.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("splashScreenColor: \"#ffffff\"", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uno-assets.platform.uno", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_DeclaresSingleUnoSplashScreenSource()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));
        var splashScreens = project.Descendants("UnoSplashScreen").ToArray();

        var splashScreen = Assert.Single(splashScreens);
        Assert.Equal(@"Assets\Splash\splash_screen.png", (string?)splashScreen.Attribute("Include"));
        Assert.Equal("300,300", (string?)splashScreen.Attribute("BaseSize"));
        Assert.Equal("#FFFFFF", (string?)splashScreen.Attribute("Color"));
        Assert.True(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Assets\Splash\splash_screen.png")));
        Assert.False(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Assets\Splash\splash_screen.svg")));
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath)
        => Path.Combine(FindRepoRoot(), NormalizeRelativePath(relativePath));

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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
