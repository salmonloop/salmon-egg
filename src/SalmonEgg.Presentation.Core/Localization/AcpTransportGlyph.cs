using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Localization;

/// <summary>
/// Single mapping from ACP transport types to their Segoe Fluent Icons glyph codepoints.
/// Glyphs are a Presentation concern (Symbol font codepoints), so they live here rather than
/// on the domain <c>ServerConfiguration</c>. UI converters and view models reuse this instead
/// of hard-coding glyphs.
/// </summary>
public static class AcpTransportGlyph
{
    // Segoe Fluent Icons codepoints.
    public const string StdioGlyph = "\uE756"; // CommandPrompt
    public const string StreamableHttpGlyph = "\uE774"; // Cloud
    public const string WebSocketGlyph = "\uE704"; // Globe

    public static string Resolve(TransportType transport)
        => transport switch
        {
            TransportType.Stdio => StdioGlyph,
            TransportType.StreamableHttp => StreamableHttpGlyph,
            _ => WebSocketGlyph
        };
}
