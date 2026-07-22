using System.Xml.Linq;
using SalmonEgg.Domain.Models;

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
        var enableIosTarget = project.Descendants("EnableIosTarget").Single();
        var androidTargets = project.Descendants("SalmonEggAndroidTargetFrameworks").Single();
        var iosTargets = project.Descendants("SalmonEggIosTargetFrameworks").Single();
        var mobileTargets = project.Descendants("SalmonEggMobileTargetFrameworks").ToArray();

        Assert.Equal("false", enableMobileTargets.Value);
        Assert.Equal("'$(EnableMobileTargets)' == ''", (string?)enableMobileTargets.Attribute("Condition"));
        Assert.Equal("false", enableIosTarget.Value);
        Assert.Equal("'$(EnableIosTarget)' == ''", (string?)enableIosTarget.Attribute("Condition"));

        Assert.Equal("net10.0-android36.0", androidTargets.Value);
        Assert.Contains("'$(EnableMobileTargets)' == 'true'", (string?)androidTargets.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("'$(AndroidSdkDirectory)' != ''", (string?)androidTargets.Attribute("Condition"), StringComparison.Ordinal);

        Assert.Equal("net10.0-ios", iosTargets.Value);
        Assert.Contains("'$(EnableMobileTargets)' == 'true'", (string?)iosTargets.Attribute("Condition"), StringComparison.Ordinal);
        Assert.Contains("'$(EnableIosTarget)' == 'true'", (string?)iosTargets.Attribute("Condition"), StringComparison.Ordinal);

        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggAndroidTargetFrameworks)");
        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggMobileTargetFrameworks);$(SalmonEggIosTargetFrameworks)");
        Assert.Contains(mobileTargets, element => element.Value == "$(SalmonEggIosTargetFrameworks)");
    }

    [Fact]
    public void MobileTargetGate_VerifiesTargetExpansionAndAndroidSecureStorageSource()
    {
        var gate = LoadFile(@"scripts\gates\verify-mobile-target-contracts.sh");

        Assert.Contains("-getProperty:TargetFrameworks", gate, StringComparison.Ordinal);
        Assert.Contains("-p:EnableMobileTargets=true -p:EnableIosTarget=true", gate, StringComparison.Ordinal);
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
        Assert.Contains("RawGameControllerFaceButtonLayoutResolver.Resolve", reader, StringComparison.Ordinal);
        Assert.Contains("BrowserGamepadInputReadingMapper.GetInputReading", reader, StringComparison.Ordinal);
        Assert.Contains("StandardGamepadInputReadingMapper.GetInputReading", LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Input\BrowserGamepadInputReadingMapper.cs"), StringComparison.Ordinal);
        Assert.Contains("GamepadIntentProcessor.GetActiveIntents", reader, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsInputSource.Gamepad", reader, StringComparison.Ordinal);
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
    public void DependencyInjection_RegistersPlainTextSecureStorageFallback()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("services.AddSingleton<IFileSystemPersistence, WasmFileSystemPersistence>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<PlainTextFileSecureStorage>();", code, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IConfigChangeSignal>()", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage>(sp => sp.GetRequiredService<PlainTextFileSecureStorage>());", code, StringComparison.Ordinal);
        Assert.Contains("new FallbackSecureStorage(new LinuxSecretServiceSecureStorage(), fallback)", code, StringComparison.Ordinal);
        Assert.Contains("new FallbackSecureStorage(new MacOSKeychainSecureStorage(), fallback)", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage, AndroidKeyStoreSecureStorage>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage, IosKeychainSecureStorage>();", code, StringComparison.Ordinal);
        Assert.True(File.Exists(RepoPath(@"src\SalmonEgg.Infrastructure\Storage\PlainTextFileSecureStorage.cs")));
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
        Assert.Contains("Your AI co-pilot for ACP sessions", settingsPersistenceSmoke, StringComparison.Ordinal);
        Assert.DoesNotContain("Language override is not supported on this platform", settingsPersistenceSmoke, StringComparison.Ordinal);

        foreach (var script in Directory.EnumerateFiles(RepoPath(@"scripts\gates"), "wasm-*.mjs", SearchOption.TopDirectoryOnly))
        {
            var code = File.ReadAllText(script);
            Assert.DoesNotContain("settings-ui.mjs", code, StringComparison.Ordinal);
        }
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

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
