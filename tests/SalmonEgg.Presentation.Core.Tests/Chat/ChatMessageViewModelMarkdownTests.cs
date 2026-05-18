using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatMessageViewModelMarkdownTests
{
    [Fact]
    public void AssistantText_WithClosedFence_UsesMarkdownReady()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-1",
            new TextContentBlock("```csharp\nvar value = 1;\n```"),
            isOutgoing: false);

        Assert.Equal(ChatMarkdownRenderMode.MarkdownReady, vm.MarkdownPresentation.RenderMode);
        Assert.True(vm.MarkdownPresentation.ShouldRenderMarkdown);
        Assert.False(vm.MarkdownPresentation.ShouldRenderPlainText);
        Assert.Equal(ChatMarkdownRenderMode.MarkdownReady, vm.MarkdownRenderMode);
        Assert.True(vm.ShouldRenderMarkdown);
        Assert.False(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void AssistantText_WithUnclosedFence_UsesPlainStreaming()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-2",
            new TextContentBlock("```csharp\nvar value = 1;"),
            isOutgoing: false);

        Assert.Equal(ChatMarkdownRenderMode.PlainStreaming, vm.MarkdownRenderMode);
        Assert.False(vm.ShouldRenderMarkdown);
        Assert.True(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void AssistantPlainText_UsesPlainStreaming()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-plain",
            new TextContentBlock("hello"),
            isOutgoing: false);

        Assert.Equal(ChatMarkdownRenderMode.PlainStreaming, vm.MarkdownRenderMode);
        Assert.False(vm.ShouldRenderMarkdown);
        Assert.True(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void AssistantInlineMarkdown_UsesMarkdownReady()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-inline",
            new TextContentBlock("Use `code` here"),
            isOutgoing: false);

        Assert.Equal(ChatMarkdownRenderMode.MarkdownReady, vm.MarkdownRenderMode);
        Assert.True(vm.ShouldRenderMarkdown);
        Assert.False(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void OutgoingText_AlwaysUsesPlainStreaming()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-3",
            new TextContentBlock("**bold**"),
            isOutgoing: true);

        Assert.Equal(ChatMarkdownRenderMode.PlainStreaming, vm.MarkdownRenderMode);
        Assert.False(vm.ShouldRenderMarkdown);
        Assert.True(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void MarkMarkdownRenderFailed_MakesFallbackSticky()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-4",
            new TextContentBlock("normal text"),
            isOutgoing: false);

        vm.MarkMarkdownRenderFailed();
        vm.TextContent = "```csharp\nConsole.WriteLine(\"ok\");\n```";

        Assert.Equal(ChatMarkdownRenderMode.FallbackPlain, vm.MarkdownRenderMode);
        Assert.True(vm.IsMarkdownFallbackSticky);
        Assert.False(vm.ShouldRenderMarkdown);
        Assert.True(vm.ShouldRenderPlainText);
    }

    [Fact]
    public void CopyableMarkdownCodeBlockText_ReturnsFirstClosedFenceForMarkdownMessage()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-copy",
            new TextContentBlock("```csharp\nvar done = true;\n```\n\n```text\nsecond\n```"),
            isOutgoing: false);

        Assert.Equal("var done = true;", vm.MarkdownPresentation.CopyableCodeBlockText);
        Assert.True(vm.MarkdownPresentation.HasCopyableCodeBlock);
        Assert.True(vm.HasCopyableMarkdownCodeBlock);
        Assert.Equal("var done = true;", vm.CopyableMarkdownCodeBlockText);
    }

    [Fact]
    public void CopyableMarkdownCodeBlockText_SupportsClosedTildeFence()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-copy-tilde",
            new TextContentBlock("~~~json\n{\"done\":true}\n~~~"),
            isOutgoing: false);

        Assert.True(vm.HasCopyableMarkdownCodeBlock);
        Assert.Equal("{\"done\":true}", vm.CopyableMarkdownCodeBlockText);
    }

    [Fact]
    public void CopyableMarkdownCodeBlockText_IsEmptyForPlainOrFallbackMessage()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-copy-empty",
            new TextContentBlock("```csharp\nvar partial = true;"),
            isOutgoing: false);

        Assert.Equal(string.Empty, vm.MarkdownPresentation.CopyableCodeBlockText);
        Assert.False(vm.MarkdownPresentation.HasCopyableCodeBlock);
        Assert.False(vm.HasCopyableMarkdownCodeBlock);
        Assert.Equal(string.Empty, vm.CopyableMarkdownCodeBlockText);
    }

    [Fact]
    public void MarkdownPresentation_UsesSingleSelectionPolicyForMarkdownMessages()
    {
        var inlineMarkdownVm = ChatMessageViewModel.CreateFromTextContent(
            "m-selectable",
            new TextContentBlock("Use `code` here"),
            isOutgoing: false);
        var fencedMarkdownVm = ChatMessageViewModel.CreateFromTextContent(
            "m-nonselectable",
            new TextContentBlock("```csharp\nvar value = 1;\n```"),
            isOutgoing: false);

        Assert.True(inlineMarkdownVm.MarkdownPresentation.AllowsNativeSelection);
        Assert.False(fencedMarkdownVm.MarkdownPresentation.AllowsNativeSelection);
    }

    [Fact]
    public void MarkdownPresentation_WhenFallbackSticky_StaysAuthoritativeAfterTextChanges()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-projection",
            new TextContentBlock("plain text"),
            isOutgoing: false);

        vm.MarkMarkdownRenderFailed();
        vm.TextContent = "```csharp\nConsole.WriteLine(\"ok\");\n```";

        Assert.Equal(ChatMarkdownRenderMode.FallbackPlain, vm.MarkdownPresentation.RenderMode);
        Assert.True(vm.MarkdownPresentation.ShouldRenderPlainText);
        Assert.False(vm.MarkdownPresentation.ShouldRenderMarkdown);
        Assert.Equal(string.Empty, vm.MarkdownPresentation.CopyableCodeBlockText);
        Assert.False(vm.MarkdownPresentation.HasCopyableCodeBlock);
    }

    [Fact]
    public async Task CopyTextCommand_ForwardsConfiguredNonWhitespaceText()
    {
        var copiedText = (string?)null;
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-copy-command",
            new TextContentBlock("copy me"),
            isOutgoing: false);
        vm.ConfigureShellActions(
            text =>
            {
                copiedText = text;
                return Task.FromResult(true);
            },
            _ => Task.FromResult(true));

        await vm.CopyTextCommand.ExecuteAsync("copy me");

        Assert.Equal("copy me", copiedText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CopyTextCommand_RejectsMissingOrWhitespaceText(string? text)
    {
        var copyCount = 0;
        var vm = new ChatMessageViewModel();
        vm.ConfigureShellActions(
            _ =>
            {
                copyCount++;
                return Task.FromResult(true);
            },
            _ => Task.FromResult(true));

        Assert.False(vm.CopyTextCommand.CanExecute(text));
        await vm.CopyTextCommand.ExecuteAsync(text);

        Assert.Equal(0, copyCount);
    }

    [Fact]
    public async Task CopyTextCommand_RejectsTextBeforeShellActionsAreConfigured()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-unconfigured-copy",
            new TextContentBlock("copy me"),
            isOutgoing: false);

        Assert.False(vm.CopyTextCommand.CanExecute("copy me"));
        await vm.CopyTextCommand.ExecuteAsync("copy me");
    }

    [Fact]
    public async Task OpenMarkdownLinkCommand_ForwardsAllowedUriThroughConfiguredShellAction()
    {
        var openedUri = (Uri?)null;
        var vm = new ChatMessageViewModel();
        vm.ConfigureShellActions(
            _ => Task.FromResult(true),
            uri =>
            {
                openedUri = uri;
                return Task.FromResult(true);
            });

        await vm.OpenMarkdownLinkCommand.ExecuteAsync("https://example.com/docs");

        Assert.NotNull(openedUri);
        Assert.Equal("https://example.com/docs", openedUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///tmp/secret.txt")]
    [InlineData("mailto:test@example.com")]
    [InlineData("/relative/path")]
    public async Task OpenMarkdownLinkCommand_RejectsDisallowedLinks(string rawLink)
    {
        var openCount = 0;
        var vm = new ChatMessageViewModel();
        vm.ConfigureShellActions(
            _ => Task.FromResult(true),
            _ =>
            {
                openCount++;
                return Task.FromResult(true);
            });

        Assert.False(vm.OpenMarkdownLinkCommand.CanExecute(rawLink));
        await vm.OpenMarkdownLinkCommand.ExecuteAsync(rawLink);

        Assert.Equal(0, openCount);
    }
}
