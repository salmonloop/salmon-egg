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

        // 只守护「不得重新引入手动 XamlRoot/主题覆写」的回归;不锁调用次数——
        // 次数是实现摆放断言(§5.5),新增一个合法对话框也会误报。
        Assert.Contains("ContentDialogHost.AttachToXamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlRoot = xamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiInteractionService_ResolvesDialogHostFromActiveWindowBeforeMainWindowFallback()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs"));

        var activeWindowIndex = code.IndexOf("ResolveWindowXamlRoot(_activationSignalSource.ActiveWindow)", StringComparison.Ordinal);
        var mainWindowIndex = code.IndexOf("ResolveWindowXamlRoot(App.MainWindowInstance)", StringComparison.Ordinal);

        Assert.True(activeWindowIndex >= 0);
        Assert.True(mainWindowIndex > activeWindowIndex);
    }

    [Fact]
    public void DataStorageSettingsPage_AttachesContentDialogsToHostTheme()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml.cs"));

        Assert.Contains("ContentDialogHost.AttachToElement", code, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlRoot = XamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptTextAsync_TextBoxStretchesWithoutFixedMinWidth()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs"));
        var methodStart = code.IndexOf("public async Task<string?> PromptTextAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "PromptTextAsync must remain on UiInteractionService.");
        var nextMethod = code.IndexOf("public async Task", methodStart + 1, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = code.Length;
        }

        var method = code.Substring(methodStart, nextMethod - methodStart);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", method, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 320", method, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth =", method, StringComparison.Ordinal);
        Assert.Contains("ContentDialogHost.AttachToXamlRoot", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme =", method, StringComparison.Ordinal);
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
