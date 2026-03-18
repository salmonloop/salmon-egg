using System;

namespace SalmonEgg.Presentation.Models.Navigation;

public static class NavItemTag
{
    public static string Start => "Start";
    public static string SessionsHeader => "SessionsHeader";

    private const string SessionPrefix = "Session:";
    private const string MorePrefix = "More:";

    public static string Session(string sessionId) => string.IsNullOrWhiteSpace(sessionId)
        ? SessionPrefix
        : SessionPrefix + sessionId;

    public static string More(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? MorePrefix
        : MorePrefix + projectId;

    public static bool TryParseSession(string? tag, out string sessionId)
    {
        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            sessionId = string.Empty;
            return false;
        }

        sessionId = tag.Substring(SessionPrefix.Length);
        return !string.IsNullOrWhiteSpace(sessionId);
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
