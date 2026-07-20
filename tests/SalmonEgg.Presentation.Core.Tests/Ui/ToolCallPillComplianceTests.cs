using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

public sealed class ToolCallPillComplianceTests
{
    [Fact]
    public void ToolCallPill_StatusFlagsRefreshBindableVisualState()
    {
        var code = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml.cs"));

        Assert.Contains(
            "DependencyProperty.Register(nameof(IsInProgress), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependencyProperty.Register(nameof(IsFailed), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "DependencyProperty.Register(nameof(IsCancelled), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));",
            code,
            StringComparison.Ordinal);
    }

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
    public void ToolCallPill_CustomizesExpanderWithLocalLightweightResources()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        Assert.Contains("<Expander.Resources>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderHeaderBackground\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderHeaderBorderBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderHeaderPadding\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderContentBackground\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderContentBorderBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderChevronButtonSize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderChevronGlyphSize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderChevronMargin\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderChevronForeground\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Thickness x:Key=\"ExpanderHeaderPadding\">0</Thickness>", xaml, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"ExpanderChevronButtonSize\">0</x:Double>", xaml, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"ExpanderChevronGlyphSize\">0</x:Double>", xaml, StringComparison.Ordinal);
        Assert.Contains("<Thickness x:Key=\"ExpanderChevronMargin\">0</Thickness>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Template\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_DetailHeightsDensifyOnShortWindowHeights()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml"));

        Assert.Contains("x:Name=\"DetailHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RawInputScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RawOutputScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"160\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"140\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"DetailScrollViewer.MaxHeight\" Value=\"320\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RawInputScrollViewer.MaxHeight\" Value=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RawOutputScrollViewer.MaxHeight\" Value=\"220\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);

        // VSM must live on the single content root (RootFrame Border), not as a second UserControl child.
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
