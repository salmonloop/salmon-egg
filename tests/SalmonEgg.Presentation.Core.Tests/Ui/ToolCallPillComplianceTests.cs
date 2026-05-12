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
