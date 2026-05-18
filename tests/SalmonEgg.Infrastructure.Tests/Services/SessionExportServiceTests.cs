using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class SessionExportServiceTests : IDisposable
{
    private readonly string _root;

    public SessionExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggSessionExportTests", Guid.NewGuid().ToString("N"));
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
    public async Task ExportAsync_Json_WritesExportWithMessages()
    {
        var sut = new SessionExportService(
            new TestAppDataService(_root),
            new TestPlatformCapabilities(supportsLocalFileExport: true),
            new FileSystemAppFileStore());
        var request = new SessionExportRequest(
            "json",
            "session-1",
            "agent",
            "1.0",
            new List<SessionExportMessage>
            {
                new("message-1", DateTimeOffset.UnixEpoch, true, "text", null, "hello")
            });

        var result = await sut.ExportAsync(request);

        Assert.Equal(SessionExportStatus.Success, result.Status);
        var path = Assert.IsType<string>(result.Path);
        Assert.True(File.Exists(path));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("session-1", json.RootElement.GetProperty("SessionId").GetString());
        Assert.Single(json.RootElement.GetProperty("Messages").EnumerateArray());
    }

    [Fact]
    public async Task ExportAsync_WhenLocalFileExportUnsupported_DoesNotCreateExport()
    {
        var sut = new SessionExportService(
            new TestAppDataService(_root),
            new TestPlatformCapabilities(supportsLocalFileExport: false),
            new FileSystemAppFileStore());
        var request = new SessionExportRequest(
            "json",
            "session-1",
            "agent",
            "1.0",
            []);

        var result = await sut.ExportAsync(request);

        Assert.Equal(SessionExportStatus.Unsupported, result.Status);
        Assert.Null(result.Path);
        Assert.False(Directory.Exists(Path.Combine(_root, "exports")));
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
}
