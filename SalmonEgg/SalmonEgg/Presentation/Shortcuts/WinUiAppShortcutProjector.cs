using SalmonEgg.Presentation.Core.Services.Shortcuts;
using Windows.System;

namespace SalmonEgg.Presentation.Shortcuts;

internal static class WinUiAppShortcutProjector
{
    public static bool TryProject(
        AppShortcutGesture gesture,
        out VirtualKey key,
        out VirtualKeyModifiers modifiers)
    {
        modifiers = VirtualKeyModifiers.None;
        if (gesture.Modifiers.HasFlag(AppShortcutModifiers.Control))
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (gesture.Modifiers.HasFlag(AppShortcutModifiers.Alt))
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        if (gesture.Modifiers.HasFlag(AppShortcutModifiers.Shift))
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        return TryProjectKey(gesture.KeyToken, out key);
    }

    private static bool TryProjectKey(string keyToken, out VirtualKey key)
    {
        key = default;

        if (keyToken.Length == 1)
        {
            var ch = keyToken[0];
            if (ch is >= 'A' and <= 'Z')
            {
                key = (VirtualKey)ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                key = (VirtualKey)ch;
                return true;
            }
        }

        if (keyToken.StartsWith('F') &&
            int.TryParse(keyToken.AsSpan(1), out var functionKeyNumber) &&
            functionKeyNumber is >= 1 and <= 24 &&
            Enum.TryParse<VirtualKey>($"F{functionKeyNumber}", ignoreCase: true, out var functionKey))
        {
            key = functionKey;
            return true;
        }

        return false;
    }
}
