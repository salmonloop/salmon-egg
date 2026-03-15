using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class KeyBindingPairViewModel : ObservableObject
{
    public KeyBindingPairViewModel(string actionId, string gesture)
    {
        _actionId = actionId;
        _gesture = gesture;
    }

    [ObservableProperty]
    private string _actionId = string.Empty;

    [ObservableProperty]
    private string _gesture = string.Empty;
}

