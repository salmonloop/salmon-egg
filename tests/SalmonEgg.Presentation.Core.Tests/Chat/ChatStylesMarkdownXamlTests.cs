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
        Assert.Contains("IncomingMessageBorderStyle", xaml, StringComparison.Ordinal);
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
        Assert.Contains("ShouldRenderMarkdown=\"{x:Bind MarkdownPresentation.ShouldRenderMarkdown, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RenderFailureSink=\"{x:Bind}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LinkCommand=\"{x:Bind OpenMarkdownLinkCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageViewModel=\"{x:Bind}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind MarkdownPresentation.ShouldRenderMarkdown, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind MarkdownPresentation.ShouldRenderPlainText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"IncomingToolCallPill\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_MarkdownReadingTypographyUsesThemeResources()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("LinkForeground=\"{ThemeResource AccentTextFillColorPrimaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CodeBlockBackground=\"{ThemeResource ControlFillColorSecondaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CodeBlockBorderBrush=\"{ThemeResource ControlStrokeColorDefaultBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("InlineCodeBackground=\"{ThemeResource ControlFillColorTertiaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("QuoteForeground=\"{ThemeResource TextFillColorSecondaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("QuoteBorderBrush=\"{ThemeResource AccentFillColorDefaultBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TableBorderBrush=\"{ThemeResource ControlStrokeColorDefaultBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"22\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_CodeBlockCopyButtonIsViewModelDrivenAndLocalized()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("x:Name=\"IncomingMarkdownCopyCodeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"ChatMarkdownCopyCodeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Load=\"{x:Bind MarkdownPresentation.HasCopyableCodeBlock, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind CopyTextCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{x:Bind MarkdownPresentation.CopyableCodeBlockText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatMessage.CopyCodeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<SymbolIcon Symbol=\"Copy\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"OnCopyTextClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"OnCopyCodeBlockClick\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_UsesXBindForRenderSwitching()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("Text=\"{x:Bind DisplayBodyText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MarkdownPresentation.ShouldRenderMarkdown", xaml, StringComparison.Ordinal);
        Assert.Contains("MarkdownPresentation.ShouldRenderPlainText", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled=\"{x:Bind MarkdownPresentation.AllowsNativeSelection, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{Binding ShouldRenderMarkdown", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{Binding ShouldRenderPlainText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_XLoadElementsDeclareNames()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("x:Name=\"IncomingPlainTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IncomingMarkdownPresenter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OutgoingPlainTextBlock\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplateSelector_BindsDedicatedToolCallTemplate()
    {
        var xaml = LoadChatStylesXaml();

        Assert.Contains("<DataTemplate x:Key=\"ToolCallMessageTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolCallTemplate=\"{StaticResource ToolCallMessageTemplate}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViews_UseMessageTemplateSelector()
    {
        var chatViewXaml = LoadRepoFile("SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "ChatView.xaml");
        var miniChatViewXaml = LoadRepoFile("SalmonEgg", "SalmonEgg", "Presentation", "Views", "MiniWindow", "MiniChatView.xaml");

        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplateSelector}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplateSelector}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplate}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource MessageTemplate}\"", miniChatViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageTemplate_EnablesPlainTextSelectionAndLeafOwnedCopyContextMenu()
    {
        // ContextFlyout must live on the selectable text leaf, not the message Border. Selectable
        // text installs its own TextCommandBarFlyout and marks the bubbling ContextRequested handled,
        // so a Border-level flyout only fires in the empty left-rule/padding gap — never over the
        // body text (AGENTS.md §7 leaf-owned ContextFlyout, mirrors MainPage project/session menus).
        var xaml = LoadChatStylesXaml();

        Assert.Contains("IsTextSelectionEnabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock.ContextFlyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"ChatMessageCopyMenu\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind CopyTextCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border.ContextFlyout>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{x:Bind HasTextContent", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"OnCopyMessageClick\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AiSideMessageTemplates_ExposeReportContextMenuButOutgoingDoesNot()
    {
        var xaml = LoadChatStylesXaml();
        var incoming = ExtractTemplate(xaml, "IncomingMessageTemplate", "OutgoingMessageTemplate");
        var outgoing = ExtractTemplate(xaml, "OutgoingMessageTemplate", "ToolCallMessageTemplate");
        var toolCall = ExtractTemplate(xaml, "ToolCallMessageTemplate", null);

        // Report belongs on every AI-side surface. Incoming leaves host the menu item directly
        // (plain TextBlock + markdown host); the tool-call pill owns its menu internally, so the
        // template projects the Report command into the pill via its ReportCommand DP instead.
        Assert.Contains("x:Uid=\"ChatMessageReportMenu\"", incoming, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ReportContentCommand}\"", incoming, StringComparison.Ordinal);
        Assert.Contains("ReportCommand=\"{x:Bind ReportContentCommand}\"", toolCall, StringComparison.Ordinal);

        // Outgoing user messages copy only; they are never reportable AI content.
        Assert.DoesNotContain("x:Uid=\"ChatMessageReportMenu\"", outgoing, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportCommand=\"{x:Bind ReportContentCommand}\"", outgoing, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ReportContentCommand}\"", outgoing, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallMessageTemplate_ProjectsCopyAndReportCommandsIntoPillLeaf()
    {
        // The tool-call pill owns its right-click menu internally (its selectable
        // title/summary/detail/raw text each install a text-selection command bar that
        // marks ContextRequested handled, so a template Border flyout never fires over
        // the pill body). The message template must therefore project Copy/Report into
        // the pill via command DPs, not host a Border-level ContextFlyout.
        var xaml = LoadChatStylesXaml();
        var toolCall = ExtractTemplate(xaml, "ToolCallMessageTemplate", null);

        Assert.DoesNotContain("<Border.ContextFlyout>", toolCall, StringComparison.Ordinal);
        Assert.Contains("CopyCommand=\"{x:Bind CopyTextCommand}\"", toolCall, StringComparison.Ordinal);
        Assert.Contains("CopyCommandParameter=\"{x:Bind DisplayBodyText, Mode=OneWay}\"", toolCall, StringComparison.Ordinal);
        Assert.Contains("ReportCommand=\"{x:Bind ReportContentCommand}\"", toolCall, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomingMessageBorderStyle_SetsHitTestableBackgroundSoContextFlyoutFiresOverMarkdownGaps()
    {
        // Uno's Border hit-test rule is `Background != null` (Border.IsViewHitImpl); a null background
        // makes the bubble transparent to pointer input, so right-clicks that land in the empty gaps of a
        // markdown paragraph (line spacing, wrap gaps, indents) never reach the ContextFlyout host and the
        // Copy/Report menu never appears. A Transparent background keeps the whole bubble hit-testable while
        // staying visually identical. This is the shared style behind both the incoming and tool-call borders.
        var xaml = LoadChatStylesXaml();
        var style = ExtractIncomingMessageBorderStyle(xaml);

        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", style, StringComparison.Ordinal);
    }

    private static string ExtractIncomingMessageBorderStyle(string xaml)
    {
        var start = xaml.IndexOf("<Style x:Key=\"IncomingMessageBorderStyle\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "IncomingMessageBorderStyle must exist in ChatStyles.xaml.");

        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, "IncomingMessageBorderStyle must be a closed Style element.");
        return xaml.Substring(start, end - start);
    }

    private static string ExtractTemplate(string xaml, string key, string? nextKey)
    {
        var start = xaml.IndexOf($"<DataTemplate x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{key} must exist in ChatStyles.xaml.");

        if (nextKey is null)
        {
            return xaml.Substring(start);
        }

        var end = xaml.IndexOf($"<DataTemplate x:Key=\"{nextKey}\"", StringComparison.Ordinal);
        Assert.True(end > start, $"{nextKey} must follow {key} in ChatStyles.xaml.");
        return xaml.Substring(start, end - start);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public void ChatMessageReportMenu_UsesTextLocalizationKey(string localeFolder)
    {
        var resources = LoadResourcesResw(localeFolder);

        Assert.Contains("ChatMessageReportMenu.Text", resources, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMessageReportMenu.Content", resources, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public void ChatMarkdownCopyCodeButton_UsesLocalizedTooltipAndAutomationName(string localeFolder)
    {
        var resources = LoadResourcesResw(localeFolder);

        Assert.Contains("ChatMarkdownCopyCodeButton.ToolTipService.ToolTip", resources, StringComparison.Ordinal);
        Assert.Contains("ChatMarkdownCopyCodeButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name", resources, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMarkdownCopyCodeButton.Content", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownTextPresenter_DoesNotEagerlyAttachBothWindowsVariants()
    {
        var source = LoadRepoFile("SalmonEgg", "SalmonEgg", "Controls", "MarkdownTextPresenter.cs");

        Assert.DoesNotContain("Children.Add(_nonSelectableMarkdown);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Add(_selectableMarkdown);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownTextPresenter_SelectableWindowsVariantDisablesLinkParsingWithOfficialSwitch()
    {
        var source = LoadRepoFile("SalmonEgg", "SalmonEgg", "Controls", "MarkdownTextPresenter.cs");

        Assert.Contains("UseAutoLinks = !isTextSelectionEnabled", source, StringComparison.Ordinal);
        Assert.Contains("DisableLinks = isTextSelectionEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerEntered", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerMoved", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownTextPresenter_CentralizesMarkdownReadingTypography()
    {
        var source = LoadRepoFile("SalmonEgg", "SalmonEgg", "Controls", "MarkdownTextPresenter.cs");

        Assert.Contains("ApplyMarkdownTypography", source, StringComparison.Ordinal);
        Assert.Contains("MarkdownThemes", source, StringComparison.Ordinal);
        Assert.Contains("IRenderFailureSink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMessageViewModel", source, StringComparison.Ordinal);
        Assert.Contains("CodeBlockBackgroundProperty", source, StringComparison.Ordinal);
        Assert.Contains("InlineCodeBackgroundProperty", source, StringComparison.Ordinal);
        Assert.Contains("QuoteBorderBrushProperty", source, StringComparison.Ordinal);
        Assert.Contains("ParagraphLineHeight = 22", source, StringComparison.Ordinal);
        Assert.Contains("UseListExtras = true", source, StringComparison.Ordinal);
        Assert.Contains("LinkCommandProperty", source, StringComparison.Ordinal);
        Assert.Contains("TryExecuteLinkCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMarkdownFenceDetector.HasClosedFence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatMarkdownLinkPolicy.TryResolveLaunchUri", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Uri.TryCreate(e.Link", source, StringComparison.Ordinal);
        Assert.DoesNotContain("App.ServiceProvider", source, StringComparison.Ordinal);
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
