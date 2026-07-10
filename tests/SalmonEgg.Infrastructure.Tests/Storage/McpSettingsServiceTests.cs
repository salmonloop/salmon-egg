using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class McpSettingsServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public McpSettingsServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggMcpSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Environment.SetEnvironmentVariable(
            "SALMONEGG_APPDATA_ROOT",
            Path.Combine(_testDirectory, "SalmonEgg"),
            EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task LoadAsync_WhenMcpYamlMissing_ReturnsEmptyCatalog()
    {
        var service = CreateService();

        var settings = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(settings.Servers);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsMcpServerCatalog()
    {
        var service = CreateService();
        var settings = new McpSettings
        {
            Servers =
            [
                new McpServerCatalogEntry(
                    new StdioMcpServer(
                        "filesystem",
                        "/usr/bin/mcp-filesystem",
                        ["--stdio"],
                        [
                            new McpEnvVariable("ROOT", "/repo")
                            {
                                Meta = new()
                                {
                                    ["scope"] = "workspace"
                                }
                            }
                        ])
                    {
                        Meta = new()
                        {
                            ["source"] = "settings"
                        }
                    },
                    enabled: false),
                new HttpMcpServer(
                    "api",
                    "api.example.com/mcp",
                    [
                        new McpHttpHeader("Authorization", "Bearer token")
                        {
                            Meta = new()
                            {
                                ["secret_ref"] = "header-auth"
                            }
                        }
                    ]),
                new SseMcpServer("events", "events.example.com/mcp")
            ]
        };

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);

        var yaml = await File.ReadAllTextAsync(GetMcpYamlPath(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("is_enabled:", yaml, StringComparison.Ordinal);
        Assert.Contains("enabled: false", yaml, StringComparison.Ordinal);
        Assert.Contains("servers:", yaml, StringComparison.Ordinal);
        Assert.Contains("transport: stdio", yaml, StringComparison.Ordinal);
        Assert.Contains("transport: http", yaml, StringComparison.Ordinal);
        Assert.Contains("transport: sse", yaml, StringComparison.Ordinal);
        Assert.Contains("source: settings", yaml, StringComparison.Ordinal);
        Assert.Contains("scope: workspace", yaml, StringComparison.Ordinal);
        Assert.Contains("secret_ref: header-auth", yaml, StringComparison.Ordinal);

        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, loaded.Servers.Count);

        Assert.False(loaded.Servers[0].Enabled);
        var stdio = Assert.IsType<StdioMcpServer>(loaded.Servers[0].Server);
        Assert.Equal("filesystem", stdio.Name);
        Assert.Equal("/usr/bin/mcp-filesystem", stdio.Command);
        Assert.Equal("--stdio", Assert.Single(stdio.Args!));
        Assert.Equal("settings", stdio.Meta!["source"]);
        var env = Assert.Single(stdio.Env!);
        Assert.Equal("ROOT", env.Name);
        Assert.Equal("/repo", env.Value);
        Assert.Equal("workspace", env.Meta!["scope"]);

        var http = Assert.IsType<HttpMcpServer>(loaded.Servers[1].Server);
        Assert.Equal("api.example.com/mcp", http.Url);
        var header = Assert.Single(http.Headers!);
        Assert.Equal("Authorization", header.Name);
        Assert.Equal("Bearer token", header.Value);
        Assert.Equal("header-auth", header.Meta!["secret_ref"]);

        var sse = Assert.IsType<SseMcpServer>(loaded.Servers[2].Server);
        Assert.Equal("events", sse.Name);
    }

    [Fact]
    public async Task SaveAsync_WhenServerDisabled_StillPersistsConfiguredServer()
    {
        var service = CreateService();
        var settings = new McpSettings
        {
            Servers =
            [
                new McpServerCatalogEntry(
                    new StdioMcpServer("filesystem", string.Empty, [], []),
                    enabled: false)
            ]
        };

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);

        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(loaded.Servers);
        Assert.False(entry.Enabled);
        var server = Assert.IsType<StdioMcpServer>(entry.Server);
        Assert.Equal(string.Empty, server.Command);
    }

    [Fact]
    public async Task SaveAsync_WithInvalidMcpServer_Throws()
    {
        var service = CreateService();
        var settings = new McpSettings
        {
            Servers =
            [
                new StdioMcpServer("filesystem", string.Empty, [], [])
            ]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(settings, TestContext.Current.CancellationToken));
        Assert.Contains("MCP server configuration is invalid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidMcpYaml_ReturnsEmptyCatalog()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GetMcpYamlPath())!);
        await File.WriteAllTextAsync(
            GetMcpYamlPath(),
            """
            schema_version: 1
            servers:
            - transport: stdio
              name: filesystem
              command: ''
            """, TestContext.Current.CancellationToken);
        var service = CreateService();

        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Servers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("legacy")]
    public async Task LoadAsync_WithUnsupportedTransport_ReturnsEmptyCatalog(string transport)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GetMcpYamlPath())!);
        await File.WriteAllTextAsync(
            GetMcpYamlPath(),
            $$"""
            schema_version: 1
            servers:
            - transport: '{{transport}}'
              name: filesystem
              command: /usr/bin/mcp-filesystem
            """, TestContext.Current.CancellationToken);
        var service = CreateService();

        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Servers);
    }

    [Fact]
    public void Constructor_DoesNotCreateConfigDirectory()
    {
        _ = CreateService();

        Assert.False(Directory.Exists(Path.Combine(_testDirectory, "SalmonEgg", "config")));
    }

    [Fact]
    public async Task SaveAsync_WhenExistingFileIsCorruptedYaml_OverwritesAndLoadsBack()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GetMcpYamlPath())!);
        await File.WriteAllTextAsync(GetMcpYamlPath(), ":\n  - definitely not yaml", TestContext.Current.CancellationToken);

        var service = CreateService();
        var settings = new McpSettings
        {
            Servers =
            [
                new StdioMcpServer("filesystem", "/usr/bin/mcp-filesystem", ["--stdio"], [])
            ]
        };

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);

        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);
        var server = Assert.IsType<StdioMcpServer>(Assert.Single(loaded.Servers).Server);
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("/usr/bin/mcp-filesystem", server.Command);
    }

    private McpSettingsService CreateService()
        => new(new FileSystemAppFileStore(), new AppDataService(), NullLogger<McpSettingsService>.Instance);

    private string GetMcpYamlPath()
        => Path.Combine(_testDirectory, "SalmonEgg", "config", "mcp.yaml");
}
