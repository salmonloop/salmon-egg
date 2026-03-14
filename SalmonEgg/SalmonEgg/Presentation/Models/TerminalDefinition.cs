namespace SalmonEgg.Presentation.Models;

public sealed class TerminalDefinition
{
    public TerminalDefinition(string id, string displayName, string executablePath, string? arguments = null)
    {
        Id = id;
        DisplayName = displayName;
        ExecutablePath = executablePath;
        Arguments = arguments;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string ExecutablePath { get; }
    public string? Arguments { get; }
}
