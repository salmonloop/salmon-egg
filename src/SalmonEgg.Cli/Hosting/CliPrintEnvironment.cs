using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;

namespace SalmonEgg.Cli.Hosting;

/// <summary>
/// The CLI's entry into the environment-printing protocol.
/// </summary>
/// <remarks>
/// The protocol itself lives in <see cref="DesktopPrintEnvironment"/>, beside the capture that reads it.
/// Two executables answer this mode — this CLI and the desktop app — and the app's search-path capture
/// invokes whichever one it can find, so the option's spelling and the payload's shape belong with the
/// reader rather than being restated in each writer.
///
/// This type remains because the CLI has its own exit-code contract to map onto, which the protocol has no
/// business knowing.
/// </remarks>
internal static class CliPrintEnvironment
{
    /// <summary>The option that selects this mode. Undocumented: it is an app-internal protocol.</summary>
    internal const string OptionName = DesktopPrintEnvironment.OptionName;

    /// <inheritdoc cref="DesktopPrintEnvironment.TryGetMarker"/>
    internal static bool TryGetMarker(IReadOnlyList<string> args, out string marker)
        => DesktopPrintEnvironment.TryGetMarker(args, out marker);

    /// <summary>
    /// Prints the environment and returns the CLI's success code.
    /// </summary>
    internal static async Task<int> WriteAsync(
        string marker,
        TextWriter stdout,
        CancellationToken cancellationToken = default)
    {
        await DesktopPrintEnvironment.WriteAsync(marker, stdout, cancellationToken).ConfigureAwait(false);
        return CliExitCodes.Success;
    }
}
