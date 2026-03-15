using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class SlashCommandViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _inputHint;

    public string DisplayText => "/" + Name;
}

