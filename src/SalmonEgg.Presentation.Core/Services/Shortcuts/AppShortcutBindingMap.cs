using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.Services.Shortcuts;

public sealed class AppShortcutBindingMap
{
    private readonly Dictionary<AppShortcutGesture, string> _gestureToAction;

    private AppShortcutBindingMap(Dictionary<AppShortcutGesture, string> gestureToAction)
    {
        _gestureToAction = gestureToAction;
    }

    public static AppShortcutBindingMap Create(
        IReadOnlyDictionary<string, string>? savedBindings,
        bool keyboardShortcutsEnabled = true)
    {
        if (!keyboardShortcutsEnabled)
        {
            return new AppShortcutBindingMap([]);
        }

        var candidates = new List<(string ActionId, AppShortcutGesture Gesture)>();

        foreach (var definition in AppShortcutCatalog.EditableActions)
        {
            if (!TryResolveConfiguredGesture(savedBindings, definition, out var gesture))
            {
                continue;
            }

            candidates.Add((definition.ActionId, gesture));
        }

        var resolved = candidates
            .GroupBy(candidate => candidate.Gesture)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().ActionId);

        return new AppShortcutBindingMap(resolved);
    }

    public bool TryResolveActionId(AppShortcutGesture gesture, out string actionId)
        => _gestureToAction.TryGetValue(gesture, out actionId!);

    public IReadOnlyDictionary<AppShortcutGesture, string> AsDictionary() => _gestureToAction;

    private static bool TryResolveConfiguredGesture(
        IReadOnlyDictionary<string, string>? savedBindings,
        AppShortcutDefinition definition,
        out AppShortcutGesture gesture)
    {
        if (savedBindings != null &&
            savedBindings.TryGetValue(definition.ActionId, out var savedValue) &&
            AppShortcutGesture.TryParse(savedValue, out gesture))
        {
            return true;
        }

        return AppShortcutGesture.TryParse(definition.DefaultGesture, out gesture);
    }
}
