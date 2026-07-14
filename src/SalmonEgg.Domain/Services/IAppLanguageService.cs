using System;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IAppLanguageService
{
    bool IsSupported { get; }

    string CurrentLanguageTag { get; }

    event EventHandler? LanguageChanged;

    Task ApplyLanguageOverrideAsync(string languageTag);
}
