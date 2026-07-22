using System.Globalization;

namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Parses browser <c>Gamepad.id</c> facts into portable identity fields for diagnostics
/// and layout resolution. Does not invent brand-specific face semantics; face mapping
/// remains W3C standard-position based when mapping is "standard".
/// </summary>
public static class BrowserGamepadIdentityParser
{
    public static BrowserGamepadIdentity Parse(string? gamepadId)
    {
        if (string.IsNullOrWhiteSpace(gamepadId))
        {
            return BrowserGamepadIdentity.Empty;
        }

        var trimmed = gamepadId.Trim();
        if (TryParseFirefoxStyle(trimmed, out var firefoxIdentity))
        {
            return firefoxIdentity;
        }

        var openParen = trimmed.IndexOf('(', StringComparison.Ordinal);
        var displayName = openParen > 0
            ? trimmed[..openParen].Trim()
            : trimmed;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = trimmed;
        }

        return new BrowserGamepadIdentity(
            DisplayName: displayName,
            HardwareVendorId: TryExtractHexToken(trimmed, "Vendor:"),
            HardwareProductId: TryExtractHexToken(trimmed, "Product:"));
    }

    // Firefox commonly reports: "045e-0b13-Xbox Wireless Controller"
    private static bool TryParseFirefoxStyle(string value, out BrowserGamepadIdentity identity)
    {
        identity = default;
        var firstDash = value.IndexOf('-', StringComparison.Ordinal);
        if (firstDash != 4)
        {
            return false;
        }

        var secondDash = value.IndexOf('-', firstDash + 1);
        if (secondDash != 9)
        {
            return false;
        }

        if (!IsHexSpan(value.AsSpan(0, 4)) || !IsHexSpan(value.AsSpan(5, 4)))
        {
            return false;
        }

        var name = value[(secondDash + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!ushort.TryParse(value.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendor)
            || !ushort.TryParse(value.AsSpan(5, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var product))
        {
            return false;
        }

        identity = new BrowserGamepadIdentity(
            DisplayName: name,
            HardwareVendorId: vendor,
            HardwareProductId: product);
        return true;
    }

    private static ushort? TryExtractHexToken(string value, string token)
    {
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var cursor = index + token.Length;
        while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
        {
            cursor++;
        }

        // Accept optional 0x / 0X prefix used by some browser / OS id strings.
        if (cursor + 1 < value.Length
            && value[cursor] == '0'
            && (value[cursor + 1] == 'x' || value[cursor + 1] == 'X'))
        {
            cursor += 2;
        }

        var end = cursor;
        while (end < value.Length && IsHexDigit(value[end]))
        {
            end++;
        }

        if (end == cursor)
        {
            return null;
        }

        return ushort.TryParse(
            value.AsSpan(cursor, end - cursor),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;
    }

    private static bool IsHexSpan(ReadOnlySpan<char> value)
    {
        foreach (var ch in value)
        {
            if (!IsHexDigit(ch))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static bool IsHexDigit(char value)
        => (value >= '0' && value <= '9')
            || (value >= 'a' && value <= 'f')
            || (value >= 'A' && value <= 'F');
}

public readonly record struct BrowserGamepadIdentity(
    string DisplayName,
    ushort? HardwareVendorId,
    ushort? HardwareProductId)
{
    public static BrowserGamepadIdentity Empty { get; } = new(
        DisplayName: string.Empty,
        HardwareVendorId: null,
        HardwareProductId: null);
}
