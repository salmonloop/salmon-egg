using System;
using System.Linq;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ToolCallDetailProjectorTests
{
    [Fact]
    public void Project_WithNoContentOrLocations_ReturnsEmpty()
    {
        Assert.Empty(ToolCallDetailProjector.Project(null, null));
    }

    [Fact]
    public void Project_TextContent_ProducesTextItemWithTextDisplay()
    {
        var items = ToolCallDetailProjector.Project(
            new[] { new ContentToolCallContent(new TextContentBlock("Analysis complete")) },
            null);

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Text, item.Kind);
        Assert.Equal("Analysis complete", item.Text);
        Assert.Equal("Analysis complete", item.DisplayText);
    }

    [Fact]
    public void Project_ResourceLink_ProducesLocationItemWithUriAsPath()
    {
        var items = ToolCallDetailProjector.Project(
            new[] { new ContentToolCallContent(new ResourceLinkContentBlock("https://example.com/doc")) },
            null);

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Location, item.Kind);
        Assert.Equal("https://example.com/doc", item.Path);
        Assert.True(item.HasPath);
    }

    [Fact]
    public void Project_DiffContent_ProducesSingleDiffItemCarryingOldAndNewText()
    {
        var items = ToolCallDetailProjector.Project(
            new[] { new DiffToolCallContent("/repo/a.cs", "old", "new") },
            null);

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Diff, item.Kind);
        Assert.Equal("/repo/a.cs", item.Path);
        Assert.Equal("old", item.DiffOldText);
        Assert.Equal("new", item.DiffNewText);
        Assert.True(item.HasPath);
        Assert.True(item.HasDiffNewText);
        Assert.Equal("/repo/a.cs", item.DisplayText);
    }

    [Fact]
    public void Project_DiffContent_WithEmptyOldText_StillProjectsNewText()
    {
        var items = ToolCallDetailProjector.Project(
            new[] { new DiffToolCallContent("/repo/new.cs", null, "new file body") },
            null);

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Diff, item.Kind);
        Assert.Null(item.DiffOldText);
        Assert.Equal("new file body", item.DiffNewText);
        Assert.True(item.HasDiffNewText);
    }

    [Fact]
    public void Project_TerminalContent_ProducesTerminalItemWithId()
    {
        var items = ToolCallDetailProjector.Project(
            new[] { new TerminalToolCallContent("term_abc") },
            null);

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Terminal, item.Kind);
        Assert.Equal("term_abc", item.TerminalId);
        Assert.Equal("term_abc", item.DisplayText);
    }

    [Fact]
    public void Project_Location_ProducesLocationItemWithPathAndLine()
    {
        var items = ToolCallDetailProjector.Project(
            null,
            new[] { new ToolCallLocation("/repo/a.cs", 42) });

        var item = Assert.Single(items);
        Assert.Equal(ToolCallDetailKind.Location, item.Kind);
        Assert.Equal("/repo/a.cs", item.Path);
        Assert.Equal(42u, item.Line);
        Assert.Equal("/repo/a.cs:42", item.DisplayText);
    }

    [Fact]
    public void Project_LocationWithoutLine_DisplaysPathOnly()
    {
        var items = ToolCallDetailProjector.Project(
            null,
            new[] { new ToolCallLocation("/repo/a.cs", null) });

        var item = Assert.Single(items);
        Assert.Equal("/repo/a.cs", item.DisplayText);
    }

    [Fact]
    public void Project_CombinesContentAndLocationsInOrder()
    {
        var items = ToolCallDetailProjector.Project(
            new ToolCallContent[]
            {
                new ContentToolCallContent(new TextContentBlock("first")),
                new TerminalToolCallContent("term_x")
            },
            new[] { new ToolCallLocation("/repo/loc.cs", 7) });

        Assert.Equal(3, items.Count);
        Assert.Equal(ToolCallDetailKind.Text, items[0].Kind);
        Assert.Equal(ToolCallDetailKind.Terminal, items[1].Kind);
        Assert.Equal(ToolCallDetailKind.Location, items[2].Kind);
    }

    [Fact]
    public void Project_IgnoresBlankLocationPaths()
    {
        var items = ToolCallDetailProjector.Project(
            null,
            new[] { new ToolCallLocation("   ", 1), new ToolCallLocation("/repo/real.cs", 2) });

        var item = Assert.Single(items);
        Assert.Equal("/repo/real.cs", item.Path);
    }

    [Fact]
    public void ProjectSummary_FallsBackToDiffPathWhenInputEmpty()
    {
        var summary = ToolCallDetailProjector.ProjectSummary(
            ToolCallKind.Edit,
            null,
            new[] { new DiffToolCallContent("/repo/edited.cs", "old", "new") },
            null);

        Assert.Equal("/repo/edited.cs", summary);
    }

    [Fact]
    public void ProjectSummary_ReturnsEmptyWhenNothingToSummarize()
    {
        Assert.Equal(string.Empty, ToolCallDetailProjector.ProjectSummary(null, null, null, null));
    }
}
