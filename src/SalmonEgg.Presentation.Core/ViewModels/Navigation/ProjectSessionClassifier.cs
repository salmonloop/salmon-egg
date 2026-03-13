using System;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public static class ProjectSessionClassifier
{
    public static string ClassifyProjectId(
        string? cwd,
        IReadOnlyDictionary<string, string> normalizedRoots,
        string unclassifiedProjectId)
    {
        var cwdNorm = NavTimeFormatter.NormalizePathForPrefixMatch(cwd);
        if (string.IsNullOrWhiteSpace(cwdNorm))
        {
            return unclassifiedProjectId;
        }

        string? bestId = null;
        var bestLen = -1;

        foreach (var kvp in normalizedRoots)
        {
            var rootNorm = kvp.Value;
            if (string.IsNullOrWhiteSpace(rootNorm))
            {
                continue;
            }

            if (cwdNorm.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase) && rootNorm.Length > bestLen)
            {
                bestId = kvp.Key;
                bestLen = rootNorm.Length;
            }
        }

        return bestId ?? unclassifiedProjectId;
    }
}
