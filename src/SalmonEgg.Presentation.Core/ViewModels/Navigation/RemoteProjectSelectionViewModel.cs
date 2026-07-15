using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

/// <summary>
/// A single selectable remote directory row. Read-only projection of an
/// <see cref="AgentRemoteDirectory"/>: it carries the stable directory id for identity and
/// the display fields for rendering, but never owns or edits the configuration.
/// </summary>
public sealed class RemoteProjectOptionViewModel
{
    public RemoteProjectOptionViewModel(string directoryId, string displayName, string remotePath)
    {
        DirectoryId = directoryId ?? string.Empty;
        RemotePath = remotePath ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? RemotePath : displayName.Trim();
    }

    public string DirectoryId { get; }

    public string DisplayName { get; }

    public string RemotePath { get; }

    /// <summary>Accessible name combines the display name and full path so screen readers
    /// can disambiguate directories that share a name.</summary>
    public string AutomationName
        => string.Equals(DisplayName, RemotePath, StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName}, {RemotePath}";
}

/// <summary>
/// Drives the remote project selection dialog. Projects the authoritative remote directory
/// configuration into a read-only, selectable list; the selection state holds only the
/// stable directory id. The dialog is a pure view over this state — it does not own project,
/// path or navigation business state.
/// </summary>
public sealed partial class RemoteProjectSelectionViewModel : ObservableObject, IDisposable
{
    private readonly INavigationProjectPreferences _preferences;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly NotifyCollectionChangedEventHandler _directoriesChangedHandler;
    private bool _disposed;

    public ObservableCollection<RemoteProjectOptionViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string? _selectedDirectoryId;

    public bool HasProjects => Items.Count > 0;

    public bool IsEmpty => Items.Count == 0;

    public bool CanConfirm
        => !string.IsNullOrWhiteSpace(SelectedDirectoryId)
            && Items.Any(item => string.Equals(item.DirectoryId, SelectedDirectoryId, StringComparison.Ordinal));

    public RemoteProjectSelectionViewModel(
        INavigationProjectPreferences preferences,
        IUiDispatcher uiDispatcher)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        _directoriesChangedHandler = (_, _) => _uiDispatcher.Enqueue(RebuildItems);
        ((INotifyCollectionChanged)_preferences.AgentRemoteDirectories).CollectionChanged += _directoriesChangedHandler;

        RebuildItems();
    }

    /// <summary>Do not auto-select the first item: the dialog opens with the add action
    /// disabled until the user explicitly picks a directory (plan §2.4).</summary>
    private void RebuildItems()
    {
        var previousSelection = SelectedDirectoryId;

        Items.Clear();
        foreach (var directory in _preferences.AgentRemoteDirectories
                     .Where(d => d is not null
                                 && !string.IsNullOrWhiteSpace(d.DirectoryId)
                                 && !string.IsNullOrWhiteSpace(d.RemotePath))
                     .OrderBy(d => string.IsNullOrWhiteSpace(d.DisplayName) ? d.RemotePath : d.DisplayName, StringComparer.Ordinal))
        {
            Items.Add(new RemoteProjectOptionViewModel(
                directory.DirectoryId,
                directory.DisplayName,
                directory.RemotePath));
        }

        // Preserve a still-valid selection across a projection refresh; drop it otherwise so a
        // stale id can never be confirmed.
        if (!string.IsNullOrWhiteSpace(previousSelection)
            && Items.Any(item => string.Equals(item.DirectoryId, previousSelection, StringComparison.Ordinal)))
        {
            SelectedDirectoryId = previousSelection;
        }
        else if (!string.IsNullOrWhiteSpace(previousSelection))
        {
            SelectedDirectoryId = null;
        }

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanConfirm));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ((INotifyCollectionChanged)_preferences.AgentRemoteDirectories).CollectionChanged -= _directoriesChangedHandler;
    }
}
