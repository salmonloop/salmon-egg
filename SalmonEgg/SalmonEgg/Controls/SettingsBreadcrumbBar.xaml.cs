using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Controls;

public sealed partial class SettingsBreadcrumbBar : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<SettingsBreadcrumbItem>),
            typeof(SettingsBreadcrumbBar),
            new PropertyMetadata(null));

    private readonly MainNavigationViewModel _navigationViewModel;

    public SettingsBreadcrumbBar()
    {
        // Route through the navigation VM owner so settings activation failures
        // surface the same localized ShowInfo used by the nav shell entry.
        _navigationViewModel = App.ServiceProvider.GetRequiredService<MainNavigationViewModel>();
        InitializeComponent();
    }

    public IEnumerable<SettingsBreadcrumbItem>? ItemsSource
    {
        get => (IEnumerable<SettingsBreadcrumbItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not SettingsBreadcrumbItem item)
        {
            return;
        }

        if (item.IsCurrent || string.IsNullOrWhiteSpace(item.SettingsKey))
        {
            return;
        }

        _ = _navigationViewModel.ActivateSettingsAsync(item.SettingsKey);
    }
}
