using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class PermissionRequestViewModel : ObservableObject
{
    private bool _showsCancellationRetry;

    [ObservableProperty]
    private ObservableCollection<PermissionOptionViewModel> _options = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public object MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ToolCallJson { get; set; } = string.Empty;
    internal string? ToolCallId { get; set; }
    internal string? RequestTitle { get; set; }
    internal ConversationBindingSlice? Binding { get; set; }
    internal Task? BindingCancellationTask { get; set; }
    internal bool BindingCancellationAttempted { get; set; }
    internal Func<bool>? IsRequestAvailable { get; set; }
    internal Func<bool>? IsResponsePrepared { get; set; }
    internal Func<bool>? IsRequestCancellationRequested { get; set; }
    internal Action? UnsubscribeRequestChanges { get; set; }
    internal bool IsAvailable => IsRequestAvailable?.Invoke() ?? true;
    internal bool IsAwaitingInput => IsAvailable && IsResponsePrepared?.Invoke() != true;
    internal bool IsCancellationOnly => BindingCancellationAttempted || IsRequestCancellationRequested?.Invoke() == true;

    public Func<string, string?, Task>? OnRespond { get; set; }

    internal void ShowCancellationRetry(string title, string description, bool bindingChanged)
    {
        if (bindingChanged) BindingCancellationAttempted = true;
        ToolCallId = null;
        Title = title;
        Description = description;
        if (_showsCancellationRetry)
        {
            Options[0].Name = title;
            Options[0].Description = description;
            return;
        }
        _showsCancellationRetry = true;
        Options.Clear();
        Options.Add(new PermissionOptionViewModel
        {
            Name = title,
            Description = description,
            OnSelect = _ => RespondCommand.ExecuteAsync(null)
        });
    }

    internal void DetachRequest()
    {
        UnsubscribeRequestChanges?.Invoke();
        UnsubscribeRequestChanges = null;
    }

    internal void ReprojectLocalizedText(
        string defaultTitle, string cancellationTitle, string bindingCancellationDescription, string peerCancellationDescription)
    {
        if (!_showsCancellationRetry)
        {
            Title = string.IsNullOrWhiteSpace(RequestTitle) ? defaultTitle : RequestTitle;
            return;
        }

        Title = cancellationTitle;
        Description = BindingCancellationAttempted ? bindingCancellationDescription : peerCancellationDescription;
        foreach (var option in Options)
        {
            option.Name = cancellationTitle;
            option.Description = Description;
        }
    }

    [RelayCommand]
    private async Task RespondAsync(PermissionOptionViewModel? option)
    {
        if (OnRespond == null || !IsAvailable)
        {
            return;
        }

        if (option != null && !IsCancellationOnly)
        {
            await OnRespond("selected", option.OptionId);
        }
        else
        {
            await OnRespond("cancelled", null);
        }
    }
}

public partial class PermissionOptionViewModel : ObservableObject
{
    public Func<PermissionOptionViewModel, Task>? OnSelect { get; set; }

    [ObservableProperty]
    private string _optionId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _kind = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public bool IsAllow => Kind.StartsWith("allow", StringComparison.Ordinal);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [RelayCommand]
    private async Task SelectAsync()
    {
        if (OnSelect != null)
        {
            await OnSelect(this).ConfigureAwait(true);
        }
    }

    partial void OnKindChanged(string value) => OnPropertyChanged(nameof(IsAllow));

    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasDescription));
}
