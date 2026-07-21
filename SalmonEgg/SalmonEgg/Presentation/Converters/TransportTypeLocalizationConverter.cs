using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Localization;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// Projects ACP transport types to localized CoreStrings labels for UI surfaces that still
/// bind domain models directly (for example Discover profile lists).
/// </summary>
public sealed class TransportTypeLocalizationConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader =
        ResourceLoader.GetForViewIndependentUse("CoreStrings");

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (!AcpTransportLocalization.TryResolveTransport(value, out var transport))
        {
            return string.Empty;
        }

        var key = AcpTransportLocalization.ResolveResourceKey(transport);
        var localized = ResourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(localized)
            ? AcpTransportLocalization.ResolveInvariantFallback(transport)
            : localized;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
