using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Views.Navigation;

public sealed partial class ConversationProjectPickerDialog : ContentDialog, INotifyPropertyChanged
{
    private readonly IReadOnlyList<ConversationProjectTargetOption> _options;
    private string _dialogTitle = string.Empty;
    private string _sessionTitle = string.Empty;
    private ConversationProjectTargetOption? _selectedOption;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConversationProjectPickerDialog(
        string title,
        string sessionTitle,
        IReadOnlyList<ConversationProjectTargetOption> options,
        string? selectedProjectId)
    {
        _options = options ?? Array.Empty<ConversationProjectTargetOption>();
        _dialogTitle = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        _sessionTitle = string.IsNullOrWhiteSpace(sessionTitle) ? string.Empty : sessionTitle.Trim();

        InitializeComponent();

        SelectedOption = ChooseDefaultOption(selectedProjectId);
    }

    public IReadOnlyList<ConversationProjectTargetOption> Options => _options;

    public string DialogTitle
    {
        get => _dialogTitle;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (string.Equals(_dialogTitle, next, StringComparison.Ordinal))
            {
                return;
            }

            _dialogTitle = next;
            OnPropertyChanged(nameof(DialogTitle));
        }
    }

    public string SessionTitle
    {
        get => _sessionTitle;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (string.Equals(_sessionTitle, next, StringComparison.Ordinal))
            {
                return;
            }

            _sessionTitle = next;
            OnPropertyChanged(nameof(SessionTitle));
        }
    }

    public ConversationProjectTargetOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (ReferenceEquals(_selectedOption, value))
            {
                return;
            }

            _selectedOption = value;
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedOption is not null;

    public string? PickedProjectId => SelectedOption?.ProjectId;

    private ConversationProjectTargetOption? ChooseDefaultOption(string? projectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            foreach (var option in _options)
            {
                if (string.Equals(option.ProjectId, projectId, StringComparison.Ordinal))
                {
                    return option;
                }
            }
        }

        return _options.Count > 0 ? _options[0] : null;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
