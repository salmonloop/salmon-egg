using System;
using System.IO;
using System.Threading.Tasks;

namespace SalmonEgg.Cli.Output;

/// <summary>
/// Presents command results and diagnostics at the CLI boundary.
/// </summary>
/// <remarks>
/// System.CommandLine owns its automatic help/version/parse output. Application command handlers
/// use this abstraction instead of Console so later output formats can be introduced without
/// binding command behavior to process-global streams.
/// </remarks>
public interface ICliOutput
{
    /// <summary>
    /// Writes a normal command result.
    /// </summary>
    /// <param name="message">The text to write.</param>
    Task WriteAsync(string message);

    /// <summary>
    /// Writes a diagnostic or failure message.
    /// </summary>
    /// <param name="message">The text to write.</param>
    Task WriteErrorAsync(string message);
}

/// <summary>
/// Text-only CLI output implementation.
/// </summary>
public sealed class TextCliOutput : ICliOutput
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public TextCliOutput(TextWriter stdout, TextWriter stderr)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
    }

    public Task WriteAsync(string message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        return _stdout.WriteLineAsync(message);
    }

    public Task WriteErrorAsync(string message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        return _stderr.WriteLineAsync(message);
    }
}
