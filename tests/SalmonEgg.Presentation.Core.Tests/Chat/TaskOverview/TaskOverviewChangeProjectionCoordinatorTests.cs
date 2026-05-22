using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.TaskOverview;

public sealed class TaskOverviewChangeProjectionCoordinatorTests
{
    [Fact]
    public void Project_WhenNoDiffContent_ReturnsEmpty()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();

        var changes = coordinator.Project(Array.Empty<ConversationMessageSnapshot>());

        Assert.Empty(changes);
    }

    [Fact]
    public void Project_WhenDiffHasOldAndNewText_ReturnsModifiedSummary()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var path = AbsolutePath("src/App.cs");
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent(path, "one\nold\n", "one\nnew\nextra\n")
            }
        };

        var change = Assert.Single(coordinator.Project(new[] { message }));

        Assert.Equal(path, change.Path);
        Assert.Equal(TaskOverviewChangeKind.Modified, change.Kind);
        Assert.Equal("Modified", change.KindDisplayName);
        Assert.Equal("+3 / -2", change.LineSummary);
    }

    [Fact]
    public void Project_WhenTranscriptSnapshotContainsDiff_DoesNotRequireProjectedMessageViewModels()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var path = AbsolutePath("src/App.cs");
        var messages = new[]
        {
            new ConversationMessageSnapshot
            {
                ToolCallContent = new List<ToolCallContent>
                {
                    new DiffToolCallContent(path, "old\n", "new\nnext\n")
                }
            }
        };

        var change = Assert.Single(coordinator.Project(messages));

        Assert.Equal(path, change.Path);
        Assert.Equal(TaskOverviewChangeKind.Modified, change.Kind);
        Assert.Equal("+2 / -1", change.LineSummary);
    }

    [Fact]
    public void Project_WhenDiffHasOnlyNewText_ReturnsAdded()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent(AbsolutePath("src/New.cs"), null, "one\ntwo\n")
            }
        };

        var change = Assert.Single(coordinator.Project(new[] { message }));

        Assert.Equal(TaskOverviewChangeKind.Added, change.Kind);
        Assert.Equal("+2 / -0", change.LineSummary);
    }

    [Fact]
    public void Project_WhenNewTextIsEmptyString_TreatsDiffAsStandardEmptyContent()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent(AbsolutePath("src/Empty.cs"), "old\n", string.Empty)
            }
        };

        var change = Assert.Single(coordinator.Project(new[] { message }));

        Assert.Equal(TaskOverviewChangeKind.Modified, change.Kind);
        Assert.Equal("+0 / -1", change.LineSummary);
    }

    [Fact]
    public void Project_WhenNewTextMissing_DoesNotInventDeletionSemantics()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent(AbsolutePath("src/Old.cs"), "one\n", null)
            }
        };

        Assert.Empty(coordinator.Project(new[] { message }));
    }

    [Fact]
    public void Project_WhenPathMissing_DoesNotGuess()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent(null, "old", "new")
            }
        };

        Assert.Empty(coordinator.Project(new[] { message }));
    }

    [Fact]
    public void Project_WhenPathIsRelative_DoesNotProjectNonStandardDiff()
    {
        var coordinator = new TaskOverviewChangeProjectionCoordinator();
        var message = new ConversationMessageSnapshot
        {
            ToolCallContent = new List<ToolCallContent>
            {
                new DiffToolCallContent("src/Relative.cs", "old", "new")
            }
        };

        Assert.Empty(coordinator.Project(new[] { message }));
    }

    private static string AbsolutePath(string relativePath)
        => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "salmon-acp-task-overview-tests", relativePath));
}
