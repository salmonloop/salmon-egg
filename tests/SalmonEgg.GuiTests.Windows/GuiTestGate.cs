using System;

namespace SalmonEgg.GuiTests.Windows;

internal static class GuiTestGate
{
    private const string EnableEnvVar = "SALMONEGG_GUI";

    public static void RequireEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable(EnableEnvVar);
        Skip.IfNot(
            string.Equals(enabled, "1", StringComparison.Ordinal),
            $"Windows GUI smoke tests are opt-in. Set {EnableEnvVar}=1 after installing/running the MSIX build.");
    }
}
