using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views.Navigation;

public sealed partial class SessionsListDialog : ContentDialog, INotifyPropertyChanged
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();
    private readonly IReadOnlyList<SessionNavItemViewModel> _allSessions;
    private string _filterText = string.Empty;
    private string _dialogTitle = ResolveResourceString("SessionsDialogDefaultTitle", "会话");

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SessionNavItemViewModel> FilteredSessions { get; } = new();

    public string DialogTitle
    {
        get => _dialogTitle;
        set
        {
            if (string.Equals(_dialogTitle, value, StringComparison.Ordinal))
            {
                return;
            }

            _dialogTitle = value;
            OnPropertyChanged(nameof(DialogTitle));
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filterText, value, StringComparison.Ordinal))
            {
                return;
            }

            _filterText = value;
            OnPropertyChanged(nameof(FilterText));
            ApplyFilter();
        }
    }

    public string? PickedSessionId { get; private set; }

    public SessionsListDialog(string title, IReadOnlyList<SessionNavItemViewModel> sessions)
    {
        _allSessions = sessions ?? Array.Empty<SessionNavItemViewModel>();
        _dialogTitle = string.IsNullOrWhiteSpace(title)
            ? ResolveResourceString("SessionsDialogDefaultTitle", "会话")
            : title.Trim();

        InitializeComponent();
        ApplyFilter();
    }

    private void OnSessionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SessionNavItemViewModel vm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.SessionId) || vm.IsPlaceholder)
        {
            return;
        }

        PickedSessionId = vm.SessionId;
        Hide();
    }

    private void ApplyFilter()
    {
        var filter = (_filterText ?? string.Empty).Trim();
        IEnumerable<SessionNavItemViewModel> next = _allSessions;

        if (!string.IsNullOrEmpty(filter))
        {
            next = _allSessions.Where(s => (s.Title ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        FilteredSessions.Clear();
        foreach (var item in next)
        {
            FilteredSessions.Add(item);
        }
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
