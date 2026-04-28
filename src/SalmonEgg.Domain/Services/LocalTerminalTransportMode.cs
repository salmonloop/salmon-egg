namespace SalmonEgg.Domain.Services;

/// <summary>
/// Describes how a local terminal session is transported to the UI host.
/// </summary>
public enum LocalTerminalTransportMode
{
    Pipe = 0,
    PseudoConsole = 1
}
