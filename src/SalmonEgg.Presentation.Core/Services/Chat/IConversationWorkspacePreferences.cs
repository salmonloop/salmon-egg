using System;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationWorkspacePreferences
{
    bool SaveLocalHistory { get; }
}

public sealed class AppPreferencesConversationWorkspacePreferences : IConversationWorkspacePreferences
{
    private readonly AppPreferencesViewModel _preferences;

    public AppPreferencesConversationWorkspacePreferences(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public bool SaveLocalHistory => _preferences.SaveLocalHistory;
}
