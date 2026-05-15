using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class DiagnosticsSettingsPageXamlTests
{
    [Fact]
    public void DiagnosticsSettingsPage_LiveLogViewer_RemainsBehindNativeExpander()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");

        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{x:Bind ViewModel.LiveLogViewer.IsExpanded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextChanged=\"OnLiveLogTextChanged\"", xaml, StringComparison.Ordinal);
    }

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
        return File.ReadAllText(Path.Combine(root, NormalizeRelativePath(relativePath)));
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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
