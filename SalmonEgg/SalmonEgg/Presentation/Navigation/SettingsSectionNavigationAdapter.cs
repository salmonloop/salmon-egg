using System;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI-only adapter that projects settings sections into a native Top NavigationView.
/// </summary>
public sealed class SettingsSectionNavigationAdapter : IDisposable
{
    private readonly NavigationView _navigationView;
    private bool _disposed;

    public SettingsSectionNavigationAdapter(NavigationView navigationView)
    {
        _navigationView = navigationView ?? throw new ArgumentNullException(nameof(navigationView));

        _navigationView.ItemInvoked += OnItemInvoked;
    }

    public event EventHandler<SettingsSectionNavigationInvokedEventArgs>? SectionInvoked;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _navigationView.ItemInvoked -= OnItemInvoked;
        _disposed = true;
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        if (TryGetInvokedSectionKey(args, out var key))
        {
            SectionInvoked?.Invoke(this, new SettingsSectionNavigationInvokedEventArgs(key));
        }
    }

    private static bool TryGetInvokedSectionKey(NavigationViewItemInvokedEventArgs args, out string key)
    {
        key = string.Empty;

        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            if (item.DataContext is SettingsShellSectionViewModel section)
            {
                key = section.Key;
                return true;
            }

            if (item.Tag is string tag)
            {
                key = tag;
                return true;
            }
        }

        if (args.InvokedItem is SettingsShellSectionViewModel invokedSection)
        {
            key = invokedSection.Key;
            return true;
        }

        return false;
    }
}

public sealed class SettingsSectionNavigationInvokedEventArgs : EventArgs
{
    public SettingsSectionNavigationInvokedEventArgs(string key)
    {
        Key = string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("Section key is required.", nameof(key))
            : key;
    }

    public string Key { get; }
}
