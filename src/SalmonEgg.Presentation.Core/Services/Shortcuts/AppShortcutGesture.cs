using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.Services.Shortcuts;

[Flags]
public enum AppShortcutModifiers
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
}

public readonly record struct AppShortcutGesture(AppShortcutModifiers Modifiers, string KeyToken)
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift"];

    public static bool TryParse(string? input, out AppShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var modifiers = AppShortcutModifiers.None;
        var seenModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var token = parts[i];
            if (!seenModifiers.Add(token))
            {
                return false;
            }

            if (string.Equals(token, "Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= AppShortcutModifiers.Control;
                continue;
            }

            if (string.Equals(token, "Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= AppShortcutModifiers.Alt;
                continue;
            }

            if (string.Equals(token, "Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= AppShortcutModifiers.Shift;
                continue;
            }

            return false;
        }

        if (modifiers == AppShortcutModifiers.None)
        {
            return false;
        }

        var keyToken = NormalizeKeyToken(parts[^1]);
        if (!IsSupportedKeyToken(keyToken))
        {
            return false;
        }

        gesture = new AppShortcutGesture(modifiers, keyToken);
        return true;
    }

    public override string ToString()
    {
        var tokens = new List<string>(4);
        foreach (var modifier in ModifierOrder)
        {
            if (modifier == "Ctrl" && Modifiers.HasFlag(AppShortcutModifiers.Control))
            {
                tokens.Add(modifier);
            }
            else if (modifier == "Alt" && Modifiers.HasFlag(AppShortcutModifiers.Alt))
            {
                tokens.Add(modifier);
            }
            else if (modifier == "Shift" && Modifiers.HasFlag(AppShortcutModifiers.Shift))
            {
                tokens.Add(modifier);
            }
        }

        tokens.Add(KeyToken);
        return string.Join("+", tokens);
    }

    private static string NormalizeKeyToken(string rawToken)
    {
        var token = rawToken.Trim();
        return token.Length switch
        {
            1 => token.ToUpperInvariant(),
            _ => token.ToUpperInvariant()
        };
    }

    private static bool IsSupportedKeyToken(string keyToken)
    {
        if (keyToken.Length == 1)
        {
            return char.IsLetterOrDigit(keyToken[0]);
        }

        if (keyToken.StartsWith('F') &&
            int.TryParse(keyToken.AsSpan(1), out var functionKeyNumber))
        {
            return functionKeyNumber is >= 1 and <= 24;
        }

        return false;
    }
}
