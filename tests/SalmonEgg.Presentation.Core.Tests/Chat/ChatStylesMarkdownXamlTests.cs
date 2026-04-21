using System;
using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatStylesMarkdownXamlTests
{
    [Fact]
    public void MessageTemplate_PreservesIncomingAndOutgoingBubbles()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("CornerRadius=\"12,12,12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"12,12,0,12\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_IncomingBodyContainsMarkdownAndPlainRenderSlots()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("xmlns:controls=\"using:SalmonEgg.Controls\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<controls:MarkdownTextPresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("MessageViewModel=\"{x:Bind}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ShouldRenderMarkdown", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ShouldRenderPlainText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_UsesXBindForRenderSwitching()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("Text=\"{x:Bind TextContent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{Binding ShouldRenderMarkdown", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{Binding ShouldRenderPlainText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_EnablesPlainTextSelectionAndCopyContextMenu()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("IsTextSelectionEnabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border.ContextFlyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"ChatMessageCopyMenu\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCopyMessageClick\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public void ChatMessageCopyMenu_UsesTextLocalizationKey(string localeFolder)
    {
        var resources = LoadResourcesResw(localeFolder);

        Assert.Contains("ChatMessageCopyMenu.Text", resources, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMessageCopyMenu.Content", resources, StringComparison.Ordinal);
    }

    private static string LoadChatStylesXaml()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Styles", "ChatStyles.xaml"));
    }

    private static string LoadResourcesResw(string localeFolder)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Strings", localeFolder, "Resources.resw"));
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
