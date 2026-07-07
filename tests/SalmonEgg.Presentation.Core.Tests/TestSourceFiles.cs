using System;
using System.IO;

namespace SalmonEgg.Presentation.Core.Tests;

internal static class TestSourceFiles
{
    public static string ReadAllText(string relativePath)
        => File.ReadAllText(GetPath(relativePath));

    public static string GetPath(string relativePath)
        => Path.Combine(FindRepositoryRoot(), NormalizeRelativePath(relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath))
                && Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "SalmonEgg")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
