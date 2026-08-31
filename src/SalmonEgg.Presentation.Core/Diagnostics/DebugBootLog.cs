using System;
using System.Diagnostics;
using System.IO;

namespace SalmonEgg.Presentation.Core.Diagnostics;

/// <summary>
/// DEBUG-only boot fact writer shared by shell readiness and Skia Desktop GUI smoke.
/// Writes under <c>SALMONEGG_APPDATA_ROOT/boot.log</c> so gates can observe projection
/// state without AT-SPI or platform automation providers.
/// </summary>
public static class DebugBootLog
{
    private const string AppDataRootEnvVar = "SALMONEGG_APPDATA_ROOT";

    [Conditional("DEBUG")]
    public static void Write(string message)
    {
#if DEBUG
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var root = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            Directory.CreateDirectory(root);
            File.AppendAllText(
                Path.Combine(root, "boot.log"),
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Boot diagnostics must never affect product control flow.
        }
#endif
    }
}
