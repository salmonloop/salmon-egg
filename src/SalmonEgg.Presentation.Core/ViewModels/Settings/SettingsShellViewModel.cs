using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Settings;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed class SettingsShellSectionViewModel
{
    public SettingsShellSectionViewModel(string key, string title, string automationId)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Section key is required.", nameof(key)) : key;
        Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("Section title is required.", nameof(title)) : title;
        AutomationId = string.IsNullOrWhiteSpace(automationId) ? throw new ArgumentException("Automation id is required.", nameof(automationId)) : automationId;
    }

    public string Key { get; }

    public string Title { get; }

    public string AutomationId { get; }
}

public sealed partial class SettingsShellViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, SettingsShellSectionViewModel> _sectionsByKey;
    private readonly ISettingsSectionSelectionStore _selectionStore;
    private SettingsShellSectionViewModel _selectedSection;

    public SettingsShellViewModel(
        IStringLocalizer<CoreStrings> localizer,
        ISettingsSectionSelectionStore selectionStore)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));

        Sections = CreateSections(localizer);
        _sectionsByKey = BuildSectionIndex(Sections);
        _selectedSection = ResolveSection(_selectionStore.CurrentSectionKey);
    }

    public IReadOnlyList<SettingsShellSectionViewModel> Sections { get; }

    public SettingsShellSectionViewModel SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedSection, value);
            }
        }
    }

    public SettingsShellSectionViewModel SelectSection(string? key)
    {
        var section = ResolveSection(_selectionStore.Select(key));
        SelectedSection = section;
        return section;
    }

    private SettingsShellSectionViewModel ResolveSection(string? key)
        => !string.IsNullOrWhiteSpace(key) && _sectionsByKey.TryGetValue(key, out var section)
            ? section
            : _sectionsByKey[SettingsSectionCatalog.DefaultSection.Key];

    private static SettingsShellSectionViewModel CreateSection(string key, LocalizedString title, string automationId)
        => new(key, title.Value, automationId);

    private static IReadOnlyList<SettingsShellSectionViewModel> CreateSections(
        IStringLocalizer<CoreStrings> localizer)
    {
        var sections = new List<SettingsShellSectionViewModel>(SettingsSectionCatalog.Sections.Count);
        foreach (var section in SettingsSectionCatalog.Sections)
        {
            sections.Add(CreateSection(
                section.Key,
                localizer[section.TitleResourceKey],
                section.AutomationId));
        }

        return sections;
    }

    private static IReadOnlyDictionary<string, SettingsShellSectionViewModel> BuildSectionIndex(
        IReadOnlyList<SettingsShellSectionViewModel> sections)
    {
        var index = new Dictionary<string, SettingsShellSectionViewModel>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            index.Add(section.Key, section);
        }

        return index;
    }
}
