namespace SalmonEgg.Domain.Services;

public interface IPlatformCapabilityService
{
    bool SupportsLaunchOnStartup { get; }
    bool SupportsTray { get; }
    bool SupportsLanguageOverride { get; }
    bool SupportsMiniWindow { get; }
    bool SupportsExternalFileOpen { get; }
    bool SupportsLocalFileExport { get; }
    bool SupportsStdioTransport { get; }
    bool SupportsInteractiveTerminalSurface { get; }
    bool SupportsLocalTerminal { get; }
    bool SupportsGamepadInput { get; }
}
