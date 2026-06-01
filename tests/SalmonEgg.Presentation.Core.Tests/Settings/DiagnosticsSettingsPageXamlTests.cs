using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class DiagnosticsSettingsPageXamlTests
{
    [Fact]
    public void DiagnosticsSettingsPage_Resources_DoNotKeepRemovedLiveLogActionKeys()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = LoadFile(resourceFile);

            Assert.DoesNotContain("Diagnostics_OpenLiveLog.Content", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("Diagnostics_LiveLogCollapse.Content", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DiagnosticsSettingsPage_LiveLogViewer_RemainsBehindNativeExpander()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");

        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{x:Bind ViewModel.LiveLogViewer.IsExpanded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextChanged=\"OnLiveLogTextChanged\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSettingsPage_ExposesVoiceDiagnosticsSectionThroughViewModel()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.VoiceHeader\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SupportStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.PermissionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SessionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.TimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RecommendationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RefreshSnapshotCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.VoiceProbeHeader\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StartProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StopProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeTimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeCapturedText", xaml, StringComparison.Ordinal);
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
