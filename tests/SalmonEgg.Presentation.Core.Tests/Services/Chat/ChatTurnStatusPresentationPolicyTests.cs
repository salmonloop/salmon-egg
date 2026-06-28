using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class ChatTurnStatusPresentationPolicyTests
{
    [Theory]
    [InlineData(ChatTurnPhase.CreatingRemoteSession, ChatTurnStatusSource.ClientLifecycle, true, true, false, "ChatTurnStatus_CreatingRemoteSession")]
    [InlineData(ChatTurnPhase.DispatchingPrompt, ChatTurnStatusSource.ClientLifecycle, true, true, false, "ChatTurnStatus_DispatchingPrompt")]
    [InlineData(ChatTurnPhase.WaitingForAgent, ChatTurnStatusSource.ClientLifecycle, true, false, false, "ChatTurnStatus_WaitingForAgent")]
    [InlineData(ChatTurnPhase.Thinking, ChatTurnStatusSource.AcpSessionUpdate, true, false, false, "ChatTurnStatus_Thinking")]
    [InlineData(ChatTurnPhase.ToolPending, ChatTurnStatusSource.AcpSessionUpdate, true, false, false, "ChatTurnStatus_ToolPending")]
    [InlineData(ChatTurnPhase.ToolRunning, ChatTurnStatusSource.AcpSessionUpdate, true, false, false, "ChatTurnStatus_ToolRunning")]
    [InlineData(ChatTurnPhase.Responding, ChatTurnStatusSource.AcpSessionUpdate, true, false, false, "ChatTurnStatus_Responding")]
    [InlineData(ChatTurnPhase.Failed, ChatTurnStatusSource.PromptResult, false, false, true, "ChatTurnStatus_Failed")]
    [InlineData(ChatTurnPhase.Cancelled, ChatTurnStatusSource.PromptResult, false, false, false, "ChatTurnStatus_Cancelled")]
    public void Resolve_ClassifiesStatusByAuthoritativeSource(
        ChatTurnPhase phase,
        ChatTurnStatusSource expectedSource,
        bool expectedRunning,
        bool expectedSubmitInFlight,
        bool expectedFaulted,
        string expectedResourceKey)
    {
        var turn = new ActiveTurnState(
            "conv-1",
            "turn-1",
            phase,
            DateTime.UtcNow,
            DateTime.UtcNow,
            ToolTitle: "read_file");

        var presentation = ChatTurnStatusPresentationPolicy.Resolve(turn);

        Assert.True(presentation.IsVisible);
        Assert.Equal(expectedSource, presentation.Source);
        Assert.Equal(expectedResourceKey, presentation.ResourceKey);
        Assert.Equal(expectedRunning, presentation.IsRunning);
        Assert.Equal(expectedSubmitInFlight, presentation.IsPromptSubmitInFlight);
        Assert.Equal(expectedFaulted, presentation.IsFaulted);
    }

    [Fact]
    public void Resolve_HidesCompletedTurnStatus()
    {
        var turn = new ActiveTurnState(
            "conv-1",
            "turn-1",
            ChatTurnPhase.Completed,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var presentation = ChatTurnStatusPresentationPolicy.Resolve(turn);

        Assert.False(presentation.IsVisible);
        Assert.Equal(ChatTurnStatusSource.None, presentation.Source);
        Assert.False(presentation.IsRunning);
        Assert.False(presentation.IsFaulted);
    }
}
