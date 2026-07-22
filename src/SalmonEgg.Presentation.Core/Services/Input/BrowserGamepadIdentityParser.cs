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
