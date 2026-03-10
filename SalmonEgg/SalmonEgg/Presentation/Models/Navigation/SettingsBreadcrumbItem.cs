namespace SalmonEgg.Presentation.Models.Navigation;

/// <summary>
/// Presentation-only model for WinUI BreadcrumbBar.
/// </summary>
public sealed class SettingsBreadcrumbItem
{
    public SettingsBreadcrumbItem(string text, string? settingsKey = null, bool isCurrent = false)
    {
        Text = text;
        SettingsKey = settingsKey;
        IsCurrent = isCurrent;
    }

    public string Text { get; }

    /// <summary>
    /// When set, clicking this item navigates to the corresponding Settings key in the shell.
    /// </summary>
    public string? SettingsKey { get; }

    /// <summary>
    /// Indicates the current page; current items should not navigate.
    /// </summary>
    public bool IsCurrent { get; }

    public static SettingsBreadcrumbItem Link(string text, string settingsKey) => new(text, settingsKey, isCurrent: false);

    public static SettingsBreadcrumbItem Current(string text) => new(text, settingsKey: null, isCurrent: true);
}

