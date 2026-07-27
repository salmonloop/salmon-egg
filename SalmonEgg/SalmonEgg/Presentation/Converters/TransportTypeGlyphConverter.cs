using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Localization;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// Projects ACP transport types to their Segoe Fluent Icons glyph for UI surfaces that still
/// bind domain models directly (for example Discover profile lists). Glyphs are a Presentation
/// concern, so they live here instead of on the domain <see cref="ServerConfiguration"/>.
/// </summary>
public sealed class TransportTypeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => AcpTransportLocalization.TryResolveTransport(value, out var transport)
            ? AcpTransportGlyph.Resolve(transport)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
