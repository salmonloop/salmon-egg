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
    }

    [Fact]
    public void BrowserWasmBuild_RemovesDesktopProcessDependenciesFromInfrastructureGraph()
    {
        var appProject = XDocument.Parse(LoadFile(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj"));
        var infrastructureProject = XDocument.Parse(LoadFile(@"src\SalmonEgg.Infrastructure\SalmonEgg.Infrastructure.csproj"));

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
        Assert.Null(infrastructureReference.Attribute("AdditionalProperties"));
        Assert.DoesNotContain(infrastructureProject.Descendants("PackageReference"), element => (string?)element.Attribute("Include") == "Porta.Pty");
        var desktopReference = Assert.Single(desktopInfrastructureReferences);
        Assert.Equal("'$(SalmonEggSupportsDesktopProcessHost)' != 'false'", (string?)desktopReference.Attribute("Condition"));
    }

    [Fact]
    public void DependencyInjection_RegistersUnsupportedTerminalManagerForBrowserWasm()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("#if __WASM__", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IPlatformShellService, WasmPlatformShellService>();", code, StringComparison.Ordinal);
        Assert.Contains("#elif __ANDROID__ || __IOS__", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ITerminalSessionManager, UnsupportedTerminalSessionManager>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IStdioTransportFactory, UnsupportedStdioTransportFactory>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IPlatformShellService, UnsupportedPlatformShellService>();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_RegistersBrowserWasmFilePersistenceAndFileBackedSecureStorage()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("services.AddSingleton<IFileSystemPersistence, WasmFileSystemPersistence>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IAppFileStore>(sp => new FileSystemAppFileStore(sp.GetRequiredService<IFileSystemPersistence>()));", code, StringComparison.Ordinal);
        // Non-Windows platforms persist secrets via AppFileStoreSecureStorage (backed by IAppFileStore/IDBFS on WASM).
        Assert.DoesNotContain("services.AddSingleton<ISecureStorage, VolatileSecureStorage>();", code, StringComparison.Ordinal);
        Assert.Contains("new AppFileStoreSecureStorage(", code, StringComparison.Ordinal);
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
    public void WasmSmokeGate_RunsFileSystemAvailabilitySmoke()
    {
        var gate = LoadFile(@"scripts\gates\run-wasm-smoke-gates.sh");

        Assert.Contains("wasm-file-system-availability-smoke.mjs", gate, StringComparison.Ordinal);
        Assert.Contains("Run WASM file system availability smoke", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmFileSystemAvailabilitySmoke_CoversPersistenceAndDesktopCapabilityBoundary()
    {
        var smoke = LoadFile(@"scripts\gates\wasm-file-system-availability-smoke.mjs");

        Assert.Contains("indexedDB.deleteDatabase", smoke, StringComparison.Ordinal);
        Assert.Contains("DataStorage.CacheRetention", smoke, StringComparison.Ordinal);
        Assert.Contains("DataStorage.OpenCacheFolder", smoke, StringComparison.Ordinal);
        Assert.Contains("DataStorage.OpenExports", smoke, StringComparison.Ordinal);
        Assert.Contains("expectNoAdvertisedFileSystemCapability", smoke, StringComparison.Ordinal);
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
