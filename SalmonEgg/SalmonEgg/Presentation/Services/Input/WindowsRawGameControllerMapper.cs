#if WINDOWS
using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsRawGameControllerMapper
{
    private const double CenteredAxisValue = 0.5;
    private const double AxisDeadzone = 0.18;

    public HashSet<GamepadNavigationIntent> GetActiveIntents(RawGameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var buttons = new bool[controller.ButtonCount];
        var switches = new GameControllerSwitchPosition[controller.SwitchCount];
        var axes = new double[controller.AxisCount];
        controller.GetCurrentReading(buttons, switches, axes);

        var intents = new HashSet<GamepadNavigationIntent>();

        for (var i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i])
            {
                continue;
            }

            MapButtonLabel(controller.GetButtonLabel(i), intents);
        }

        for (var i = 0; i < switches.Length; i++)
        {
            var position = switches[i];
            if ((position & GameControllerSwitchPosition.Up) == GameControllerSwitchPosition.Up)
            {
                intents.Add(GamepadNavigationIntent.MoveUp);
            }

            if ((position & GameControllerSwitchPosition.Down) == GameControllerSwitchPosition.Down)
            {
                intents.Add(GamepadNavigationIntent.MoveDown);
            }

            if ((position & GameControllerSwitchPosition.Left) == GameControllerSwitchPosition.Left)
            {
                intents.Add(GamepadNavigationIntent.MoveLeft);
            }

            if ((position & GameControllerSwitchPosition.Right) == GameControllerSwitchPosition.Right)
            {
                intents.Add(GamepadNavigationIntent.MoveRight);
            }
        }

        if (axes.Length >= 2)
        {
            MapAxisPair(axes[0], axes[1], intents);
        }

        return intents;
    }

    private static void MapButtonLabel(GameControllerButtonLabel label, ISet<GamepadNavigationIntent> intents)
    {
        switch (label)
        {
            case GameControllerButtonLabel.XboxUp:
            case GameControllerButtonLabel.Up:
                intents.Add(GamepadNavigationIntent.MoveUp);
                break;
            case GameControllerButtonLabel.XboxDown:
            case GameControllerButtonLabel.Down:
                intents.Add(GamepadNavigationIntent.MoveDown);
                break;
            case GameControllerButtonLabel.XboxLeft:
            case GameControllerButtonLabel.Left:
                intents.Add(GamepadNavigationIntent.MoveLeft);
                break;
            case GameControllerButtonLabel.XboxRight:
            case GameControllerButtonLabel.Right:
                intents.Add(GamepadNavigationIntent.MoveRight);
                break;
            case GameControllerButtonLabel.XboxA:
            case GameControllerButtonLabel.Cross:
            case GameControllerButtonLabel.LetterA:
                intents.Add(GamepadNavigationIntent.Activate);
                break;
            case GameControllerButtonLabel.XboxB:
            case GameControllerButtonLabel.Circle:
            case GameControllerButtonLabel.LetterB:
            case GameControllerButtonLabel.Back:
                intents.Add(GamepadNavigationIntent.Back);
                break;
        }
    }

    private static void MapAxisPair(double x, double y, ISet<GamepadNavigationIntent> intents)
    {
        var offsetX = x - CenteredAxisValue;
        var offsetY = y - CenteredAxisValue;
        if (Math.Abs(offsetX) < AxisDeadzone && Math.Abs(offsetY) < AxisDeadzone)
        {
            return;
        }

        if (Math.Abs(offsetX) > Math.Abs(offsetY))
        {
            intents.Add(offsetX > 0
                ? GamepadNavigationIntent.MoveRight
                : GamepadNavigationIntent.MoveLeft);
        }
        else
        {
            intents.Add(offsetY < 0
                ? GamepadNavigationIntent.MoveUp
                : GamepadNavigationIntent.MoveDown);
        }
    }
}
#endif
