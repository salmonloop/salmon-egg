using System;
using System.Reflection;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class ChatInputNavigationPolicyTests
{
    [Fact]
    public void Decide_ReturnsMoveSlashUp_WhenSlashVisible_AndFocusIsInput_AndIntentIsMoveUp()
    {
        Assert.Equal("MoveSlashUp", Decide(GamepadNavigationIntent.MoveUp, "InputBox", slashCommandsVisible: true, inputEnabled: true, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsMoveSlashDown_WhenSlashVisible_AndFocusIsInput_AndIntentIsMoveDown()
    {
        Assert.Equal("MoveSlashDown", Decide(GamepadNavigationIntent.MoveDown, "InputBox", slashCommandsVisible: true, inputEnabled: true, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsAcceptSlashCommand_WhenSlashVisible_AndFocusIsInput_AndIntentIsActivate()
    {
        Assert.Equal("AcceptSlashCommand", Decide(GamepadNavigationIntent.Activate, "InputBox", slashCommandsVisible: true, inputEnabled: true, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenSlashHidden()
    {
        Assert.Equal("None", Decide(GamepadNavigationIntent.MoveDown, "InputBox", slashCommandsVisible: false, inputEnabled: true, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenInputIsDisabled()
    {
        Assert.Equal("None", Decide(GamepadNavigationIntent.MoveDown, "InputBox", slashCommandsVisible: true, inputEnabled: false, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenImeCompositionIsActive()
    {
        Assert.Equal("None", Decide(GamepadNavigationIntent.MoveDown, "InputBox", slashCommandsVisible: true, inputEnabled: true, isImeComposing: true));
    }

    [Fact]
    public void Decide_ReturnsNone_WhenFocusIsOnModeSelector_AndIntentIsMoveDown()
    {
        Assert.Equal("None", Decide(GamepadNavigationIntent.MoveDown, "ModeSelector", slashCommandsVisible: true, inputEnabled: true, isImeComposing: false));
    }

    [Fact]
    public void Decide_ReturnsNone_ForBackIntent()
    {
        Assert.Equal("None", Decide(GamepadNavigationIntent.Back, "InputBox", slashCommandsVisible: true, inputEnabled: true, isImeComposing: false));
    }

    private static string Decide(GamepadNavigationIntent intent, string focusContextName, bool slashCommandsVisible, bool inputEnabled, bool isImeComposing)
    {
        var assembly = typeof(GamepadNavigationIntent).Assembly;
        var policyType = assembly.GetType("SalmonEgg.Presentation.Core.Services.Input.ChatInputNavigationPolicy");
        Assert.NotNull(policyType);

        var focusContextType = assembly.GetType("SalmonEgg.Presentation.Core.Services.Input.ChatInputFocusContext");
        Assert.NotNull(focusContextType);

        var focusContext = Enum.Parse(focusContextType!, focusContextName);
        var decideMethod = policyType!.GetMethod(
            "Decide",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(GamepadNavigationIntent), focusContextType!, typeof(bool), typeof(bool), typeof(bool) },
            modifiers: null);

        Assert.NotNull(decideMethod);
        var result = decideMethod!.Invoke(null, new object[] { intent, focusContext, slashCommandsVisible, inputEnabled, isImeComposing });
        Assert.NotNull(result);
        return result!.ToString()!;
    }
}
