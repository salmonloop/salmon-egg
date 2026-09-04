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

    /// <summary>
    /// True when the app can tell whether its <c>salmon-egg</c> command is reachable from a shell. Needs a
    /// PATH to resolve and a process host to ask the resolved executable what version it is.
    /// </summary>
    bool SupportsCliCommandInspection { get; }

    /// <summary>
    /// True when the app, rather than an installer, owns the command's entry on PATH.
    /// </summary>
    /// <remarks>
    /// macOS only, and deliberately narrow. Everywhere else an installer writes the entry and removes it on
    /// uninstall; a second owner writing the same path would leave the loser of that race behind as a
    /// command pointing at a deleted app. macOS is the exception because a dragged .app has no install hook
    /// at all, so without this its users have no path to the command.
    /// </remarks>
    bool SupportsCliCommandLinking { get; }
}
