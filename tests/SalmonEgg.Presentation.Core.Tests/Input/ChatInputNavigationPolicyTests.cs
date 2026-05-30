using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class ChatInputNavigationPolicyTests
{
    [Fact]
    public void Decide_ReturnsMoveSlashUp_WhenSlashVisible_AndFocusIsInput_AndIntentIsMoveUp()
    {
        Assert.Equal(
            ChatInputNavigationAction.MoveSlashUp,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveUp,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsMoveSlashDown_WhenSlashVisible_AndFocusIsInput_AndIntentIsMoveDown()
    {
        Assert.Equal(
            ChatInputNavigationAction.MoveSlashDown,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveDown,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsAcceptSlashCommand_WhenSlashVisible_AndFocusIsInput_AndIntentIsActivate()
    {
        Assert.Equal(
            ChatInputNavigationAction.AcceptSlashCommand,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.Activate,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenSlashHidden_AndIntentIsBack()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.Back,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: false,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsEscapeMoveUp_WhenSlashHidden_AndFocusIsInput()
    {
        Assert.Equal(
            ChatInputNavigationAction.EscapeMoveUp,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveUp,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: false,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsMoveToFirstSelector_WhenSlashHidden_AndFocusIsInput_AndIntentIsMoveDown()
    {
        Assert.Equal(
            ChatInputNavigationAction.MoveToFirstSelector,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveDown,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: false,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenInputIsDisabled()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveDown,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: false,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenImeCompositionIsActive()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveDown,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: true));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenFocusIsOnModeSelector_AndIntentIsMoveDown()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveDown,
                ChatInputFocusContext.ModeSelector,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenFocusIsOnModeSelector_AndIntentIsMoveUp()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.MoveUp,
                ChatInputFocusContext.ModeSelector,
                slashCommandsVisible: false,
                inputEnabled: true,
                isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_ForBackIntent()
    {
        Assert.Equal(
            ChatInputNavigationAction.None,
            ChatInputNavigationPolicy.Decide(
                GamepadNavigationIntent.Back,
                ChatInputFocusContext.InputBox,
                slashCommandsVisible: true,
                inputEnabled: true,
                isImeComposing: false));
    }
}
