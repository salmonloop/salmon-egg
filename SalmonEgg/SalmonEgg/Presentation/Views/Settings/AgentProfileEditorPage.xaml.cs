using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AgentProfileEditorPage : SettingsPageBase
{
    public ConfigurationEditorViewModel ViewModel { get; }

    public static readonly DependencyProperty PageTitleProperty =
        DependencyProperty.Register(
            nameof(PageTitle),
            typeof(string),
            typeof(AgentProfileEditorPage),
            new PropertyMetadata(string.Empty));

    public string PageTitle
    {
        get => (string)GetValue(PageTitleProperty);
        private set => SetValue(PageTitleProperty, value);
    }

    private readonly IConfigurationService _configurationService;
    private readonly AcpProfilesViewModel _profiles;

    public AgentProfileEditorPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ConfigurationEditorViewModel>();
        _configurationService = App.ServiceProvider.GetRequiredService<IConfigurationService>();
        _profiles = App.ServiceProvider.GetRequiredService<AcpProfilesViewModel>();

        InitializeComponent();
        PageTitle = ResolveResourceString("AgentProfileEditorPageTitleNew", "新建");
        UpdateBreadcrumb();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.NavigationMode == NavigationMode.Back)
        {
            // Returning to this page should not keep any old input.
            ViewModel.LoadBlankConfiguration();
            PageTitle = ResolveResourceString("AgentProfileEditorPageTitleNew", "新建");
            UpdateBreadcrumb();
            return;
        }

        if (e.Parameter is AgentProfileEditorArgs args && args.IsEditing && !string.IsNullOrWhiteSpace(args.ProfileId))
        {
            PageTitle = ResolveResourceString("AgentProfileEditorPageTitleEdit", "编辑");
            var config = await _configurationService.LoadConfigurationAsync(args.ProfileId);
            if (config != null)
            {
                ViewModel.LoadConfiguration(config);
            }
            else
            {
                ViewModel.LoadBlankConfiguration();
            }

            UpdateBreadcrumb();
            return;
        }

        PageTitle = ResolveResourceString("AgentProfileEditorPageTitleNew", "新建");
        ViewModel.LoadBlankConfiguration();
        UpdateBreadcrumb();
    }

    private void UpdateBreadcrumb()
    {
        SetBreadcrumb(
            SettingsBreadcrumbItem.Link(ResolveResourceString("SettingsBreadcrumbRoot", "设置"), "General"),
            SettingsBreadcrumbItem.Link(ResolveResourceString("SettingsNav_AgentAcp.Content", "Agent (ACP)"), "AgentAcp"),
            SettingsBreadcrumbItem.Current(PageTitle));
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }
        else
        {
            Frame?.Navigate(typeof(AcpConnectionSettingsPage));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        OnBackClick(sender, e);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveConfigurationAsync();
        if (ViewModel.HasError)
        {
            return;
        }

        await _profiles.RefreshAsync();
        _profiles.SelectedProfile = _profiles.Profiles.FirstOrDefault(p => p.Id == ViewModel.Configuration.Id);

        OnBackClick(sender, e);
    }
}
