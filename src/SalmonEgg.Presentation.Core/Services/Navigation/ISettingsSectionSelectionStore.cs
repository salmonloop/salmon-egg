using System.Threading;
using SalmonEgg.Presentation.Models.Settings;

namespace SalmonEgg.Presentation.Core.Services;

public interface ISettingsSectionSelectionStore
{
    string CurrentSectionKey { get; }

    string Select(string? sectionKey);
}

public sealed class SettingsSectionSelectionStore : ISettingsSectionSelectionStore
{
    private string _currentSectionKey = SettingsSectionCatalog.GeneralKey;

    public string CurrentSectionKey => Volatile.Read(ref _currentSectionKey);

    public string Select(string? sectionKey)
    {
        var normalizedSectionKey = SettingsSectionCatalog.FindOrDefault(sectionKey).Key;
        Volatile.Write(ref _currentSectionKey, normalizedSectionKey);
        return normalizedSectionKey;
    }
}
