using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class DiagnosticsBundleServiceTests : IDisposable
{
    private readonly string _root;

    public DiagnosticsBundleServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggDiagnosticsBundleTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task CreateBundleAsync_WhenLocalFileExportUnsupported_DoesNotCreateExportDirectory()
    {
        var sut = new DiagnosticsBundleService(
            new TestAppDataService(_root),
            new TestPlatformCapabilities(supportsLocalFileExport: false));

        var result = await sut.CreateBundleAsync(new DiagnosticsSnapshot());

        Assert.Equal(DiagnosticsBundleStatus.Unsupported, result.Status);
        Assert.Null(result.Path);
        Assert.False(Directory.Exists(Path.Combine(_root, "exports")));
    }

    private sealed class TestAppDataService : IAppDataService
    {
        public TestAppDataService(string root)
        {
            AppDataRootPath = root;
        }

        public string AppDataRootPath { get; }
        public string ConfigRootPath => Path.Combine(AppDataRootPath, "config");
        public string LogsDirectoryPath => Path.Combine(AppDataRootPath, "logs");
        public string CacheRootPath => Path.Combine(AppDataRootPath, "cache");
        public string ExportsDirectoryPath => Path.Combine(AppDataRootPath, "exports");
    }

    private sealed class TestPlatformCapabilities : IPlatformCapabilityService
    {
        public TestPlatformCapabilities(bool supportsLocalFileExport)
        {
            SupportsLocalFileExport = supportsLocalFileExport;
        }

        public bool SupportsLaunchOnStartup => false;
        public bool SupportsTray => false;
        public bool SupportsLanguageOverride => false;
        public bool SupportsMiniWindow => false;
        public bool SupportsExternalFileOpen => SupportsLocalFileExport;
        public bool SupportsLocalFileExport { get; }
        public bool SupportsStdioTransport => false;
        public bool SupportsInteractiveTerminalSurface => false;
        public bool SupportsLocalTerminal => false;
        public bool SupportsGamepadInput => false;
    }
}
