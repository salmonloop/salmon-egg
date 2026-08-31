using System;
using System.IO;
using System.Reflection;

namespace SalmonEgg.Cli.Tests;

/// <summary>
/// Locates repository-level release facts that CLI contract tests assert against.
/// </summary>
/// <remarks>
/// Tests must never restate the release version as a literal. The version is owned by the git tag
/// and stamped into the assemblies by MinVer at build time, so the GUI and CLI artifacts cannot
/// drift apart; a hardcoded copy in a test is a second owner that turns every release bump into a
/// red gate.
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
    /// Reads the informational version MinVer stamped onto the CLI assembly (for example <c>1.1.0</c>).
    /// </summary>
    public static string ReadCliInformationalVersion()
    {
        var attribute = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attribute is null || string.IsNullOrWhiteSpace(attribute.InformationalVersion))
        {
            throw new InvalidOperationException(
                "SalmonEgg.Cli carries no AssemblyInformationalVersion. The release identity is derived "
                + "from the git tag by MinVer and must stay stamped onto the shipped assembly.");
        }

        return attribute.InformationalVersion;
    }
}
