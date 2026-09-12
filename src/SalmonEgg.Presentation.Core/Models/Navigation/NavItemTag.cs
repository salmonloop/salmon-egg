using System;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Models.Navigation;

public static class NavItemTag
{
    public static string Start => "Start";
    public static string DiscoverSessions => "DiscoverSessions";
    public static string Settings => "Settings";
    public static string SessionsLabel => "SessionsLabel";
    public static string AddProject => "AddProject";

    private const string SessionPrefix = "Session:";
    private const string ProjectPrefix = "Project:";
    private const string MorePrefix = "More:";
    private const string StatusGroupPrefix = "StatusGroup:";
    private const string MoreStatusGroupPrefix = "MoreStatusGroup:";

    public static string Session(string sessionId) => string.IsNullOrWhiteSpace(sessionId)
        ? SessionPrefix
        : SessionPrefix + sessionId;

    public static string Project(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? ProjectPrefix
        : ProjectPrefix + projectId;

    public static string More(string projectId) => string.IsNullOrWhiteSpace(projectId)
        ? MorePrefix
        : MorePrefix + projectId;

    public static string StatusGroup(ConversationStatusGroup group) => StatusGroupPrefix + group;

    public static string MoreStatusGroup(ConversationStatusGroup group) => MoreStatusGroupPrefix + group;

    public static bool TryParseStatusGroup(string? tag, out ConversationStatusGroup group)
        => TryParseGroup(tag, StatusGroupPrefix, out group);

    public static bool TryParseMoreStatusGroup(string? tag, out ConversationStatusGroup group)
        => TryParseGroup(tag, MoreStatusGroupPrefix, out group);

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

    private static bool TryParseGroup(string? tag, string prefix, out ConversationStatusGroup group)
    {
        group = default;
        return tag is not null
            && tag.StartsWith(prefix, StringComparison.Ordinal)
            && Enum.TryParse(tag[prefix.Length..], out group)
            && Enum.IsDefined(group)
            && string.Equals(group.ToString(), tag[prefix.Length..], StringComparison.Ordinal);
    }
}
