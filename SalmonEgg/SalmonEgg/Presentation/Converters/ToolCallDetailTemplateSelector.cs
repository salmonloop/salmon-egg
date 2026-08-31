using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// Selects a detail rendering template by <see cref="ToolCallDetailKind"/> so the
/// expanded pill renders each ACP content type (text / diff / terminal / location)
/// with its native affordance instead of a flat label-value list.
/// </summary>
public sealed class ToolCallDetailTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? LocationTemplate { get; set; }

    public DataTemplate? DiffTemplate { get; set; }

    public DataTemplate? TerminalTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is ToolCallDetailItem detail)
        {
            return detail.Kind switch
            {
                ToolCallDetailKind.Diff => DiffTemplate,
                ToolCallDetailKind.Terminal => TerminalTemplate,
                ToolCallDetailKind.Location => LocationTemplate,
                _ => TextTemplate
            };
        }

        return base.SelectTemplateCore(item, container);
    }
}
