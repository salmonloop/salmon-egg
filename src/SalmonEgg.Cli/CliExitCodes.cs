namespace SalmonEgg.Cli;

/// <summary>
/// Stable process exit-code contract for the CLI host.
/// </summary>
public static class CliExitCodes
{
    /// <summary>
    /// The invocation succeeded, including built-in help and version output.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// The CLI host or a command handler failed unexpectedly.
    /// </summary>
    public const int Failure = 1;

    /// <summary>
    /// Command-line input could not be parsed or validated.
    /// </summary>
    public const int Usage = 2;
}
