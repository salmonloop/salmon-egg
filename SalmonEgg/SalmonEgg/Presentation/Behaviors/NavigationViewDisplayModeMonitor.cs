using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Behaviors;

public static class NavigationViewDisplayModeMonitor
{
    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.RegisterAttached(
            "DisplayMode",
            typeof(NavigationPaneDisplayMode),
            typeof(NavigationViewDisplayModeMonitor),
            new PropertyMetadata(NavigationPaneDisplayMode.Expanded, OnDisplayModeChanged));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(NavigationViewDisplayModeMonitor),
            new PropertyMetadata(false));

    private static readonly DependencyProperty PaneDisplayModeTokenProperty =
        DependencyProperty.RegisterAttached(
            "PaneDisplayModeToken",
            typeof(long),
            typeof(NavigationViewDisplayModeMonitor),
            new PropertyMetadata(0L));

    public static NavigationPaneDisplayMode GetDisplayMode(DependencyObject obj) =>
        (NavigationPaneDisplayMode)obj.GetValue(DisplayModeProperty);

    public static void SetDisplayMode(DependencyObject obj, NavigationPaneDisplayMode value) =>
        obj.SetValue(DisplayModeProperty, value);

    private static bool GetIsHooked(DependencyObject obj) =>
        (bool)obj.GetValue(IsHookedProperty);

    private static void SetIsHooked(DependencyObject obj, bool value) =>
        obj.SetValue(IsHookedProperty, value);

    private static long GetPaneDisplayModeToken(DependencyObject obj) =>
        (long)obj.GetValue(PaneDisplayModeTokenProperty);

    private static void SetPaneDisplayModeToken(DependencyObject obj, long value) =>
        obj.SetValue(PaneDisplayModeTokenProperty, value);

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavigationView nav)
        {
            return;
        }

        if (GetIsHooked(nav))
        {
            return;
        }

        nav.DisplayModeChanged += OnNavDisplayModeChanged;
        nav.SizeChanged += OnNavSizeChanged;
        nav.Loaded += OnNavLoaded;
        nav.Unloaded += OnNavUnloaded;
        var token = nav.RegisterPropertyChangedCallback(NavigationView.PaneDisplayModeProperty, OnPaneDisplayModePropertyChanged);
        SetPaneDisplayModeToken(nav, token);
        SetIsHooked(nav, true);
    }

    private static void OnNavLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationView nav)
        {
            UpdateDisplayMode(nav);
        }
    }

    private static void OnNavUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationView nav)
        {
            nav.DisplayModeChanged -= OnNavDisplayModeChanged;
            nav.SizeChanged -= OnNavSizeChanged;
            nav.Loaded -= OnNavLoaded;
            nav.Unloaded -= OnNavUnloaded;
            var token = GetPaneDisplayModeToken(nav);
            if (token != 0)
            {
                nav.UnregisterPropertyChangedCallback(NavigationView.PaneDisplayModeProperty, token);
                SetPaneDisplayModeToken(nav, 0);
            }
            SetIsHooked(nav, false);
        }
    }

    private static void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateDisplayMode(sender);
    }

    private static void OnNavSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is NavigationView nav)
        {
            UpdateDisplayMode(nav);
        }
    }

    private static void OnPaneDisplayModePropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is NavigationView nav)
        {
            UpdateDisplayMode(nav);
        }
    }

    private static void UpdateDisplayMode(NavigationView nav)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[NavDisplayMode] Raw={nav.DisplayMode} IsPaneOpen={nav.IsPaneOpen} OpenPaneLength={nav.OpenPaneLength} CompactPaneLength={nav.CompactPaneLength}");
#endif
        // Prefer requested PaneDisplayMode when explicitly set, otherwise fall back to effective DisplayMode.
        var mapped = nav.PaneDisplayMode switch
        {
            NavigationViewPaneDisplayMode.LeftMinimal => NavigationPaneDisplayMode.Minimal,
            NavigationViewPaneDisplayMode.LeftCompact => NavigationPaneDisplayMode.Compact,
            NavigationViewPaneDisplayMode.Left => NavigationPaneDisplayMode.Expanded,
            _ => MapDisplayMode(nav.DisplayMode)
        };
        SetDisplayMode(nav, mapped);
    }

    private static NavigationPaneDisplayMode MapDisplayMode(NavigationViewDisplayMode displayMode) =>
        displayMode switch
        {
            NavigationViewDisplayMode.Minimal => NavigationPaneDisplayMode.Minimal,
            NavigationViewDisplayMode.Compact => NavigationPaneDisplayMode.Compact,
            _ => NavigationPaneDisplayMode.Expanded
        };
}
