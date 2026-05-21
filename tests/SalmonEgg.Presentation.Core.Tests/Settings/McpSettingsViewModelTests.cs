using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class McpSettingsViewModelTests
{
    [Fact]
    public async Task LoadCommand_LoadsGlobalMcpSettingsIntoRows()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                IsEnabled = true,
                Servers =
                {
                    new StdioMcpServer("filesystem", "C:\\mcp\\filesystem.exe", ["--root", "C:\\repo"]),
                    new HttpMcpServer("search", "https://example.com/mcp", [new McpHttpHeader("Authorization", "Bearer token")])
                }
            }
        };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEnabled);
        Assert.Equal(2, viewModel.Servers.Count);
        Assert.Equal("filesystem", viewModel.Servers[0].Name);
        Assert.Equal(McpServerTransport.Stdio, viewModel.Servers[0].Transport);
        Assert.Equal("C:\\mcp\\filesystem.exe", viewModel.Servers[0].Command);
        Assert.Equal("--root C:\\repo", viewModel.Servers[0].ArgumentsText);
        Assert.Equal("search", viewModel.Servers[1].Name);
        Assert.Equal(McpServerTransport.Http, viewModel.Servers[1].Transport);
        Assert.Equal("https://example.com/mcp", viewModel.Servers[1].Url);
        Assert.Equal("Authorization: Bearer token", viewModel.Servers[1].HeadersText);
    }

    [Fact]
    public async Task SaveCommand_PersistsEnabledStateAndEditedRows()
    {
        var service = new FakeMcpSettingsService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsEnabled = true;
        viewModel.AddServerCommand.Execute(null);
        var row = Assert.Single(viewModel.Servers);
        row.Name = "filesystem";
        row.Transport = McpServerTransport.Stdio;
        row.Command = "C:\\mcp\\filesystem.exe";
        row.ArgumentsText = "--root \"C:\\repo path\"";
        row.EnvironmentText = "ROOT=C:\\repo path";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(service.SavedSettings);
        Assert.True(service.SavedSettings!.IsEnabled);
        var server = Assert.IsType<StdioMcpServer>(Assert.Single(service.SavedSettings.Servers));
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("C:\\mcp\\filesystem.exe", server.Command);
        Assert.Equal(["--root", "C:\\repo path"], server.Args);
        var env = Assert.Single(server.Env!);
        Assert.Equal("ROOT", env.Name);
        Assert.Equal("C:\\repo path", env.Value);
    }

    [Fact]
    public async Task IsEnabledChanged_PersistsGlobalSwitchWithoutServerRows()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                IsEnabled = false
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsEnabled = true;

        await WaitUntilAsync(() => service.SavedSettings is not null);

        Assert.NotNull(service.SavedSettings);
        Assert.True(service.SavedSettings!.IsEnabled);
        Assert.Empty(service.SavedSettings.Servers);
    }

    [Fact]
    public async Task IsEnabledChanged_WhenReloaded_RetainsPersistedGlobalSwitch()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                IsEnabled = false
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsEnabled = true;
        await WaitUntilAsync(() => service.SavedSettings is not null);
        service.Settings = service.SavedSettings!;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEnabled);
    }

    [Fact]
    public async Task RemoveServerCommand_RemovesSelectedRowBeforeSave()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                IsEnabled = true,
                Servers =
                {
                    new HttpMcpServer("search", "https://example.com/mcp")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.RemoveServerCommand.Execute(viewModel.Servers[0]);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Servers);
        Assert.NotNull(service.SavedSettings);
        Assert.Empty(service.SavedSettings!.Servers);
    }

    [Fact]
    public async Task RowRemoveCommand_RemovesItsOwnRow()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new StdioMcpServer("filesystem", "npx"),
                    new HttpMcpServer("search", "https://example.com/mcp")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Servers[0].RemoveCommand.Execute(null);

        var row = Assert.Single(viewModel.Servers);
        Assert.Equal("search", row.Name);
    }

    [Fact]
    public async Task AddRemoveAddRemoveSequence_DoesNotReuseStaleRowCommand()
    {
        var viewModel = CreateViewModel(new FakeMcpSettingsService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.AddServerCommand.Execute(null);
        var firstRow = Assert.Single(viewModel.Servers);
        firstRow.RemoveCommand.Execute(null);

        Assert.Empty(viewModel.Servers);

        viewModel.AddServerCommand.Execute(null);
        var secondRow = Assert.Single(viewModel.Servers);
        Assert.NotSame(firstRow, secondRow);

        secondRow.RemoveCommand.Execute(null);

        Assert.Empty(viewModel.Servers);
    }

    [Fact]
    public async Task ImportJsonCommand_ExpandsAndImportsKeyedMcpServers()
    {
        var viewModel = CreateViewModel(new FakeMcpSettingsService());
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ImportJsonText = """
        {
          "mcpServers": {
            "filesystem": {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\repo"],
              "env": {
                "API_KEY": "secret"
              }
            }
          }
        }
        """;

        await viewModel.ImportJsonCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsImportPanelOpen);
        var row = Assert.Single(viewModel.Servers);
        Assert.Equal("filesystem", row.Name);
        Assert.Equal(McpServerTransport.Stdio, row.Transport);
        Assert.Equal("npx", row.Command);
        Assert.Equal("-y @modelcontextprotocol/server-filesystem C:\\repo", row.ArgumentsText);
        Assert.Equal("API_KEY=secret", row.EnvironmentText);
        Assert.Equal("McpSettings_ImportSucceeded", viewModel.StatusMessage);
        Assert.Equal("McpSettings_ImportSucceeded", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task ImportJsonCommand_ImportsArrayServersAndReplacesSameName()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new StdioMcpServer("search", "old.exe")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ImportJsonText = """
        {
          "mcpServers": [
            {
              "name": "search",
              "type": "http",
              "url": "https://example.com/mcp",
              "headers": {
                "Authorization": "Bearer token"
              }
            }
          ]
        }
        """;

        await viewModel.ImportJsonCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Servers);
        Assert.Equal("search", row.Name);
        Assert.Equal(McpServerTransport.Http, row.Transport);
        Assert.Equal("https://example.com/mcp", row.Url);
        Assert.Equal("Authorization: Bearer token", row.HeadersText);
        Assert.Equal("McpSettings_ImportSucceededWithReplacements", viewModel.StatusMessage);
        Assert.Equal("McpSettings_ImportSucceededWithReplacements", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task ImportJsonCommand_WhenJsonInvalid_SetsLocalizedFailure()
    {
        var viewModel = CreateViewModel(new FakeMcpSettingsService());
        viewModel.ImportJsonText = "{ invalid";

        await viewModel.ImportJsonCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Servers);
        Assert.Equal("McpSettings_ImportFailed", viewModel.StatusMessage);
        Assert.Equal("McpSettings_ImportFailed", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task ClearImportJsonCommand_ClearsImportFeedback()
    {
        var viewModel = CreateViewModel(new FakeMcpSettingsService());
        viewModel.ImportJsonText = "{ invalid";
        await viewModel.ImportJsonCommand.ExecuteAsync(null);

        viewModel.ClearImportJsonCommand.Execute(null);

        Assert.Empty(viewModel.ImportJsonText);
        Assert.Empty(viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task SaveCommand_WhenAddedDraftIsIncomplete_ShowsValidationAndDoesNotPersist()
    {
        var service = new FakeMcpSettingsService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.AddServerCommand.Execute(null);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(service.SavedSettings);
        Assert.Equal("McpSettings_SaveValidationFailed", viewModel.StatusMessage);
    }

    private static McpSettingsViewModel CreateViewModel(IMcpSettingsService settingsService)
        => new(settingsService, new TestCoreStringLocalizer(), Mock.Of<ILogger<McpSettingsViewModel>>());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private sealed class FakeMcpSettingsService : IMcpSettingsService
    {
        public McpSettings Settings { get; set; } = new();

        public McpSettings? SavedSettings { get; private set; }

        public Task<McpSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task SaveAsync(McpSettings settings, CancellationToken cancellationToken = default)
        {
            SavedSettings = settings;
            return Task.CompletedTask;
        }
    }
}
