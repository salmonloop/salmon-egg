using System;

namespace SalmonEgg.Presentation.Models.Navigation;

internal static class NavItemTag
{
    public static string SessionsHeader => "SessionsHeader";
    public static string Start => "Start";

    private const string ProjectPrefix = "Project:";
    private const string MorePrefix = "More:";

    public static string Project(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? ProjectPrefix
        : ProjectPrefix + projectId;

    public static string More(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? MorePrefix
        : MorePrefix + projectId;

    public static bool TryParseProject(string? tag, out string projectId)
    {
        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(ProjectPrefix, StringComparison.Ordinal))
        {
            projectId = string.Empty;
            return false;
        }

        projectId = tag.Substring(ProjectPrefix.Length);
        return !string.IsNullOrWhiteSpace(projectId);
    }

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
