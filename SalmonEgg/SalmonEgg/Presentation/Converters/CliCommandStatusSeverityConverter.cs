using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Presentation.Models.Cli;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// Maps the presentation-layer severity onto the native InfoBar's own.
/// </summary>
/// <remarks>
/// Presentation.Core cannot reference WinUI types, so it reports severity as its own enum and the mapping
/// lands here. One-way only: an InfoBar's severity is never a source of user intent.
/// </remarks>
public sealed partial class CliCommandStatusSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CliCommandStatusSeverity severity
            ? severity switch
            {
                CliCommandStatusSeverity.Success => InfoBarSeverity.Success,
                CliCommandStatusSeverity.Warning => InfoBarSeverity.Warning,
                CliCommandStatusSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            }
            : InfoBarSeverity.Informational;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("InfoBar severity is presentation output, never an input.");
}
