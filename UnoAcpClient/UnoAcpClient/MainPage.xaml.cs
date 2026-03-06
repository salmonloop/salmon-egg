using Microsoft.Extensions.DependencyInjection;
using UnoAcpClient.Presentation.ViewModels;
using UnoAcpClient.Presentation.Views;

namespace UnoAcpClient;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        this.InitializeComponent();
        ViewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
        DataContext = ViewModel;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // 导航到设置页面
        var frame = new Microsoft.UI.Xaml.Controls.Frame();
        frame.Navigate(typeof(SettingsPage));
        var window = new Microsoft.UI.Xaml.Window();
        window.Content = frame;
        window.Activate();
    }
}
