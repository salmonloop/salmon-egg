using System;
using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatStylesMarkdownXamlTests
{
    [Fact]
    public void MessageTemplates_SplitIncomingAndOutgoingBubbles()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("<DataTemplate x:Key=\"IncomingMessageTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<DataTemplate x:Key=\"OutgoingMessageTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"12,12,12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"12,12,0,12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{x:Bind IsOutgoing", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplateSelector_BindsDirectionalTemplates()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("<converters:MessageTemplateSelector x:Key=\"MessageTemplateSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IncomingTemplate=\"{StaticResource IncomingMessageTemplate}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OutgoingTemplate=\"{StaticResource OutgoingMessageTemplate}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_IncomingBodyContainsMarkdownAndPlainRenderSlots()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("xmlns:controls=\"using:SalmonEgg.Controls\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<controls:MarkdownTextPresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("MessageViewModel=\"{x:Bind}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind ShouldRenderMarkdown, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind ShouldRenderPlainText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind ShouldShowToolCallPill, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
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
    public void MessageTemplate_XLoadElementsDeclareNames()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("x:Name=\"IncomingPlainTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IncomingMarkdownPresenter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IncomingToolCallPill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OutgoingPlainTextBlock\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViews_UseMessageTemplateSelector()
    {
        var chatViewXaml = LoadRepoFile("SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "ChatView.xaml");
        var miniChatViewXaml = LoadRepoFile("SalmonEgg", "SalmonEgg", "Presentation", "Views", "MiniWindow", "MiniChatView.xaml");

        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplate}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplate}\"", miniChatViewXaml, StringComparison.Ordinal);
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

    [Fact]
    public void MarkdownTextPresenter_DoesNotEagerlyAttachBothWindowsVariants()
    {
        var source = LoadRepoFile("SalmonEgg", "SalmonEgg", "Controls", "MarkdownTextPresenter.cs");

        Assert.DoesNotContain("Children.Add(_nonSelectableMarkdown);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Add(_selectableMarkdown);", source, StringComparison.Ordinal);
    }

    private static string LoadChatStylesXaml()
    {
        return LoadRepoFile("SalmonEgg", "SalmonEgg", "Styles", "ChatStyles.xaml");
    }

    private static string LoadResourcesResw(string localeFolder)
    {
        return LoadRepoFile("SalmonEgg", "SalmonEgg", "Strings", localeFolder, "Resources.resw");
    }

    private static string LoadRepoFile(params string[] segments)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. segments]));
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
