namespace SalmonEgg.Presentation.ViewModels.Navigation;

public static class SessionCwdResolver
{
    public static string? Resolve(string? pendingProjectRootPath, string? lastSelectedProjectRootPath)
    {
        var pending = Normalize(pendingProjectRootPath);
        if (!string.IsNullOrWhiteSpace(pending))
        {
            return pending;
        }

        var lastSelected = Normalize(lastSelectedProjectRootPath);
        return string.IsNullOrWhiteSpace(lastSelected) ? null : lastSelected;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Trim();
    }
}
