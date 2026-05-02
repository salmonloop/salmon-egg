using System.Threading.Tasks;
using System.Text.Json;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Panels;

public sealed class ChatTerminalProjectionCoordinatorTests
{
    [Fact]
    public void TryApplyRequest_WithSyntheticPayload_SelectsTerminalAndProjectsOutput()
    {
        var panelCoordinator = new ChatConversationPanelStateCoordinator();
        var sut = new ChatTerminalProjectionCoordinator();
        using var payload = JsonDocument.Parse("""{"terminalId":"term-1","output":"hello","truncated":true,"exitStatus":{"exitCode":7}}""");

        var applied = sut.TryApplyRequest(
            panelCoordinator,
            "conv-1",
            new TerminalRequestEventArgs("msg-1", "remote-1", "term-1", "terminal/create", payload.RootElement, _ => Task.FromResult(true)),
            isCurrentConversation: true,
            out var selection);

        Assert.True(applied);
        Assert.Single(selection.TerminalSessions);
        Assert.Equal("term-1", selection.SelectedTerminal?.TerminalId);
        Assert.Equal("hello", selection.SelectedTerminal?.Output);
        Assert.True(selection.SelectedTerminal?.IsTruncated);
        Assert.Equal(7, selection.SelectedTerminal?.ExitCode);
    }

    [Fact]
    public void TryApplyState_WithRelease_ProjectsLifecycle()
    {
        var panelCoordinator = new ChatConversationPanelStateCoordinator();
        var sut = new ChatTerminalProjectionCoordinator();

        var applied = sut.TryApplyState(
            panelCoordinator,
            "conv-1",
            new TerminalStateChangedEventArgs(
                "remote-1",
                "term-2",
                "terminal/release",
                output: "done",
                truncated: false,
                exitStatus: new TerminalExitStatus { ExitCode = 0 },
                isReleased: true),
            isCurrentConversation: true,
            out var selection);

        Assert.True(applied);
        Assert.Equal("term-2", selection.SelectedTerminal?.TerminalId);
        Assert.Equal("done", selection.SelectedTerminal?.Output);
        Assert.True(selection.SelectedTerminal?.IsReleased);
        Assert.Equal(0, selection.SelectedTerminal?.ExitCode);
    }
}
