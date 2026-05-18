using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI-only adapter that projects settings sections into a native Top NavigationView.
/// </summary>
public sealed class SettingsSectionNavigationAdapter : IDisposable
{
    private readonly NavigationView _navigationView;
    private readonly Dictionary<string, NavigationViewItem> _sectionItemsByKey = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _suppressSelectionChanged;

    public SettingsSectionNavigationAdapter(
        NavigationView navigationView,
        IReadOnlyList<SettingsShellSectionViewModel> sections)
    {
        _navigationView = navigationView ?? throw new ArgumentNullException(nameof(navigationView));
        ArgumentNullException.ThrowIfNull(sections);

        PopulateSections(sections);
        _navigationView.SelectionChanged += OnSelectionChanged;
    }

    public event EventHandler<SettingsSectionNavigationInvokedEventArgs>? SectionInvoked;

    public void Select(string key)
    {
        ThrowIfDisposed();

        var section = SettingsSectionCatalog.FindOrDefault(key);
        if (!_sectionItemsByKey.TryGetValue(section.Key, out var item))
        {
            return;
        }

        _suppressSelectionChanged = true;
        try
        {
            _navigationView.SelectedItem = item;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _navigationView.SelectionChanged -= OnSelectionChanged;
        _disposed = true;
    }

    private void PopulateSections(IReadOnlyList<SettingsShellSectionViewModel> sections)
    {
        // Keep Settings Top NavigationView on its native MenuItems path; Uno splits
        // MenuItemsSource internally for overflow, which can desynchronize indices.
        _navigationView.MenuItems.Clear();
        _sectionItemsByKey.Clear();

        foreach (var section in sections)
        {
            var item = new NavigationViewItem
            {
                Content = section.Title,
                Tag = section.Key
            };
            AutomationProperties.SetAutomationId(item, section.AutomationId);
            _navigationView.MenuItems.Add(item);
            _sectionItemsByKey[section.Key] = item;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_disposed || _suppressSelectionChanged)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string key)
        {
            SectionInvoked?.Invoke(this, new SettingsSectionNavigationInvokedEventArgs(key));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SettingsSectionNavigationAdapter));
        }
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
