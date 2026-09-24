using System;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Navigation;

public static class MainNavigationAutomationIds
{
    public static string StartItem() => "MainNav.Start";

    public static string DiscoverSessionsItem() => "MainNav.DiscoverSessions";

    public static string SettingsItem() => "SettingsItem";

    public static string SessionsLabel() => "MainNav.SessionsLabel";

    public static string AddProject() => "MainNav.AddProject";

    public static string ProjectItem(string projectId) => WithSuffix("MainNav.Project", projectId);

    public static string SessionItem(string sessionId) => WithSuffix("MainNav.Session", sessionId);

    public static string SessionUnread(string sessionId) => WithSuffix("MainNav.Session.Unread", sessionId);

    public static string MoreItem(string projectId) => WithSuffix("MainNav.More", projectId);

    public static string StatusGroupItem(ConversationStatusGroup group) => WithSuffix("MainNav.StatusGroup", group.ToString());

    public static string MoreGroupItem(string projectId, ConversationStatusGroup? group)
        => group is { } status ? WithSuffix("MainNav.More.StatusGroup", status.ToString()) : MoreItem(projectId);

    public static string SessionsDialogItem(string sessionId) => WithSuffix("SessionsDialog.Session", sessionId);

    private static string WithSuffix(string prefix, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return prefix;
        }

        return prefix + "." + value.Trim();
    }
}
