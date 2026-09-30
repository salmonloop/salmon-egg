using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

/// <summary>
/// The agent's programmatic tool name (ACP tool-call <c>name</c>) has to survive the whole projection
/// chain — protocol snapshot, then display view model — or the label the pill renders never lights up.
/// </summary>
public sealed class ChatMessageViewModelToolCallNameTests
{
    [Fact]
    public void ReportedToolName_ReachesTheDisplayViewModel()
    {
        var vm = new ChatMessageViewModel();

        vm.ApplySnapshot(
            new ConversationMessageSnapshot
            {
                Id = "tool-name",
                ContentType = "tool_call",
                Title = "Reading config",
                ToolCallId = "call-1",
                ToolCallName = "read_file"
            },
            projectionIndex: 0);

        Assert.Equal("read_file", vm.ToolCallName);
        Assert.True(vm.ShouldShowToolCallPill);
    }

    [Fact]
    public void UnreportedToolName_StaysUnset()
    {
        var vm = new ChatMessageViewModel();

        vm.ApplySnapshot(
            new ConversationMessageSnapshot
            {
                Id = "tool-no-name",
                ContentType = "tool_call",
                Title = "Reading config",
                ToolCallId = "call-2"
            },
            projectionIndex: 0);

        Assert.Null(vm.ToolCallName);
    }
}