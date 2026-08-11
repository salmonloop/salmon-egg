using System.Xml.Linq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Application.Services.Acp;

namespace SalmonEgg.Presentation.Core.Tests;

public sealed class WasmStartupAssetsTests
{
    [Fact]
    public void WasmAppManifest_UsesSalmonEggSplashAsset()
    {
        var manifest = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\AppManifest.js");

        Assert.Contains("displayName: \"SalmonEgg\"", manifest, StringComparison.Ordinal);
        Assert.Contains("splashScreenImage: \"splash_screen.scale-200.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("splashScreenColor: \"#ffffff\"", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uno-assets.platform.uno", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_DeclaresSingleUnoSplashScreenSource()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));
        var splashScreenFile = project.Descendants("UnoSplashScreenFile").Single();
        var splashScreenBaseSize = project.Descendants("UnoSplashScreenBaseSize").Single();
        var splashScreenColor = project.Descendants("UnoSplashScreenColor").Single();

        Assert.Empty(project.Descendants("UnoSplashScreen"));
        Assert.Equal(@"Assets\Icons\splash_screen.png", splashScreenFile.Value);
        Assert.Equal("256,256", splashScreenBaseSize.Value);
        Assert.Equal("#FFFFFF", splashScreenColor.Value);
        Assert.True(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Assets\Icons\splash_screen.png")));
        Assert.False(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Assets\Splash\splash_screen.svg")));
    }

    [Fact]
    public void Project_KeepsWasmCulturesCanonical()
    {
        var browserWasmPropertyGroup = LoadBrowserWasmPropertyGroup();

        var languages = browserWasmPropertyGroup.Element("SatelliteResourceLanguages")?.Value;
        var expectedLanguages = string.Join(';', AppLanguageCatalog.SupportedResourceLanguageTags);

        Assert.Equal(expectedLanguages, languages);
        Assert.DoesNotContain(";zh;", languages, StringComparison.Ordinal);
        Assert.DoesNotContain("zh-CN", languages, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_EnablesIndexedDbBackedWasmFileSystem()
    {
        var browserWasmPropertyGroup = LoadBrowserWasmPropertyGroup();

        Assert.Equal("true", browserWasmPropertyGroup.Element("WasmShellEnableIDBFS")?.Value);
        Assert.Equal("true", browserWasmPropertyGroup.Element("AllowUnsafeBlocks")?.Value);
    }

    [Fact]
    public void RuntimeLanguageService_UsesUnoPlatformOverrideAtApplicationBoundary()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var languageService = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\UnoAppLanguageService.cs");
        var stringLocalizer = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\UnoCoreStringLocalizer.cs");
        var cultureService = LoadFile(@"src\SalmonEgg.Infrastructure\Services\AppCultureService.cs");
        var project = LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.Contains("AddSingleton<IAppLanguageService, UnoAppLanguageService>()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IStringLocalizer<CoreStrings>, UnoCoreStringLocalizer>()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride", languageService, StringComparison.Ordinal);
        Assert.Contains("Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride", languageService, StringComparison.Ordinal);
        Assert.Contains("_uiDispatcher.EnqueueAsync", languageService, StringComparison.Ordinal);
        Assert.Contains("ResourceLoader.GetForViewIndependentUse(\"CoreStrings\")", stringLocalizer, StringComparison.Ordinal);
        Assert.Contains("PrepareCoreStringPriResources", project, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"UnoResourcesGeneration;BeforeGenerateProjectPriFile\"", project, StringComparison.Ordinal);
        Assert.Contains(@"Link=""Strings\en-US\CoreStrings.resw""", project, StringComparison.Ordinal);
        Assert.Contains("MSIX resources.pri does not contain the required 'CoreStrings' ResourceMap.", LoadFile(@".tools\run-winui3-msix.ps1"), StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeInformation.IsOSPlatform", cultureService, StringComparison.Ordinal);
        Assert.DoesNotContain("#if WINDOWS", cultureService, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_IncludesWasmFileSystemPersistenceInterop()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));

        var nativeFileReference = project
            .Descendants("WasmShellNativeFileReference")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-storage.js",
                StringComparison.Ordinal));
        Assert.Equal("'$(TargetFramework)' == 'net10.0-browserwasm'", (string?)nativeFileReference.Parent?.Attribute("Condition"));
        var contentReference = project
            .Descendants("Content")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-storage.js",
                StringComparison.Ordinal));
        Assert.Equal("_framework/salmon-egg-wasm-storage.js", contentReference.Element("TargetPath")?.Value);
        Assert.Equal("PreserveNewest", contentReference.Element("CopyToOutputDirectory")?.Value);

        var script = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-storage.js");
        Assert.Contains("syncFileSystem(true)", script, StringComparison.Ordinal);
        Assert.Contains("syncFileSystem(false)", script, StringComparison.Ordinal);
        Assert.Contains("globalThis.Windows?.Storage?.StorageFolder", script, StringComparison.Ordinal);
        Assert.Contains("synchronizeFileSystem(populateFromBackingStore", script, StringComparison.Ordinal);
        Assert.DoesNotContain("globalThis.Module", script, StringComparison.Ordinal);
        Assert.DoesNotContain("globalThis.FS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("getLocationProtocol", script, StringComparison.Ordinal);
        Assert.DoesNotContain("location.protocol", script, StringComparison.Ordinal);

        var persistence = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmFileSystemPersistence.cs");
        // ImportAsync 的第二个参数会直接喂给浏览器 import()，必须是相对/绝对 URL，
        // 且在 Uno 的 package_<hash> 打包前缀下需要跟随 authoritative app base。
        Assert.Contains(".ImportAsync(StorageModuleName, StorageModuleUrl", persistence, StringComparison.Ordinal);
        Assert.Contains("ResolveStorageModuleUrl()", persistence, StringComparison.Ordinal);
        Assert.Contains("UNO_BOOTSTRAP_APP_BASE", persistence, StringComparison.Ordinal);
        Assert.Contains("_framework/{StorageModuleName}", persistence, StringComparison.Ordinal);
        Assert.Contains("ApplicationData.Current.LocalFolder.CreateFolderAsync(\"SalmonEgg\"", persistence, StringComparison.Ordinal);
        Assert.Contains("EnsureStorageModuleImportedAsync", persistence, StringComparison.Ordinal);

        var endpointContext = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmTransportEndpointAccessContext.cs");
        Assert.Contains("JSHost.GlobalThis", endpointContext, StringComparison.Ordinal);
        Assert.DoesNotContain("JSImport(\"getLocationProtocol\"", endpointContext, StringComparison.Ordinal);

        var paths = LoadFile(@"src\SalmonEgg.Infrastructure\Storage\SalmonEggPaths.cs");
        Assert.Contains("OperatingSystem.IsBrowser()", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("#elif __WASM__", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("__ANDROID__", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("__IOS__", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("Android.App.Application", paths, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserWasmShellService_UsesBrowserClipboardInterop()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));

        var nativeFileReference = project
            .Descendants("WasmShellNativeFileReference")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-shell.js",
                StringComparison.Ordinal));
        Assert.Equal("'$(TargetFramework)' == 'net10.0-browserwasm'", (string?)nativeFileReference.Parent?.Attribute("Condition"));
        var contentReference = project
            .Descendants("Content")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-shell.js",
                StringComparison.Ordinal));
        Assert.Equal("_framework/salmon-egg-wasm-shell.js", contentReference.Element("TargetPath")?.Value);
        Assert.Equal("PreserveNewest", contentReference.Element("CopyToOutputDirectory")?.Value);

        var shellService = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmPlatformShellService.cs");
        Assert.Contains("System.Runtime.InteropServices.JavaScript", shellService, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"browser\")]", shellService, StringComparison.Ordinal);
        Assert.Contains("JSHost.ImportAsync(ShellModuleName, ShellModuleUrl", shellService, StringComparison.Ordinal);
        Assert.Contains("[JSImport(\"copyToClipboard\", \"salmon-egg-wasm-shell.js\")]", shellService, StringComparison.Ordinal);
        Assert.Contains("[JSImport(\"readClipboardText\", \"salmon-egg-wasm-shell.js\")]", shellService, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyToClipboardAsync(string text) => _unsupported.CopyToClipboardAsync(text)", shellService, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadClipboardTextAsync() => _unsupported.ReadClipboardTextAsync()", shellService, StringComparison.Ordinal);

        var script = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-shell.js");
        Assert.Contains("navigator?.clipboard?.writeText", script, StringComparison.Ordinal);
        Assert.Contains("navigator?.clipboard?.readText", script, StringComparison.Ordinal);
        Assert.Contains("document.execCommand(\"copy\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserWasmBuild_RemovesDesktopProcessDependenciesFromInfrastructureGraph()
    {
        var appProject = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));
        var infrastructureProject = XDocument.Parse(LoadFile(@"src\SalmonEgg.Infrastructure\SalmonEgg.Infrastructure.csproj"));
        var runtimeProbe = LoadFile(@"src\SalmonEgg.Infrastructure\Services\PlatformRuntimeCapabilityProbe.cs");
        var capabilityService = LoadFile(@"src\SalmonEgg.Infrastructure\Services\PlatformCapabilityService.cs");

        var browserWasmPropertyGroup = LoadBrowserWasmPropertyGroup();
        var infrastructureReference = appProject
            .Descendants("ProjectReference")
            .Single(element => ((string?)element.Attribute("Include"))?.Contains("SalmonEgg.Infrastructure.csproj", StringComparison.Ordinal) == true);
        var desktopInfrastructureReferences = appProject
            .Descendants("ProjectReference")
            .Where(element => ((string?)element.Attribute("Include"))?.Contains("SalmonEgg.Infrastructure.Desktop.csproj", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal("BrowserWasm", browserWasmPropertyGroup.Element("SalmonEggPlatform")?.Value);
        Assert.Equal("false", browserWasmPropertyGroup.Element("SalmonEggSupportsDesktopProcessHost")?.Value);
        Assert.Contains("__WASM__", browserWasmPropertyGroup.Element("DefineConstants")?.Value, StringComparison.Ordinal);
        Assert.Null(infrastructureReference.Attribute("AdditionalProperties"));
        Assert.DoesNotContain(infrastructureProject.Descendants("PackageReference"), element => (string?)element.Attribute("Include") == "Porta.Pty");
        var desktopReference = Assert.Single(desktopInfrastructureReferences);
        Assert.Equal("'$(SalmonEggSupportsDesktopProcessHost)' != 'false'", (string?)desktopReference.Attribute("Condition"));
        Assert.Contains("OperatingSystem.IsBrowser()", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsAndroid()", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsIOS()", runtimeProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("__WASM__", runtimeProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("__ANDROID__", runtimeProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("__IOS__", runtimeProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("#if NET5_0_OR_GREATER", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("public bool SupportsLaunchOnStartup => IsWindowsDesktopProcessHost;", capabilityService, StringComparison.Ordinal);
        Assert.Contains("public bool SupportsTray => IsWindowsDesktopProcessHost;", capabilityService, StringComparison.Ordinal);
        Assert.Contains("public bool SupportsLanguageOverride => true;", capabilityService, StringComparison.Ordinal);
        Assert.Contains("public bool SupportsMiniWindow => IsWindowsDesktopProcessHost;", capabilityService, StringComparison.Ordinal);
        Assert.Contains("public bool SupportsGamepadInput => IsBrowserRuntime || IsWindowsDesktopProcessHost;", capabilityService, StringComparison.Ordinal);
        Assert.Contains("private static bool IsBrowserRuntime => OperatingSystem.IsBrowser();", capabilityService, StringComparison.Ordinal);
        Assert.DoesNotContain("#if NET5_0_OR_GREATER", capabilityService, StringComparison.Ordinal);
        Assert.Contains("private bool IsWindowsDesktopProcessHost => _runtimeProbe.IsDesktopProcessHost && _isOSPlatform(OSPlatform.Windows);", capabilityService, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchOnStartup_UsesPlatformBoundaryImplementationForWindows()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var unsupportedService = LoadFile(@"src\SalmonEgg.Infrastructure\Services\UnsupportedAppStartupService.cs");
        var windowsService = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\Windows\WindowsAppStartupService.cs");

        Assert.Contains("services.AddSingleton<IAppStartupService, WindowsAppStartupService>();", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IAppStartupService, UnsupportedAppStartupService>();", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("StartupTask.GetAsync", windowsService, StringComparison.Ordinal);
        Assert.Contains("#if WINDOWS", windowsService, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.ApplicationModel", unsupportedService, StringComparison.Ordinal);
        Assert.DoesNotContain("#if WINDOWS", unsupportedService, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ExposesMobileTargetsThroughOptInProperties()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));

        var enableMobileTargets = project.Descendants("EnableMobileTargets").Single();
        var enableAndroidTarget = project.Descendants("EnableAndroidTarget").Single();
        var enableIosTarget = project.Descendants("EnableIosTarget").Single();
        var androidTargets = project.Descendants("SalmonEggAndroidTargetFrameworks").Single();
        var iosTargets = project.Descendants("SalmonEggIosTargetFrameworks").Single();
        var mobileTargets = project.Descendants("SalmonEggMobileTargetFrameworks").ToArray();
        var useDefaultPublishRuntimeIdentifier = project.Descendants("UseDefaultPublishRuntimeIdentifier").Single();

        Assert.Equal("false", enableMobileTargets.Value);
        Assert.Equal("'$(EnableMobileTargets)' == ''", (string?)enableMobileTargets.Attribute("Condition"));
        Assert.Equal("true", enableAndroidTarget.Value);
        Assert.Equal("'$(EnableAndroidTarget)' == ''", (string?)enableAndroidTarget.Attribute("Condition"));
        Assert.Equal("false", enableIosTarget.Value);
        Assert.Equal("'$(EnableIosTarget)' == ''", (string?)enableIosTarget.Attribute("Condition"));

        Assert.Equal("net10.0-android36.0", androidTargets.Value);
        Assert.Contains("'$(EnableMobileTargets)' == 'true'", (string?)androidTargets.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("'$(EnableAndroidTarget)' == 'true'", (string?)androidTargets.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("'$(AndroidSdkDirectory)' != ''", (string?)androidTargets.Attribute("Condition"), StringComparison.Ordinal);

        Assert.Equal("net10.0-ios", iosTargets.Value);
        Assert.Contains("'$(EnableMobileTargets)' == 'true'", (string?)iosTargets.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("'$(EnableIosTarget)' == 'true'", (string?)iosTargets.Attribute("Condition"), StringComparison.Ordinal);

        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggAndroidTargetFrameworks)");
        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggMobileTargetFrameworks);$(SalmonEggIosTargetFrameworks)");
        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggIosTargetFrameworks)");

        Assert.Equal("false", useDefaultPublishRuntimeIdentifier.Value);
        var androidRuntimeGroupCondition = (string?)useDefaultPublishRuntimeIdentifier.Parent?.Attribute("Condition");
        Assert.Contains("GetTargetPlatformIdentifier", androidRuntimeGroupCondition, StringComparison.Ordinal);
        Assert.Contains("android", androidRuntimeGroupCondition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlatformBuildGate_IsolatesMobileRestoreGraphsAndPinsIosToolchain()
    {
        var gate = LoadFile(@".github\workflows\platform-build-gates.yml");

        Assert.Contains("sdkmanager_status=${PIPESTATUS[1]}", gate, StringComparison.Ordinal);
        Assert.Contains("if [ \"${sdkmanager_status}\" -ne 0 ]; then", gate, StringComparison.Ordinal);
        Assert.Contains("dotnet restore SalmonEgg/SalmonEgg/SalmonEgg.csproj", gate, StringComparison.Ordinal);
        Assert.Equal(
            2,
            gate.Split("-p:SalmonEggTargetFrameworks=net10.0-android36.0", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            gate.Split("-p:SalmonEggTargetFrameworks=net10.0-ios", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("-p:TargetFrameworks=", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:TargetFramework=", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("--runtime android-", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:RuntimeIdentifier=android-", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:RuntimeIdentifiers=android-", gate, StringComparison.Ordinal);
        Assert.Equal(4, gate.Split("-p:SalmonEggSupportsDesktopProcessHost=false", StringSplitOptions.None).Length - 1);
        Assert.Contains("--runtime iossimulator-arm64", gate, StringComparison.Ordinal);
        Assert.Contains("--no-restore", gate, StringComparison.Ordinal);
        Assert.Contains("DOTNET_VERSION: \"10.0.3xx\"", gate, StringComparison.Ordinal);
        Assert.Equal(
            4,
            gate.Split("dotnet-version: ${{ env.DOTNET_VERSION }}", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("global-json-file:", gate, StringComparison.Ordinal);
        Assert.Equal(2, gate.Split("dotnet workload list", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, gate.Split("10.0.3??", StringSplitOptions.None).Length - 1);
        Assert.True(
            gate.IndexOf("Verify Android .NET SDK", StringComparison.Ordinal)
            < gate.IndexOf("Install Android workload", StringComparison.Ordinal));
        Assert.True(
            gate.IndexOf("Verify iOS .NET SDK", StringComparison.Ordinal)
            < gate.IndexOf("Install iOS workload", StringComparison.Ordinal));
        Assert.Contains("runs-on: macos-15", gate, StringComparison.Ordinal);
        Assert.Contains("os.path.realpath(\"/Applications/Xcode_26.0.app/Contents/Developer\")", gate, StringComparison.Ordinal);
        Assert.Contains("sudo xcode-select --switch \"${xcode_developer_dir}\"", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("xcode-select --switch /Applications/Xcode_26.0.app/Contents/Developer", gate, StringComparison.Ordinal);
        Assert.Contains("xcodebuild -version", gate, StringComparison.Ordinal);
        Assert.Contains("26.0*)", gate, StringComparison.Ordinal);
        Assert.Contains("xcrun --sdk macosx --show-sdk-path", gate, StringComparison.Ordinal);
        Assert.Contains("xcrun --sdk iphonesimulator --show-sdk-path", gate, StringComparison.Ordinal);
        Assert.Contains("xcrun --sdk macosx --find actool", gate, StringComparison.Ordinal);

        var canonicalizeXcode = gate.IndexOf("os.path.realpath", StringComparison.Ordinal);
        var selectXcode = gate.IndexOf("sudo xcode-select --switch", StringComparison.Ordinal);
        var verifyMacosSdk = gate.IndexOf("xcrun --sdk macosx --show-sdk-path", StringComparison.Ordinal);
        var verifyIosSimulatorSdk = gate.IndexOf("xcrun --sdk iphonesimulator --show-sdk-path", StringComparison.Ordinal);
        var verifyActool = gate.IndexOf("xcrun --sdk macosx --find actool", StringComparison.Ordinal);
        var installIosWorkload = gate.IndexOf("Install iOS workload", StringComparison.Ordinal);
        Assert.True(canonicalizeXcode < selectXcode);
        Assert.True(selectXcode < verifyMacosSdk);
        Assert.True(verifyMacosSdk < verifyIosSimulatorSdk);
        Assert.True(verifyIosSimulatorSdk < verifyActool);
        Assert.True(verifyActool < installIosWorkload);
    }

    [Fact]
    public void DotnetWorkflows_PinRepositorySdkFeatureBand()
    {
        var globalJson = LoadFile("global.json");
        var wasmSmokeGate = LoadFile(@"scripts\gates\run-wasm-smoke-gates.sh");
        var linuxGamepadGate = LoadFile(@"scripts\gates\run-linux-gamepad-smoke-gates.sh");
        string[] workflowPaths =
        [
            @".github\workflows\ci-acp-sdk.yml",
            @".github\workflows\ci-core.yml",
            @".github\workflows\code-quality.yml",
            @".github\workflows\gui-smoke-gates.yml",
            @".github\workflows\platform-build-gates.yml",
            @".github\workflows\release-packaging.yml",
            @".github\workflows\wasm-smoke-gates.yml"
        ];

        Assert.Contains("\"version\": \"10.0.302\"", globalJson, StringComparison.Ordinal);
        Assert.Contains("\"rollForward\": \"latestPatch\"", globalJson, StringComparison.Ordinal);
        foreach (var workflowPath in workflowPaths)
        {
            var workflow = LoadFile(workflowPath);
            Assert.Contains("10.0.3xx", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("10.0.x", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("global-json-file:", workflow, StringComparison.Ordinal);
        }

        foreach (var gate in new[] { wasmSmokeGate, linuxGamepadGate })
        {
            Assert.Contains("resolve_selected_dotnet_sdk_root()", gate, StringComparison.Ordinal);
            Assert.Contains("\"${DOTNET_BIN}\" --list-sdks", gate, StringComparison.Ordinal);
            Assert.Contains("selected_version=\"${selected_version//$'\\r'/}\"", gate, StringComparison.Ordinal);
            Assert.Contains("sdk_list=\"${sdk_list//$'\\r'/}\"", gate, StringComparison.Ordinal);
            Assert.Contains("${DOTNET_SDK_ROOT}/Sdks/Microsoft.NET.Sdk.WebAssembly/hotreload/net10.0", gate, StringComparison.Ordinal);
            Assert.DoesNotContain("/sdk/10.0.302", gate, StringComparison.Ordinal);
            Assert.DoesNotContain("${DOTNET_ROOT}/sdk/${DOTNET_SDK_VERSION}", gate, StringComparison.Ordinal);
            Assert.True(
                gate.IndexOf("selected_version=\"${selected_version//$'\\r'/}\"", StringComparison.Ordinal)
                < gate.IndexOf("while read -r sdk_version sdk_parent; do", StringComparison.Ordinal));
            Assert.True(
                gate.IndexOf("sdk_list=\"${sdk_list//$'\\r'/}\"", StringComparison.Ordinal)
                < gate.IndexOf("while read -r sdk_version sdk_parent; do", StringComparison.Ordinal));
            AssertWasmRestoreCleanBuildOrder(gate);
        }
    }

    [Fact]
    public void WasmCapabilityBoundaryGate_RequiresStableV1InitializeShape()
    {
        var gate = LoadFile(@"scripts\gates\wasm-capability-boundary-smoke.mjs");

        Assert.Contains("params.protocolVersion !== 1", gate, StringComparison.Ordinal);
        Assert.Contains("Production initialize must negotiate stable ACP protocolVersion 1", gate, StringComparison.Ordinal);
        Assert.Contains("params.clientInfo", gate, StringComparison.Ordinal);
        Assert.Contains("params.clientCapabilities", gate, StringComparison.Ordinal);
        Assert.Contains("must not include ACP v2 info/capabilities fields", gate, StringComparison.Ordinal);
        Assert.Contains("WASM client must not advertise ACP fs capability", gate, StringComparison.Ordinal);
        Assert.Contains("WASM client must not advertise ACP terminal capability", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("params.protocolVersion === 2", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpPackageConsumerGate_SerializesStableV1InitializeShape()
    {
        var gate = LoadFile(@"scripts\gates\run-acp-consumer-package-smoke.sh");

        Assert.Contains("JsonSerializer.Serialize(initialize, AcpJsonContext.Default.InitializeParams)", gate, StringComparison.Ordinal);
        Assert.Contains("initialize.ProtocolVersion == AcpProtocolVersion.V1", gate, StringComparison.Ordinal);
        Assert.Contains("initializeRoot.GetProperty(\"protocolVersion\").GetInt32() == AcpProtocolVersion.V1", gate, StringComparison.Ordinal);
        Assert.Contains("initializeRoot.TryGetProperty(\"clientInfo\"", gate, StringComparison.Ordinal);
        Assert.Contains("initializeRoot.TryGetProperty(\"clientCapabilities\"", gate, StringComparison.Ordinal);
        Assert.Contains("!initializeRoot.TryGetProperty(\"info\"", gate, StringComparison.Ordinal);
        Assert.Contains("!initializeRoot.TryGetProperty(\"capabilities\"", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void LanguageReload_ReplacesRootFrameBeforeNavigatingToMainPage()
    {
        var app = LoadFile(@"SalmonEgg\SalmonEgg\App.xaml.cs");

        var createFrame = app.IndexOf("var replacementFrame = new Frame { AllowDrop = false };", StringComparison.Ordinal);
        var replaceRoot = app.IndexOf("window.Content = replacementFrame;", StringComparison.Ordinal);
        var navigateShell = app.IndexOf("replacementFrame.Navigate(typeof(MainPage)", StringComparison.Ordinal);

        Assert.True(createFrame >= 0, "Language reload must create a fresh root Frame.");
        Assert.True(replaceRoot > createFrame, "The fresh Frame must replace the loaded root.");
        Assert.True(navigateShell > replaceRoot, "MainPage must be recreated inside the replacement Frame.");
    }

    [Fact]
    public void MobileTargetGate_VerifiesTargetExpansionAndAndroidSecureStorageSource()
    {
        var gate = LoadFile(@"scripts\gates\verify-mobile-target-contracts.sh");

        Assert.Contains("-getProperty:TargetFrameworks", gate, StringComparison.Ordinal);
        Assert.Contains("-p:EnableMobileTargets=true -p:EnableIosTarget=true", gate, StringComparison.Ordinal);
        Assert.Contains("-p:SalmonEggTargetFrameworks=net10.0-ios", gate, StringComparison.Ordinal);
        Assert.Contains("-p:SalmonEggTargetFrameworks=net10.0-android36.0", gate, StringComparison.Ordinal);
        Assert.Contains("-t:GenerateRestoreGraphFile", gate, StringComparison.Ordinal);
        Assert.Contains("restore-graph.json", gate, StringComparison.Ordinal);
        Assert.Contains("android-restore-graph.json", gate, StringComparison.Ordinal);
        Assert.Contains("-p:Configuration=Release", gate, StringComparison.Ordinal);
        Assert.Contains("-p:NETCoreSdkPortableRuntimeIdentifier=linux-x64", gate, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App.Runtime.Mono.linux-x64", gate, StringComparison.Ordinal);
        Assert.Contains("SalmonEggTargetFrameworks=net10.0-desktop", gate, StringComparison.Ordinal);
        Assert.Contains("SalmonEggSupportsDesktopProcessHost=false", gate, StringComparison.Ordinal);
        Assert.Contains("SalmonEgg.Presentation.Core/SalmonEgg.Presentation.Core.csproj", gate, StringComparison.Ordinal);
        Assert.Contains("restricted-platform restore graph must exclude SalmonEgg.Infrastructure.Desktop", gate, StringComparison.Ordinal);
        Assert.Contains("net10.0-android36.0;net10.0-ios", gate, StringComparison.Ordinal);
        Assert.Contains("-define:__ANDROID__", gate, StringComparison.Ordinal);
        Assert.Contains("AndroidKeyStoreSecureStorage.cs", gate, StringComparison.Ordinal);
        Assert.Contains("Android ref pack or Roslyn compiler not available; skipped Android source compile", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_RegistersUnsupportedTerminalManagerForBrowserWasm()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("#if __WASM__", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IPlatformShellService, WasmPlatformShellService>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IPlatformRuntimeCapabilityProbe, RestrictedRuntimeCapabilityProbe>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IGamepadInputService, WasmGamepadInputService>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IGamepadDiagnosticsService, WasmGamepadDiagnosticsService>();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IGamepadNativeInputBridge", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WasmGamepadNativeInputBridge", code, StringComparison.Ordinal);
        Assert.Contains("#elif __ANDROID__ || __IOS__", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ITerminalSessionManager, UnsupportedTerminalSessionManager>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IStdioTransportFactory, UnsupportedStdioTransportFactory>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IPlatformShellService, UnsupportedPlatformShellService>();", code, StringComparison.Ordinal);

        var restrictedProbe = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\RestrictedRuntimeCapabilityProbe.cs");
        Assert.Contains("#if __WASM__ || __ANDROID__ || __IOS__", restrictedProbe, StringComparison.Ordinal);
        Assert.Contains("public bool IsDesktopProcessHost => false;", restrictedProbe, StringComparison.Ordinal);
        Assert.Contains("public bool HasExternalFileOpener => false;", restrictedProbe, StringComparison.Ordinal);
        Assert.Contains("public bool HasInteractiveTerminalSurface => false;", restrictedProbe, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationCore_DoesNotContainPlatformConditionalCompilation()
    {
        var root = RepoPath(@"src\SalmonEgg.Presentation.Core");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var code = File.ReadAllText(file);

            Assert.DoesNotContain("__WASM__", code, StringComparison.Ordinal);
            Assert.DoesNotContain("#if WINDOWS", code, StringComparison.Ordinal);
            Assert.DoesNotContain("#elif WINDOWS", code, StringComparison.Ordinal);
            Assert.DoesNotContain("__ANDROID__", code, StringComparison.Ordinal);
            Assert.DoesNotContain("__IOS__", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BrowserWasmGamepadServices_UseNativeBrowserGamepadApiBehindPlatformBoundary()
    {
        var reader = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadSnapshotReader.cs");
        var inputService = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadInputService.cs");
        var diagnosticsService = LoadFile(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadDiagnosticsService.cs");
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));

        Assert.Contains("#if __WASM__", reader, StringComparison.Ordinal);
        Assert.Contains("[JSImport(\"globalThis.navigator.getGamepads\")]", reader, StringComparison.Ordinal);
        Assert.Contains("SafeGetString(gamepad, \"mapping\")", reader, StringComparison.Ordinal);
        Assert.Contains("SafeGetString(gamepad, \"id\")", reader, StringComparison.Ordinal);
        Assert.Contains("BrowserGamepadIdentityParser.Parse", reader, StringComparison.Ordinal);
        Assert.Contains("BrowserStandardGamepadPressedButtons.GetPressedNames", reader, StringComparison.Ordinal);
        Assert.Contains("PressedButtons: device.PressedButtons", reader, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerFaceButtonLayoutResolver.Resolve", reader, StringComparison.Ordinal);
        Assert.Contains("BrowserGamepadInputReadingMapper.GetInputReading", reader, StringComparison.Ordinal);
        Assert.Contains("StandardGamepadInputReadingMapper.GetInputReading", LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Input\BrowserGamepadInputReadingMapper.cs"), StringComparison.Ordinal);
        // 平台 host 只采集 reading；ActiveIntents 由 Core projector 单一拥有（thin host 架构）。
        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadIntentProcessor.GetActiveIntents", reader, StringComparison.Ordinal);
        Assert.Contains(
            "GamepadIntentProcessor.GetActiveIntents",
            LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadDiagnosticsActiveReadingProjector.cs"),
            StringComparison.Ordinal);
        // InputSource 由 projector 选择结果给出，不再在 WASM reader 内硬编码。
        Assert.Contains("InputSource: active.InputSource", reader, StringComparison.Ordinal);
        Assert.Contains("ConnectedRawControllerCount: 0", reader, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"browser\")]", inputService, StringComparison.Ordinal);
        Assert.Contains("WasmGamepadSnapshotReader.ReadInputReadings()", inputService, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"browser\")]", diagnosticsService, StringComparison.Ordinal);
        Assert.Contains("WasmGamepadSnapshotReader.ReadSnapshot()", diagnosticsService, StringComparison.Ordinal);
        Assert.False(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadNativeInputBridge.cs")));
        Assert.False(File.Exists(RepoPath(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-gamepad.js")));
        Assert.DoesNotContain(project.Descendants("WasmShellNativeFileReference"), element => string.Equals(
            (string?)element.Attribute("Include"),
            @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-gamepad.js",
            StringComparison.Ordinal));
        Assert.DoesNotContain(project.Descendants("Content"), element =>
            string.Equals((string?)element.Attribute("Include"), @"Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-gamepad.js", StringComparison.Ordinal)
            || string.Equals(element.Element("TargetPath")?.Value, "_framework/salmon-egg-wasm-gamepad.js", StringComparison.Ordinal));
        Assert.DoesNotContain("Windows.Gaming.Input", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_UsesWasmPersistenceAndPlainTextSecureStorage()
    {
        // Scoped to what is WASM-specific: the browser has no platform secret store, so this head
        // must land on the plaintext one. The platform keychain matrix is asserted once, by
        // SecureStorageRegistrationContractTests; name-level only per §5.5.
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("WasmFileSystemPersistence", code, StringComparison.Ordinal);
        Assert.Contains("NoOpFileSystemPersistence", code, StringComparison.Ordinal);
        Assert.Contains("PlainTextFileSecureStorage", code, StringComparison.Ordinal);
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Storage\PlainTextFileSecureStorage.cs")));

        // The WASM branch must keep resolving ISecureStorage to the plaintext store; the contract
        // test covers the other platforms but not this one.
        var wasmBranch = ExtractWasmSecureStorageBranch(code);
        Assert.Contains("ISecureStorage", wasmBranch, StringComparison.Ordinal);
        Assert.Contains("PlainTextFileSecureStorage", wasmBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void VercelConfig_DeploysPublishedWwwrootAsStaticOutput()
    {
        var config = LoadFile("vercel.json");

        Assert.Contains("\"buildCommand\": \"bash scripts/vercel-build.sh\"", config, StringComparison.Ordinal);
        Assert.Contains("\"outputDirectory\": \"publish/vercel-wasm/wwwroot\"", config, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"/manifest.webmanifest\"", config, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"/service-worker.js\"", config, StringComparison.Ordinal);
    }

    [Fact]
    public void VercelBuildScript_RemovesVercelMetadataFromStaticOutput()
    {
        var script = LoadFile(@"scripts\vercel-build.sh");

        Assert.Contains("find \"${publish_dir}\" -type d -name .vercel -prune -exec rm -rf {} +", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VercelBuildScript_UsesDeterministicSingleNodePublish()
    {
        var script = LoadFile(@"scripts\vercel-build.sh");

        Assert.Contains("-maxcpucount:1", script, StringComparison.Ordinal);
        Assert.Contains("-p:BuildInParallel=false", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VercelBuildScript_InstallsWasmToolsWorkload()
    {
        var script = LoadFile(@"scripts\vercel-build.sh");

        Assert.Contains("dotnet workload list", script, StringComparison.Ordinal);
        Assert.Contains("workload_install_args=(wasm-tools --skip-manifest-update --disable-parallel --no-http-cache)", script, StringComparison.Ordinal);
        Assert.Contains("dotnet workload install \"${workload_install_args[@]}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmSmokeGate_RunsSplitBrowserWasmSmokes()
    {
        var gate = LoadFile(@"scripts\gates\run-wasm-smoke-gates.sh");

        Assert.Contains("wasm-settings-navigation-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-start-visibility-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-focus-boundary-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-settings-persistence-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-capability-boundary-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-gamepad-boundary-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-acp-full-chain-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("wasm-smoke-lib", gate, StringComparison.Ordinal);
        Assert.Contains("GIT_BIN=", gate, StringComparison.Ordinal);
        Assert.Contains("PYTHON_BIN=", gate, StringComparison.Ordinal);
        Assert.Contains("CURL_BIN=", gate, StringComparison.Ordinal);
        Assert.Contains("for tool_name in GIT_BIN DOTNET_BIN NODE_BIN NPM_BIN PYTHON_BIN CURL_BIN", gate, StringComparison.Ordinal);
        Assert.Contains("COMMIT=\"$(\"", gate, StringComparison.Ordinal);
        Assert.Contains("\"${GIT_BIN}\" -C \"${REPO_ROOT}\" rev-parse HEAD", gate, StringComparison.Ordinal);
        Assert.Contains("Refusing to run BrowserWasm smoke with Windows interop binary", gate, StringComparison.Ordinal);
        Assert.True(
            gate.IndexOf("if is_wsl_environment;", StringComparison.Ordinal)
            < gate.IndexOf("DOTNET_SDK_ROOT=\"$(resolve_selected_dotnet_sdk_root)\"", StringComparison.Ordinal));
        Assert.DoesNotContain("dirname", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -qiE", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Run WASM file system availability smoke", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-ui.mjs", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmSmokeScripts_AreSplitByBehaviorBoundary()
    {
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-settings-navigation-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-start-visibility-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-focus-boundary-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-settings-persistence-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-capability-boundary-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-gamepad-boundary-smoke.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-acp-full-chain-smoke.mjs")));
        Assert.True(Directory.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\ui-affordances.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\settings-shell.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\acp-ui-fixture.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\browser-app.mjs")));
        Assert.True(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\acp-test-server.mjs")));
        Assert.False(File.Exists(RepoPath(@"scripts\gates\wasm-file-system-availability-smoke.mjs")));
        Assert.False(File.Exists(RepoPath(@"scripts\gates\wasm-smoke-lib\settings-ui.mjs")));
        var settingsPersistenceSmoke = LoadFile(@"scripts\gates\wasm-settings-persistence-smoke.mjs");
        Assert.Contains("language: en-US", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("focused data storage cache retention", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("requireFocused: true,", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("document.activeElement === textInput", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("document.activeElement === element", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("focusedControl: dataStorageCacheRetentionControl", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("projection.focused && projection.focusedTarget", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("findEffectiveBackground", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.Contains("compositeCssColor", settingsPersistenceSmoke, StringComparison.Ordinal);
        // The shell keeps the active Settings section across a language reload, so the English
        // assertion is anchored on the General page summary resource instead of Start copy.
        Assert.Contains("Manage startup, window behavior, and UI language", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.DoesNotContain("Language override is not supported on this platform", settingsPersistenceSmoke, StringComparison.Ordinal);

        foreach (var script in Directory.EnumerateFiles(RepoPath(@"scripts\gates"), "wasm-*.mjs", SearchOption.TopDirectoryOnly))
        {
            var code = File.ReadAllText(script);
            Assert.DoesNotContain("settings-ui.mjs", code, StringComparison.Ordinal);
        }
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

    private static void AssertWasmRestoreCleanBuildOrder(string gate)
    {
        const string restoreCommand = "\"${DOTNET_BIN}\" restore \"${PROJECT}\"";
        const string cleanCommand = "\"${DOTNET_BIN}\" clean \"${PROJECT}\" -c \"${CONFIGURATION}\" -f net10.0-browserwasm -v minimal";
        const string buildCommand = "\"${DOTNET_BIN}\" build \"${PROJECT}\" -c \"${CONFIGURATION}\" -f net10.0-browserwasm --no-restore -v minimal";

        Assert.Equal(1, gate.Split(restoreCommand, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, gate.Split(cleanCommand, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, gate.Split(buildCommand, StringSplitOptions.None).Length - 1);

        var restoreIndex = gate.IndexOf(restoreCommand, StringComparison.Ordinal);
        var cleanIndex = gate.IndexOf(cleanCommand, StringComparison.Ordinal);
        var buildIndex = gate.IndexOf(buildCommand, StringComparison.Ordinal);
        Assert.True(restoreIndex < cleanIndex);
        Assert.True(cleanIndex < buildIndex);
    }

    /// <summary>
    /// Returns the <c>__WASM__</c> arm of the secure-storage conditional, so an assertion about the
    /// WASM registration cannot be satisfied by another platform's arm.
    /// </summary>
    private static string ExtractWasmSecureStorageBranch(string code)
    {
        // The file has several __WASM__ arms, so anchor on the secure-storage conditional rather than
        // the first marker: otherwise this reads an unrelated branch and passes for the wrong reason.
        var conditionalStart = code.IndexOf("services.AddSingleton<ISecureStorage", StringComparison.Ordinal);
        Assert.True(conditionalStart >= 0, "DependencyInjection.cs no longer registers ISecureStorage.");

        const string marker = "#elif __WASM__";
        var searchFrom = code.LastIndexOf("#if ", conditionalStart, StringComparison.Ordinal);
        var start = code.IndexOf(marker, searchFrom < 0 ? 0 : searchFrom, StringComparison.Ordinal);
        Assert.True(start >= 0, "DependencyInjection.cs no longer has a __WASM__ secure-storage branch.");

        var rest = code[(start + marker.Length)..];
        var end = rest.IndexOf('#', StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    private static XElement LoadBrowserWasmPropertyGroup()
    {
        var project = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));
        return project
            .Descendants("PropertyGroup")
            .First(element => (string?)element.Attribute("Condition") == "'$(TargetFramework)' == 'net10.0-browserwasm'");
    }

    private static string RepoPath(string relativePath)
        => Path.Combine(FindRepoRoot(), NormalizeRelativePath(relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
