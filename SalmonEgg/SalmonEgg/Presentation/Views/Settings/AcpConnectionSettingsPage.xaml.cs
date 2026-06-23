using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AcpConnectionSettingsPage : SettingsPageBase
{
    private readonly HashSet<AcpRemoteDirectoryRowViewModel> _observedRemoteDirectoryRows = new();

    public AcpConnectionSettingsViewModel ViewModel { get; }

    public AcpConnectionSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AcpConnectionSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.RemoteDirectoryRows.CollectionChanged += OnRemoteDirectoryRowsCollectionChanged;
        AttachRemoteDirectoryRowHandlers();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.AgentAcpKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => AcpGlobalEnabledToggle;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
        AttachRemoteDirectoryRowHandlers();
        QueueFocusEditingRemoteDirectory();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.RemoteDirectoryRows.CollectionChanged -= OnRemoteDirectoryRowsCollectionChanged;
        DetachRemoteDirectoryRowHandlers();
    }

    private void OnRemoteDirectoryRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AcpRemoteDirectoryRowViewModel>())
            {
                DetachRemoteDirectoryRow(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AcpRemoteDirectoryRowViewModel>())
            {
                AttachRemoteDirectoryRow(item);
            }
        }

        QueueFocusEditingRemoteDirectory();
    }

    private void AttachRemoteDirectoryRowHandlers()
    {
        foreach (var row in ViewModel.RemoteDirectoryRows)
        {
            AttachRemoteDirectoryRow(row);
        }
    }

    private void DetachRemoteDirectoryRowHandlers()
    {
        foreach (var row in _observedRemoteDirectoryRows.ToArray())
        {
            DetachRemoteDirectoryRow(row);
        }
    }

    private void AttachRemoteDirectoryRow(AcpRemoteDirectoryRowViewModel row)
    {
        if (_observedRemoteDirectoryRows.Add(row))
        {
            row.PropertyChanged += OnRemoteDirectoryRowPropertyChanged;
        }
    }

    private void DetachRemoteDirectoryRow(AcpRemoteDirectoryRowViewModel row)
    {
        if (_observedRemoteDirectoryRows.Remove(row))
        {
            row.PropertyChanged -= OnRemoteDirectoryRowPropertyChanged;
        }
    }

    private void OnRemoteDirectoryRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AcpRemoteDirectoryRowViewModel row)
        {
            return;
        }

        if (e.PropertyName == nameof(AcpRemoteDirectoryRowViewModel.IsEditing) && row.IsEditing)
        {
            QueueFocusEditingRemoteDirectory();
        }
    }

    private void QueueFocusEditingRemoteDirectory()
    {
        _ = DispatcherQueue.TryEnqueue(FocusEditingRemoteDirectory);
    }

    private void FocusEditingRemoteDirectory()
    {
        var editingRow = ViewModel.RemoteDirectoryRows.FirstOrDefault(row => row.IsEditing);
        if (editingRow is null)
        {
            return;
        }

        AcpRemoteDirectoriesList.ScrollIntoView(editingRow);
        UpdateLayout();
        AcpRemoteDirectoriesList.UpdateLayout();

        if (AcpRemoteDirectoriesList.ContainerFromItem(editingRow) is ListViewItem container
            && FindDescendant<TextBox>(container, textBox =>
                string.Equals(textBox.Name, "AcpRemoteDirectoryDisplayNameBox", StringComparison.Ordinal)) is TextBox displayNameBox)
        {
            _ = displayNameBox.Focus(FocusState.Keyboard);
        }
    }

    private void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(
            typeof(AgentProfileEditorPage),
            new AgentProfileEditorArgs(isEditing: false, profileId: null),
            UiMotionController.Current.CreateNavigationTransitionInfo());
    }

    private void OnEditProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string profileId)
        {
            return;
        }

        Frame?.Navigate(
            typeof(AgentProfileEditorPage),
            new AgentProfileEditorArgs(isEditing: true, profileId: profileId),
            UiMotionController.Current.CreateNavigationTransitionInfo());
    }

    private async void OnDeleteProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string profileId)
        {
            return;
        }

        var config = ViewModel.Profiles.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (config != null)
        {
            await ViewModel.Profiles.DeleteCommand.ExecuteAsync(config);
        }
    }

    private async void OnProfileConnectionToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not AgentProfileItemViewModel item)
        {
            return;
        }

        // Ignore programmatic state synchronization; only react to user-initiated toggles.
        if (toggle.IsOn == item.IsConnected)
        {
            return;
        }

        if (!item.ApplyConnectionToggleRequestCommand.CanExecute(toggle.IsOn))
        {
            return;
        }

        await item.ApplyConnectionToggleRequestCommand.ExecuteAsync(toggle.IsOn);
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return default;
    }

}
