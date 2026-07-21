using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

public sealed class ContentDialogHostComplianceTests
{
    [Fact]
    public void ContentDialogHost_ProjectsHostActualThemeOntoDialog()
    {
        var helper = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Utilities\ContentDialogHost.cs"));

        Assert.Contains("dialog.XamlRoot = xamlRoot", helper, StringComparison.Ordinal);
        Assert.Contains("dialog.RequestedTheme = ResolveHostTheme(xamlRoot)", helper, StringComparison.Ordinal);
        Assert.Contains("dialog.RequestedTheme = host.ActualTheme", helper, StringComparison.Ordinal);
        Assert.Contains("content.ActualTheme", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementTheme.Light", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementTheme.Dark", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void UiInteractionService_AttachesAllContentDialogsToHostTheme()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs"));

        Assert.Contains("ContentDialogHost.AttachToXamlRoot", code, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(code, "ContentDialogHost.AttachToXamlRoot"));
        Assert.DoesNotContain("XamlRoot = xamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DataStorageSettingsPage_AttachesContentDialogsToHostTheme()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml.cs"));

        Assert.Contains("ContentDialogHost.AttachToElement", code, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(code, "ContentDialogHost.AttachToElement"));
        Assert.DoesNotContain("XamlRoot = XamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme =", code, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string GetRepoPath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            relativePath.Replace('\\', Path.DirectorySeparatorChar)));
}
