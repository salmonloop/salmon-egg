using System;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class GamepadShortcutProcessor
{
    private readonly HashSet<GamepadShortcutIntent> _pressedShortcuts = [];

    public IReadOnlyCollection<GamepadShortcutIntent> Process(GamepadInputReading reading)
    {
        var activeShortcuts = GamepadShortcutIntentProjector.GetActiveShortcuts(reading);
        if (activeShortcuts.Count == 0)
        {
            _pressedShortcuts.Clear();
            return [];
        }

        var raisedShortcuts = new List<GamepadShortcutIntent>();
        foreach (var shortcut in activeShortcuts)
        {
            if (_pressedShortcuts.Add(shortcut))
            {
                raisedShortcuts.Add(shortcut);
            }
        }

        RemoveReleasedShortcuts(activeShortcuts);
        return raisedShortcuts;
    }

    public void Reset()
    {
        _pressedShortcuts.Clear();
    }

    private void RemoveReleasedShortcuts(IReadOnlyCollection<GamepadShortcutIntent> activeShortcuts)
    {
        if (_pressedShortcuts.Count == activeShortcuts.Count)
        {
            return;
        }

        var releasedShortcuts = new List<GamepadShortcutIntent>();
        foreach (var pressedShortcut in _pressedShortcuts)
        {
            if (!activeShortcuts.Contains(pressedShortcut))
            {
                releasedShortcuts.Add(pressedShortcut);
            }
        }

        foreach (var releasedShortcut in releasedShortcuts)
        {
            _pressedShortcuts.Remove(releasedShortcut);
        }
    }
}
