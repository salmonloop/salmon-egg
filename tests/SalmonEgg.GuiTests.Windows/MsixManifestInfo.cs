using System;
using System.IO;
using System.Xml.Linq;

namespace SalmonEgg.GuiTests.Windows;

internal sealed record MsixManifestInfo(string IdentityName, string ApplicationId)
{
    public static MsixManifestInfo LoadFromRepo()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, "SalmonEgg", "SalmonEgg", "Package.appxmanifest");
        var document = XDocument.Load(manifestPath);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

        var identityName = document.Root?.Element(ns + "Identity")?.Attribute("Name")?.Value;
        var appId = document.Root?.Element(ns + "Applications")?.Element(ns + "Application")?.Attribute("Id")?.Value;

        if (string.IsNullOrWhiteSpace(identityName) || string.IsNullOrWhiteSpace(appId))
        {
            throw new InvalidOperationException($"Unable to read MSIX identity/app id from '{manifestPath}'.");
        }

        return new MsixManifestInfo(identityName, appId);
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
