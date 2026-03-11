using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Views.Navigation;

public sealed class MainNavItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StartTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? ProjectTemplate { get; set; }
    public DataTemplate? SessionTemplate { get; set; }
    public DataTemplate? MoreTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            StartNavItemViewModel => StartTemplate!,
            SessionsHeaderNavItemViewModel => HeaderTemplate!,
            ProjectNavItemViewModel => ProjectTemplate!,
            SessionNavItemViewModel => SessionTemplate!,
            MoreSessionsNavItemViewModel => MoreTemplate!,
            _ => StartTemplate!
        };
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}

