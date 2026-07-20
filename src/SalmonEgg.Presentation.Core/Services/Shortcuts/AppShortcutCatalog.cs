using System;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Shortcuts;

public static class AppShortcutCatalog
{
    private static readonly IReadOnlyList<AppShortcutDefinition> EditableActionsInternal =
    [
        new(AppShortcutActionIds.NewSession, "New session", "Ctrl+N"),
        new(AppShortcutActionIds.Search, "Search", "Ctrl+K")
    ];

    private static readonly Dictionary<string, AppShortcutDefinition> ByIdInternal =
        new(StringComparer.OrdinalIgnoreCase);

    static AppShortcutCatalog()
    {
        foreach (var definition in EditableActionsInternal)
        {
            ByIdInternal[definition.ActionId] = definition;
        }
    }

    public static IReadOnlyList<AppShortcutDefinition> EditableActions => EditableActionsInternal;

    public static bool TryGet(string actionId, out AppShortcutDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return ByIdInternal.TryGetValue(actionId, out definition!);
    }
}
