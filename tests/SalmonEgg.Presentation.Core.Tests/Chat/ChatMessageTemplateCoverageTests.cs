using System;
using System.IO;
using System.Linq;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

/// <summary>
/// Portable coverage for transcript template visibility. These tests intentionally
/// construct production content types and assert the resulting ViewModels expose
/// non-blank directional bodies or dedicated template shapes — the same contract
/// Skia/WinUI ListView materialization relies on.
/// </summary>
public sealed class ChatMessageTemplateCoverageTests
{
    [Fact]
    public void Inventory_CoversProductionContentTypesWithoutDuplicates()
    {
        Assert.Equal(
            ChatMessageTemplateCoverage.AllProjectedContentTypes.Count,
            ChatMessageTemplateCoverage.AllProjectedContentTypes.Distinct(StringComparer.Ordinal).Count());

        Assert.Contains("tool_call", ChatMessageTemplateCoverage.DedicatedTemplateContentTypes);
        Assert.Contains("text", ChatMessageTemplateCoverage.DirectionalTextCompatibleContentTypes);
        Assert.Contains("image", ChatMessageTemplateCoverage.DirectionalTextCompatibleContentTypes);
        Assert.Contains("audio", ChatMessageTemplateCoverage.DirectionalTextCompatibleContentTypes);
        Assert.Contains("mode_change", ChatMessageTemplateCoverage.DirectionalTextCompatibleContentTypes);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("image")]
    [InlineData("audio")]
    [InlineData("resource")]
    [InlineData("resource_link")]
    [InlineData("resource_content")]
    [InlineData("mode_change")]
    [InlineData("plan_entry")]
    [InlineData("thinking")]
    public void RepresentativeSnapshot_DirectionalContentType_ProjectsNonBlankDisplayBody(string contentType)
    {
        var snapshot = ChatMessageTemplateCoverage.CreateRepresentativeSnapshot(contentType);
        var viewModel = new ChatMessageViewModel();
        viewModel.ApplySnapshot(snapshot, projectionIndex: 0);

        Assert.False(ChatMessageTemplateCoverage.RequiresDedicatedTemplate(contentType));
        Assert.True(
            ChatMessageTemplateCoverage.ExpectsVisibleDirectionalBody(viewModel),
            $"Content type '{contentType}' projected a blank directional body. " +
            $"TextContent='{viewModel.TextContent}', Title='{viewModel.Title}', DisplayBodyText='{viewModel.DisplayBodyText}'.");
        Assert.False(string.IsNullOrWhiteSpace(viewModel.DisplayBodyText));
        Assert.True(viewModel.HasDisplayBody);
    }

    [Fact]
    public void RepresentativeSnapshot_ToolCall_UsesDedicatedShapeWithoutRequiringDirectionalBody()
    {
        var snapshot = ChatMessageTemplateCoverage.CreateRepresentativeSnapshot("tool_call");
        var viewModel = new ChatMessageViewModel();
        viewModel.ApplySnapshot(snapshot, projectionIndex: 0);

        Assert.True(ChatMessageTemplateCoverage.RequiresDedicatedTemplate("tool_call"));
        Assert.False(ChatMessageTemplateCoverage.ExpectsVisibleDirectionalBody(viewModel));
        Assert.True(viewModel.ShouldShowToolCallPill);
        Assert.Equal("tool_call", viewModel.ContentType);
    }

    [Fact]
    public void PersistedImageWithoutTextContent_StillSurfacesMimeFallback()
    {
        // Real production defect: historical image snapshots stored mime/data only.
        // Directional templates used to bind TextContent and rendered blank rows on Skia.
        var viewModel = new ChatMessageViewModel();
        viewModel.ApplySnapshot(
            new SalmonEgg.Domain.Models.Conversation.ConversationMessageSnapshot
            {
                Id = "legacy-image",
                ContentType = "image",
                ImageData = "AAA=",
                ImageMimeType = "image/jpeg",
                TextContent = string.Empty
            },
            projectionIndex: 0);

        Assert.Equal("[image: image/jpeg]", viewModel.DisplayBodyText);
        Assert.True(viewModel.HasDisplayBody);
    }

    [Fact]
    public void ModeChangeTitleOnly_SurfacesTitleAsDisplayBody()
    {
        var viewModel = new ChatMessageViewModel();
        viewModel.ApplySnapshot(
            ChatMessageTemplateCoverage.CreateRepresentativeSnapshot("mode_change"),
            projectionIndex: 0);

        Assert.Equal("Mode Changed", viewModel.DisplayBodyText);
        Assert.True(viewModel.HasDisplayBody);
    }

    [Fact]
    public void ChatStyles_BindDirectionalBodyThroughDisplayBodyText()
    {
        // Architecture lock: directional templates must not re-bind raw TextContent for
        // body visibility, or title-only / media-fallback content types blank out again.
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "SalmonEgg",
            "SalmonEgg",
            "Styles",
            "ChatStyles.xaml"));

        Assert.Contains("DisplayBodyText", xaml, StringComparison.Ordinal);
        Assert.Contains("HasDisplayBody", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolCallTemplate=\"{StaticResource ToolCallMessageTemplate}\"", xaml, StringComparison.Ordinal);

        // Incoming/outgoing body slots must use DisplayBodyText, not raw TextContent.
        Assert.Contains("Text=\"{x:Bind DisplayBodyText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Visibility=\"{x:Bind HasTextContent, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("x:Load=\"{x:Bind HasTextContent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate SalmonEgg.sln from test base directory.");
    }
}
