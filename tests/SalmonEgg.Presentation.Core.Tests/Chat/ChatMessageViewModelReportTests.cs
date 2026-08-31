using System.Threading.Tasks;
using SalmonEgg.Acp.Content;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatMessageViewModelReportTests
{
    [Fact]
    public async Task ReportContentCommand_ForIncomingMessage_ForwardsConfiguredHandler()
    {
        ChatMessageViewModel? reported = null;
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-report",
            new TextContentBlock("report me"),
            isOutgoing: false);
        vm.ConfigureShellActions(
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            message =>
            {
                reported = message;
                return Task.CompletedTask;
            });

        Assert.True(vm.ReportContentCommand.CanExecute(null));
        await vm.ReportContentCommand.ExecuteAsync(null);

        Assert.Same(vm, reported);
    }

    [Fact]
    public async Task ReportContentCommand_ForOutgoingMessage_IsDisabled()
    {
        var reportCount = 0;
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-report-out",
            new TextContentBlock("user text"),
            isOutgoing: true);
        vm.ConfigureShellActions(
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ =>
            {
                reportCount++;
                return Task.CompletedTask;
            });

        Assert.False(vm.ReportContentCommand.CanExecute(null));
        await vm.ReportContentCommand.ExecuteAsync(null);
        Assert.Equal(0, reportCount);
    }

    [Fact]
    public async Task ReportContentCommand_WithoutHandler_IsDisabled()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-report-unconfigured",
            new TextContentBlock("report me"),
            isOutgoing: false);

        Assert.False(vm.ReportContentCommand.CanExecute(null));
        await vm.ReportContentCommand.ExecuteAsync(null);
    }
}
