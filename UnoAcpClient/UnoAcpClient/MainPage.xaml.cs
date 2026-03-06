using Microsoft.Extensions.DependencyInjection;
using UnoAcpClient.Presentation.ViewModels;

namespace UnoAcpClient;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        this.InitializeComponent();

        // 从 DI 容器获取 MainViewModel
        ViewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
        DataContext = ViewModel;

        // 加载服务器列表
        _ = ViewModel.LoadServersAsync();
    }
}
