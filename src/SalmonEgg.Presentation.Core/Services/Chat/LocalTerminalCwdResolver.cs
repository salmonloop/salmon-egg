using System;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class LocalTerminalCwdResolver : ILocalTerminalCwdResolver
{
    private readonly Func<string> _getUserHome;

    public LocalTerminalCwdResolver(Func<string>? getUserHome = null)
    {
        _getUserHome = getUserHome ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public string Resolve(bool isLocalSession, string? sessionInfoCwd)
    {
        if (isLocalSession && !string.IsNullOrWhiteSpace(sessionInfoCwd))
        {
            return sessionInfoCwd.Trim();
        }

        return _getUserHome();
    }
}
