namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed class SettingsOptionViewModel
{
    public SettingsOptionViewModel(string value, string displayNameResourceKey)
    {
        Value = value;
        DisplayNameResourceKey = displayNameResourceKey;
    }

    public string Value { get; }

    public string DisplayNameResourceKey { get; }
}
