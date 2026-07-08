using System;
using SalmonEgg.Presentation.Core.Tests;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class GuiSmokeGateContractTests
{
    [Fact]
    public void SkiaDesktopGuiSmokeGate_UsesRealDesktopBuildAndDebugReadinessProbe()
    {
        var script = TestSourceFiles.ReadAllText(
            @"scripts\gates\run-skia-desktop-gui-smoke-gates.sh");
        var x11Probe = TestSourceFiles.ReadAllText(
            @"scripts\gates\skia-desktop-x11-window-probe.py");

        Assert.Contains("-f net10.0-desktop", script, StringComparison.Ordinal);
        Assert.Contains("SalmonEggTargetFrameworks=net10.0-desktop", script, StringComparison.Ordinal);
        Assert.Contains("SalmonEggAllTargetFrameworks=net10.0-desktop", script, StringComparison.Ordinal);
        Assert.Contains("APP_PATH=\"${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-desktop/SalmonEgg\"", script, StringComparison.Ordinal);
        Assert.Contains("SALMONEGG_GUI=1", script, StringComparison.Ordinal);
        Assert.Contains("SALMONEGG_APPDATA_ROOT=\"${APPDATA_ROOT}\"", script, StringComparison.Ordinal);
        Assert.Contains("READY_MARKER=\"MainPage: initial shell content activated\"", script, StringComparison.Ordinal);
        Assert.Contains("X11_PROBE=\"${REPO_ROOT}/scripts/gates/skia-desktop-x11-window-probe.py\"", script, StringComparison.Ordinal);
        Assert.Contains("--min-distinct-pixels", x11Probe, StringComparison.Ordinal);
        Assert.Contains("--require-focus-input", script, StringComparison.Ordinal);
        Assert.Contains("libXtst.so.6", x11Probe, StringComparison.Ordinal);
        Assert.Contains("XTestFakeKeyEvent", x11Probe, StringComparison.Ordinal);
        Assert.Contains("Install the XTest runtime", x11Probe, StringComparison.Ordinal);
        Assert.Contains("Skia Desktop GUI smoke did not expose a mapped, nonblank X11 window.", script, StringComparison.Ordinal);
        Assert.Contains("Skia Desktop GUI smoke did not expose a focusable X11 window that accepts synthetic keyboard input.", script, StringComparison.Ordinal);
        Assert.Contains("Skia Desktop GUI smoke requires Debug configuration", script, StringComparison.Ordinal);
        Assert.Contains("Xvfb", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tests/SalmonEgg.GuiTests.Windows", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGuide_SeparatesPlatformGuiSmokeDrivers()
    {
        var guide = TestSourceFiles.ReadAllText(@"BUILD_GUIDE.md");

        Assert.Contains("scripts/gates/run-skia-desktop-gui-smoke-gates.sh Debug", guide, StringComparison.Ordinal);
        Assert.Contains("scripts/gates/run-gui-smoke-gates.ps1", guide, StringComparison.Ordinal);
        Assert.Contains("scripts/gates/run-wasm-smoke-gates.sh Debug", guide, StringComparison.Ordinal);
        Assert.Contains("FlaUI/UIA3", guide, StringComparison.Ordinal);
        Assert.Contains("Playwright/Chromium", guide, StringComparison.Ordinal);
        Assert.Contains("net10.0-desktop", guide, StringComparison.Ordinal);
        Assert.Contains("libx11-6", guide, StringComparison.Ordinal);
        Assert.Contains("libxtst6", guide, StringComparison.Ordinal);
        Assert.Contains("AT-SPI", guide, StringComparison.Ordinal);
        Assert.Contains("AutomationId", guide, StringComparison.Ordinal);
        Assert.Contains("X11 window", guide, StringComparison.Ordinal);
        Assert.Contains("test hook", guide, StringComparison.Ordinal);
    }
}
