namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface ILocalTerminalCwdResolver
{
    string Resolve(bool isLocalSession, string? sessionInfoCwd);
}
