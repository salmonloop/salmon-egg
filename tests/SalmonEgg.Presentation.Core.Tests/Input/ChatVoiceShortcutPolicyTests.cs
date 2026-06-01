using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class ChatVoiceShortcutPolicyTests
{
    [Fact]
    public void Decide_ReturnsStartVoiceInput_WhenToggleVoiceInputTargetsFocusedInputBox()
    {
        var action = ChatVoiceShortcutPolicy.Decide(
            GamepadShortcutIntent.ToggleVoiceInput,
            ChatVoiceShortcutFocusContext.InputBox,
            canStartVoiceInput: true,
            canStopVoiceInput: false,
            isVoiceInputListening: false,
            isImeComposing: false);

        Assert.Equal(ChatVoiceShortcutAction.StartVoiceInput, action);
    }

    [Fact]
    public void Decide_ReturnsStopVoiceInput_WhenListeningAndInputBoxIsFocused()
    {
        var action = ChatVoiceShortcutPolicy.Decide(
            GamepadShortcutIntent.ToggleVoiceInput,
            ChatVoiceShortcutFocusContext.InputBox,
            canStartVoiceInput: false,
            canStopVoiceInput: true,
            isVoiceInputListening: true,
            isImeComposing: false);

        Assert.Equal(ChatVoiceShortcutAction.StopVoiceInput, action);
    }

    [Fact]
    public void Decide_ReturnsNone_WhenFocusIsOutsideInputSurface()
    {
        var action = ChatVoiceShortcutPolicy.Decide(
            GamepadShortcutIntent.ToggleVoiceInput,
            ChatVoiceShortcutFocusContext.Other,
            canStartVoiceInput: true,
            canStopVoiceInput: false,
            isVoiceInputListening: false,
            isImeComposing: false);

        Assert.Equal(ChatVoiceShortcutAction.None, action);
    }

    [Fact]
    public void Decide_ReturnsNone_WhenImeCompositionIsActive()
    {
        var action = ChatVoiceShortcutPolicy.Decide(
            GamepadShortcutIntent.ToggleVoiceInput,
            ChatVoiceShortcutFocusContext.InputBox,
            canStartVoiceInput: true,
            canStopVoiceInput: false,
            isVoiceInputListening: false,
            isImeComposing: true);

        Assert.Equal(ChatVoiceShortcutAction.None, action);
    }
}
