using System;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Services;

public sealed class AppPreferencesAcpAvailabilityPolicy : IAcpAvailabilityPolicy
{
    private readonly AppPreferencesViewModel _preferences;

    public AppPreferencesAcpAvailabilityPolicy(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public bool IsAcpEnabled => _preferences.IsLoaded && _preferences.AcpEnabled;
}
