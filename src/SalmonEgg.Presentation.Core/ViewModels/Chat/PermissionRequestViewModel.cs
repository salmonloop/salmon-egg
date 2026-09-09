using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class PermissionRequestViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PermissionOptionViewModel> _options = new();

    public object MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ToolCallJson { get; set; } = string.Empty;
    internal string? ToolCallId { get; set; }
    internal Func<bool>? IsRequestAvailable { get; set; }
    internal bool IsAvailable => IsRequestAvailable?.Invoke() ?? true;

    public Func<string, string?, Task>? OnRespond { get; set; }

    [RelayCommand]
    private async Task RespondAsync(PermissionOptionViewModel? option)
    {
        if (OnRespond == null || !IsAvailable)
        {
            return;
        }

        if (option != null)
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
