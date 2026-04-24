using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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

    public string SelectedTabTitle => GetResourceString(SelectedTab?.TitleResourceKey);

    public bool IsTerminalTabSelected =>
        string.Equals(SelectedTab?.Id, "terminal", StringComparison.Ordinal);

    public ILocalTerminalSession? EffectiveLocalTerminalSession => LocalTerminalSession?.Session;

    public string LocalTerminalContentText => LocalTerminalSession?.OutputText ?? string.Empty;

    public ObservableCollection<BottomPanelTabViewModel>? TabsSource
    {
        get => (ObservableCollection<BottomPanelTabViewModel>?)GetValue(TabsSourceProperty);
        set => SetValue(TabsSourceProperty, value);
    }

    public static readonly DependencyProperty TabsSourceProperty =
        DependencyProperty.Register(
            nameof(TabsSource),
            typeof(ObservableCollection<BottomPanelTabViewModel>),
            typeof(BottomPanelHost),
            new PropertyMetadata(null, OnTabsSourceChanged));

    public BottomPanelTabViewModel? SelectedTab
    {
        get => (BottomPanelTabViewModel?)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public static readonly DependencyProperty SelectedTabProperty =
        DependencyProperty.Register(
            nameof(SelectedTab),
            typeof(BottomPanelTabViewModel),
            typeof(BottomPanelHost),
            new PropertyMetadata(null, OnSelectedTabChanged));

    public ObservableCollection<TerminalPanelSessionViewModel>? TerminalSessions
    {
        get => (ObservableCollection<TerminalPanelSessionViewModel>?)GetValue(TerminalSessionsProperty);
        set => SetValue(TerminalSessionsProperty, value);
    }

    public static readonly DependencyProperty TerminalSessionsProperty =
        DependencyProperty.Register(
            nameof(TerminalSessions),
            typeof(ObservableCollection<TerminalPanelSessionViewModel>),
            typeof(BottomPanelHost),
            new PropertyMetadata(null, OnTerminalStateChanged));

    public TerminalPanelSessionViewModel? SelectedTerminalSession
    {
        get => (TerminalPanelSessionViewModel?)GetValue(SelectedTerminalSessionProperty);
        set => SetValue(SelectedTerminalSessionProperty, value);
    }

    public static readonly DependencyProperty SelectedTerminalSessionProperty =
        DependencyProperty.Register(
            nameof(SelectedTerminalSession),
            typeof(TerminalPanelSessionViewModel),
            typeof(BottomPanelHost),
            new PropertyMetadata(null, OnTerminalStateChanged));

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
            new PropertyMetadata(null, OnTerminalStateChanged));

    private static void OnTabsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BottomPanelHost host)
        {
            // Refresh x:Bind-computed properties such as SelectedTabTitle when the DP-backed source changes.
            host.Bindings.Update();
        }
    }

    private static void OnSelectedTabChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BottomPanelHost host)
        {
            host.Bindings.Update();
        }
    }

    private static void OnTerminalStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BottomPanelHost host)
        {
            host.Bindings.Update();
        }
    }

    private static string GetResourceString(string? resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return string.Empty;
        }

        try
        {
            return BottomPanelResourceLookup.ResolveResourceString(ResourceLoader, resourceKey);
        }
        catch
        {
            return resourceKey;
        }
    }
}

public sealed class ResourceKeyToStringConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string resourceKey || string.IsNullOrWhiteSpace(resourceKey))
        {
            return string.Empty;
        }

        try
        {
            return BottomPanelResourceLookup.ResolveResourceString(ResourceLoader, resourceKey);
        }
        catch
        {
            return resourceKey;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

internal static class BottomPanelResourceLookup
{
    public static string ResolveResourceString(ResourceLoader resourceLoader, string resourceKey)
    {
        var value = resourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? resourceKey : value;
    }
}

public sealed class TabAutomationIdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string id || string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        return $"BottomPanelTab.{id}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
