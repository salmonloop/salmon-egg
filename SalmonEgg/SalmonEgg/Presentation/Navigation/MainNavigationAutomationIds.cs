using System;

namespace SalmonEgg.Presentation.Navigation;

public static class MainNavigationAutomationIds
{
    public static string StartItem() => "MainNav.Start";

    public static string SessionsHeader() => "MainNav.SessionsHeader";

    public static string ProjectItem(string projectId) => WithSuffix("MainNav.Project", projectId);

    public static string SessionItem(string sessionId) => WithSuffix("MainNav.Session", sessionId);

    public static string MoreItem(string projectId) => WithSuffix("MainNav.More", projectId);

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
