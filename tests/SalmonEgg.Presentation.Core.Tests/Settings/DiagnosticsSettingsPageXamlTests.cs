using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class DiagnosticsSettingsPageXamlTests
{
    [Fact]
    public void DiagnosticsSettingsPage_CodeBehind_UnloadCleanupUsesGuardedAsyncHelper()
    {
        var codeBehind = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs");

        Assert.Contains("private void OnUnloaded", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_ = HandlePageUnloadedAsync();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private async Task HandlePageUnloadedAsync()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_logger.LogError(ex, \"Diagnostics page unload cleanup failed\");", codeBehind, StringComparison.Ordinal);
    }

    private static string LoadFile(string relativePath)
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
