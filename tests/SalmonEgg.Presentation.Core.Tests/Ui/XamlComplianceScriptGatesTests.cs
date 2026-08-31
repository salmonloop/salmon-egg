using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

public sealed class XamlComplianceScriptGatesTests
{
    [Fact]
    public void WinUiMsixScript_RestoresAllReferenceProjectsUsedByApp()
    {
        var script = LoadText(@".tools\run-winui3-msix.ps1");

        Assert.Contains(
            "'src\\SalmonEgg.Infrastructure.Desktop\\SalmonEgg.Infrastructure.Desktop.csproj'",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WinUiXamlCompiler_UsesSdkProvidedMetadataWithoutCustomReferenceInjection()
    {
        var project = LoadText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.DoesNotContain("AddWinSdkXamlReferences", project, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlReferencesToCompile Include=", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WinUI.dll", project, StringComparison.Ordinal);
        Assert.DoesNotContain("microsoft.windowsappsdk.interactiveexperiences", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreGateScript_StopsWhenNativeGateCommandFails()
    {
        var script = LoadText(@"scripts\gates\run-core-gates.ps1");

        Assert.Contains("Invoke-GateCommand", script, StringComparison.Ordinal);
        Assert.Contains("$LASTEXITCODE -ne 0", script, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionGuiRegressionScript_CoversInstalledMsixRightPanelAuxiliaryPanelPath()
    {
        var script = LoadText(@".tools\run-session-gui-regression.ps1");

        Assert.Contains(
            ".tools\\run-winui3-msix.ps1",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-SkipInstall", script, StringComparison.Ordinal);
        Assert.Contains(
            "--filter-method",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.AuxiliaryPanels_AfterCloseAndReopen_RetainContentInsteadOfBlankSurface",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WinUiMsixScript_ClearsDebugEnvironmentOverridesBeforeLaunch()
    {
        var script = LoadText(@".tools\run-winui3-msix.ps1");

        Assert.Contains("Clear-SalmonEggDebugEnvironmentOverrides", script, StringComparison.Ordinal);
        Assert.Contains("SALMONEGG_APPDATA_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("'SALMONEGG_GUI'", script, StringComparison.Ordinal);
        Assert.Contains("[EnvironmentVariableTarget]::User", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryBuildProps_DoesNotSuppressUno0001()
    {
        var props = LoadText(@"SalmonEgg\Directory.Build.props");

        Assert.DoesNotContain("UNO0001", props);
    }
}
