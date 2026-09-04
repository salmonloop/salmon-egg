namespace SalmonEgg.Domain.Models.Cli;

/// <summary>
/// The command name every installer registers.
/// </summary>
/// <remarks>
/// One constant for the managed side. The same name is also spelled in places no C# constant can reach —
/// the MSIX manifest's execution alias, the WiX authoring, the Debian package's symlink, the macOS
/// postinstall — so those are pinned by GitHubWorkflowContractTests and the per-installer contract gates
/// instead. This exists so the app's own PATH lookup cannot drift from them independently.
/// </remarks>
public static class CliCommandNames
{
    /// <summary>The command as a user types it, with no extension.</summary>
    public const string Command = "salmon-egg";

    /// <summary>The file name on Windows, where PATH entries carry the extension.</summary>
    public const string WindowsFileName = Command + ".exe";

    /// <summary>The directory the installers place the command in, relative to the app's payload.</summary>
    public const string PayloadDirectory = "cli";
}
