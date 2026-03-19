namespace SalmonEgg.Presentation.Models.Navigation;

public abstract record NavigationSelectionState
{
    public sealed record Start : NavigationSelectionState;

    public sealed record Settings : NavigationSelectionState;

    public sealed record Session(string SessionId) : NavigationSelectionState;

    public static NavigationSelectionState StartSelection { get; } = new Start();

    public static NavigationSelectionState SettingsSelection { get; } = new Settings();
}
