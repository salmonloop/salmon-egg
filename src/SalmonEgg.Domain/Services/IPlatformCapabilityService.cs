namespace SalmonEgg.Domain.Services;

public interface IPlatformCapabilityService
{
    bool SupportsLaunchOnStartup { get; }
    bool SupportsTray { get; }
    bool SupportsLanguageOverride { get; }
    bool SupportsMiniWindow { get; }
    bool SupportsStdioTransport { get; }
    bool SupportsLocalTerminal { get; }
}
