using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpAvailabilityPolicy
{
    bool IsAcpEnabled { get; }
}

public sealed class AppPreferencesAcpAvailabilityPolicy : IAcpAvailabilityPolicy
{
    private readonly AppPreferencesViewModel _preferences;

    public AppPreferencesAcpAvailabilityPolicy(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public bool IsAcpEnabled => _preferences.AcpEnabled;
}

public sealed class AlwaysEnabledAcpAvailabilityPolicy : IAcpAvailabilityPolicy
{
    public static AlwaysEnabledAcpAvailabilityPolicy Instance { get; } = new();

    private AlwaysEnabledAcpAvailabilityPolicy()
    {
    }

    public bool IsAcpEnabled => true;
}
