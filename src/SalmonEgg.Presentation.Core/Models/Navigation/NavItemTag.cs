using System;

namespace SalmonEgg.Presentation.Models.Navigation;

public static class NavItemTag
{
    public static string SessionsHeader => "SessionsHeader";

    private const string MorePrefix = "More:";

    public static string More(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? MorePrefix
        : MorePrefix + projectId;

    public static bool TryParseMore(string? tag, out string projectId)
    {
        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(MorePrefix, StringComparison.Ordinal))
        {
            projectId = string.Empty;
            return false;
        }

        projectId = tag.Substring(MorePrefix.Length);
        return !string.IsNullOrWhiteSpace(projectId);
    }
}
