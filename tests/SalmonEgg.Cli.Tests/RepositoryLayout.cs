using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SalmonEgg.Cli.Tests;

/// <summary>
/// Locates repository-level release facts that CLI contract tests assert against.
/// </summary>
/// <remarks>
/// Tests must never restate the release version as a literal. The version is owned by the
/// repository root <c>Directory.Build.props</c> so the GUI and CLI artifacts cannot drift apart;
/// a hardcoded copy in a test is a second owner that turns every release bump into a red gate.
/// </remarks>
internal static class RepositoryLayout
{
    /// <summary>
    /// Walks up from the test output directory to the directory holding the solution file.
    /// </summary>
    public static string FindRoot()
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

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    /// <summary>
    /// Reads the shared display version (for example <c>1.1.0</c>) from the root build properties.
    /// </summary>
    public static string ReadSharedDisplayVersion()
    {
        var propertiesPath = Path.Combine(FindRoot(), "Directory.Build.props");
        var properties = File.ReadAllText(propertiesPath);
        var match = Regex.Match(
            properties,
            @"<SalmonEggDisplayVersion[^>]*>(?<version>[^<]+)</SalmonEggDisplayVersion>",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"SalmonEggDisplayVersion is not declared in '{propertiesPath}'. The shared release "
                + "identity must stay in the repository root build properties.");
        }

        return match.Groups["version"].Value.Trim();
    }
}
