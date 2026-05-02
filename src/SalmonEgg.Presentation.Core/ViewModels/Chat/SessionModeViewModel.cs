using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Session mode ViewModel
/// </summary>
public partial class SessionModeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _modeId = string.Empty;

    [ObservableProperty]
    private string _modeName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}
