using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Models.Settings;

public sealed class SettingsSectionDefinition
{
    public SettingsSectionDefinition(string key, string titleResourceKey, string automationId)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Section key is required.", nameof(key)) : key;
        TitleResourceKey = string.IsNullOrWhiteSpace(titleResourceKey)
            ? throw new ArgumentException("Section title resource key is required.", nameof(titleResourceKey))
            : titleResourceKey;
        AutomationId = string.IsNullOrWhiteSpace(automationId)
            ? throw new ArgumentException("Automation id is required.", nameof(automationId))
            : automationId;
    }

    public string Key { get; }

    public string TitleResourceKey { get; }

    public string AutomationId { get; }
}

public static class SettingsSectionCatalog
{
    public const string GeneralKey = "General";
    public const string AppearanceKey = "Appearance";
    public const string AgentAcpKey = "AgentAcp";
    public const string McpKey = "Mcp";
    public const string DataStorageKey = "DataStorage";
    public const string ShortcutsKey = "Shortcuts";
    public const string DiagnosticsKey = "Diagnostics";
    public const string AboutKey = "About";

    private static readonly IReadOnlyList<SettingsSectionDefinition> SectionsInternal =
    [
        new(GeneralKey, "SettingsSection_General", "SettingsNav.General"),
        new(AppearanceKey, "SettingsSection_Appearance", "SettingsNav.Appearance"),
        new(AgentAcpKey, "SettingsSection_AgentAcp", "SettingsNav.AgentAcp"),
        new(McpKey, "SettingsSection_Mcp", "SettingsNav.Mcp"),
        new(DataStorageKey, "SettingsSection_DataStorage", "SettingsNav.DataStorage"),
        new(ShortcutsKey, "SettingsSection_Shortcuts", "SettingsNav.Shortcuts"),
        new(DiagnosticsKey, "SettingsSection_Diagnostics", "SettingsNav.Diagnostics"),
        new(AboutKey, "SettingsSection_About", "SettingsNav.About")
    ];

    private static readonly IReadOnlyDictionary<string, SettingsSectionDefinition> SectionsByKey =
        BuildSectionIndex(SectionsInternal);

    public static IReadOnlyList<SettingsSectionDefinition> Sections => SectionsInternal;

    public static SettingsSectionDefinition DefaultSection => SectionsByKey[GeneralKey];

    public static SettingsSectionDefinition FindOrDefault(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !SectionsByKey.TryGetValue(key, out var section))
        {
            return DefaultSection;
        }

        return section;
    }

    public static string ResolveRootTitle(IStringLocalizer<CoreStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return ResolveLocalizedValue(localizer["Nav_Settings"], "Settings");
    }

    public static string ResolveTitle(IStringLocalizer<CoreStrings> localizer, string? key)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var section = FindOrDefault(key);
        return ResolveLocalizedValue(localizer[section.TitleResourceKey], section.Key);
    }

    private static IReadOnlyDictionary<string, SettingsSectionDefinition> BuildSectionIndex(
        IReadOnlyList<SettingsSectionDefinition> sections)
    {
        var index = new Dictionary<string, SettingsSectionDefinition>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            index.Add(section.Key, section);
        }

        return index;
    }

    private static string ResolveLocalizedValue(LocalizedString localized, string fallback)
        => string.IsNullOrWhiteSpace(localized.Value) ? fallback : localized.Value;
}
