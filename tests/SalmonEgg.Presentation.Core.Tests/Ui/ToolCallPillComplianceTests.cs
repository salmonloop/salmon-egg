using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

public sealed class ToolCallPillComplianceTests
{
    // 本文件只保留 AGENTS.md §5.6 允许的形态约束:防止重新引入已移除的补偿策略
    // (双按钮权限硬编码、ToggleButton 第二展开 owner、手动 RequestedTheme、模板整体覆写)。
    // DP 注册源码扫描与像素值断言属 §5.5 禁止的实现摆放断言,已移除;
    // 展开/密度的行为语义由 ToolCallPillExpansionPolicyTests 等行为测试承担。

    [Fact]
    public void ChatStyles_ToolCallPillVisibilityDoesNotDependOnPayloadOnly()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml"));

        Assert.Contains(
            "Visibility=\"{x:Bind ShouldShowToolCallPill, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_XamlBindsDedicatedCancelledIcon()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        Assert.Contains(
            "Visibility=\"{x:Bind IsCancelled, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_XamlProjectsAllPermissionOptions()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        Assert.Contains(
            "ItemsSource=\"{x:Bind PermissionOptions, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{x:Bind SelectCommand, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AllowPermissionOption", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectPermissionOption", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_UsesExpanderAsSingleExpansionOwner()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml.cs"));

        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ToggleButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RootButton_Checked", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RootButton_Unchecked", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_isSynchronizingRootButton", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_CustomizesExpanderWithLightweightStylingNotRetemplating()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        // 只锁「Lightweight Styling 而非整模板覆写」的原生行为边界;具体资源键与像素值不锁。
        Assert.Contains("<Expander.Resources>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Template\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_DensityStatesLiveOnSingleContentRootWithoutThemeOverride()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);

        // VSM must live on the single content root (RootFrame Border), not as a second UserControl child.
        Assert.Contains("x:Name=\"DetailHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RootFrame\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("x:Name=\"RootFrame\"", StringComparison.Ordinal)
            < xaml.IndexOf("DetailHeightStates", StringComparison.Ordinal));
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
