using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class BottomPanelHost : UserControl
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public BottomPanelHost()
    {
        this.InitializeComponent();
    }

    public string TerminalTabTitle => GetResourceString("BottomPanelTerminalTab.Text");

    public ILocalTerminalSession? EffectiveLocalTerminalSession => LocalTerminalSession?.Session;

    public string LocalTerminalContentText => LocalTerminalSession?.OutputText ?? string.Empty;

    public LocalTerminalPanelSessionViewModel? LocalTerminalSession
    {
        get => (LocalTerminalPanelSessionViewModel?)GetValue(LocalTerminalSessionProperty);
        set => SetValue(LocalTerminalSessionProperty, value);
    }

    public static readonly DependencyProperty LocalTerminalSessionProperty =
        DependencyProperty.Register(
            nameof(LocalTerminalSession),
            typeof(LocalTerminalPanelSessionViewModel),
            typeof(BottomPanelHost),
            new PropertyMetadata(null, OnLocalTerminalSessionChanged));

    private static void OnLocalTerminalSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BottomPanelHost host)
        {
            host.Bindings.Update();
        }
    }

    private static string GetResourceString(string resourceKey)
    {
        var value = ResourceLoader.GetString(resourceKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = ResourceLoader.GetString(resourceKey.Replace('.', '/'));
        }

        return string.IsNullOrWhiteSpace(value) ? resourceKey : value;
    }
}
