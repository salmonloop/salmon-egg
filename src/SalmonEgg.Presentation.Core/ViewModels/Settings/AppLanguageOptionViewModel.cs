namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed class AppLanguageOptionViewModel
{
    public AppLanguageOptionViewModel(string tag, string displayNameResourceKey)
    {
        Tag = tag;
        DisplayNameResourceKey = displayNameResourceKey;
    }

    public string Tag { get; }

    public string DisplayNameResourceKey { get; }
}
