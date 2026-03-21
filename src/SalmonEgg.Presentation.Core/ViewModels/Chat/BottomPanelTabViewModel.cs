using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed partial class BottomPanelTabViewModel : ObservableObject
{
    public BottomPanelTabViewModel(string id, string titleResourceKey)
    {
        Id = id ?? string.Empty;
        TitleResourceKey = titleResourceKey ?? string.Empty;
    }

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _titleResourceKey = string.Empty;
}
