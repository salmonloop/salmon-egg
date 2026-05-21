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
    public async Task LoadCommand_LoadsMcpSettingsIntoRows()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new StdioMcpServer("filesystem", "C:\\mcp\\filesystem.exe", ["--root", "C:\\repo"])
                    {
                        Enabled = false
                    },
                    new HttpMcpServer("search", "https://example.com/mcp", [new McpHttpHeader("Authorization", "Bearer token")])
                }
            }
        };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Servers.Count);
        Assert.Equal("filesystem", viewModel.Servers[0].Name);
        Assert.False(viewModel.Servers[0].Enabled);
        Assert.Equal(McpServerTransport.Stdio, viewModel.Servers[0].Transport);
        Assert.False(viewModel.Servers[0].IsDetailsExpanded);
        Assert.Equal("C:\\mcp\\filesystem.exe", viewModel.Servers[0].Command);
        Assert.Equal("--root C:\\repo", viewModel.Servers[0].ArgumentsText);
        Assert.Equal("McpSettings_RowSaved", viewModel.Servers[0].StatusMessage);
        Assert.Equal("search", viewModel.Servers[1].Name);
        Assert.Equal(McpServerTransport.Http, viewModel.Servers[1].Transport);
        Assert.False(viewModel.Servers[1].IsDetailsExpanded);
        Assert.Equal("https://example.com/mcp", viewModel.Servers[1].Url);
        Assert.Equal("Authorization: Bearer token", viewModel.Servers[1].HeadersText);
        Assert.Equal("McpSettings_RowSaved", viewModel.Servers[1].StatusMessage);
        Assert.False(viewModel.IsEditorOpen);
    }

    [Fact]
    public void TransportOptions_UseMcpProtocolCasingForDisplayNames()
    {
        var row = new McpServerRowViewModel();

        Assert.Collection(
            row.TransportOptions,
            option =>
            {
                Assert.Equal(McpServerTransport.Stdio, option.Transport);
                Assert.Equal("stdio", option.Name);
            },
            option =>
            {
                Assert.Equal(McpServerTransport.Http, option.Transport);
                Assert.Equal("Streamable HTTP", option.Name);
            },
            option =>
            {
                Assert.Equal(McpServerTransport.Sse, option.Transport);
                Assert.Equal("SSE", option.Name);
            });
    }

    [Fact]
    public async Task RowSaveCommand_PersistsOnlySelectedServer()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new HttpMcpServer("search", "https://example.com/mcp"),
                    new HttpMcpServer("docs", "https://docs.example.com/mcp")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = viewModel.Servers[0];
        row.Name = "filesystem";
        row.Transport = McpServerTransport.Stdio;
        row.Command = "C:\\mcp\\filesystem.exe";
        row.ArgumentsText = "--root \"C:\\repo path\"";
        row.EnvironmentText = "ROOT=C:\\repo path";
        viewModel.Servers[1].Name = "unsaved-local-edit";

        await row.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(service.SavedSettings);
        Assert.Equal(2, service.SavedSettings!.Servers.Count);
        var server = Assert.IsType<StdioMcpServer>(service.SavedSettings.Servers[0]);
        Assert.Equal("filesystem", server.Name);
        Assert.Equal("C:\\mcp\\filesystem.exe", server.Command);
        Assert.Equal(["--root", "C:\\repo path"], server.Args);
        var env = Assert.Single(server.Env!);
        Assert.Equal("ROOT", env.Name);
        Assert.Equal("C:\\repo path", env.Value);
        Assert.Equal("docs", service.SavedSettings.Servers[1].Name);
        Assert.Equal("filesystem", row.PersistedName);
        Assert.Equal("McpSettings_RowSaved", row.StatusMessage);
    }

    [Fact]
    public async Task RemoveServerCommand_RemovesSelectedRowBeforeSave()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new HttpMcpServer("search", "https://example.com/mcp")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.RemoveServerCommand.Execute(viewModel.Servers[0]);

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
        Assert.NotNull(viewModel.EditingServer);
        var firstRow = viewModel.EditingServer;
        viewModel.CloseEditorCommand.Execute(null);

        Assert.Empty(viewModel.Servers);
        Assert.False(viewModel.IsEditorOpen);

        viewModel.AddServerCommand.Execute(null);
        Assert.NotNull(viewModel.EditingServer);
        var secondRow = viewModel.EditingServer;
        Assert.NotSame(firstRow, secondRow);

        viewModel.CloseEditorCommand.Execute(null);

        Assert.Empty(viewModel.Servers);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_FillsDraftFromKeyedMcpServersJson()
    {
        var shell = new FakePlatformShellService
        {
            ClipboardText = """
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
            """
        };
        var viewModel = CreateViewModel(new FakeMcpSettingsService(), shell);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.AddServerCommand.Execute(null);

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Servers);
        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("filesystem", viewModel.EditingServer!.Name);
        Assert.Equal(McpServerTransport.Stdio, viewModel.EditingServer.Transport);
        Assert.Equal("npx", viewModel.EditingServer.Command);
        Assert.Equal("-y @modelcontextprotocol/server-filesystem C:\\repo", viewModel.EditingServer.ArgumentsText);
        Assert.Equal("API_KEY=secret", viewModel.EditingServer.EnvironmentText);
        Assert.Empty(viewModel.StatusMessage);
        Assert.Equal("McpSettings_ClipboardFilled", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_FillsExistingEditorWithoutChangingEnabledState()
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
        var shell = new FakePlatformShellService
        {
            ClipboardText = """
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
            """
        };
        var viewModel = CreateViewModel(service, shell);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Servers[0].SetEnabledFromStore(false);
        viewModel.Servers[0].EditCommand.Execute(null);

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Servers);
        Assert.Equal("old.exe", row.Command);
        Assert.False(viewModel.EditingServer!.Enabled);
        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("search", viewModel.EditingServer!.Name);
        Assert.Equal("search", viewModel.EditingServer.PersistedName);
        Assert.Equal(McpServerTransport.Http, viewModel.EditingServer.Transport);
        Assert.Equal("https://example.com/mcp", viewModel.EditingServer.Url);
        Assert.Equal("Authorization: Bearer token", viewModel.EditingServer.HeadersText);
        Assert.Empty(viewModel.StatusMessage);
        Assert.Equal("McpSettings_ClipboardFilled", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_AcceptsJsonCodeFence()
    {
        var shell = new FakePlatformShellService
        {
            ClipboardText = """
            ```json
            {
              "name": "docs",
              "transport": "sse",
              "url": "https://example.com/sse"
            }
            ```
            """
        };
        var viewModel = CreateViewModel(new FakeMcpSettingsService(), shell);

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("docs", viewModel.EditingServer!.Name);
        Assert.Equal(McpServerTransport.Sse, viewModel.EditingServer.Transport);
        Assert.Equal("https://example.com/sse", viewModel.EditingServer.Url);
        Assert.Equal("McpSettings_ClipboardFilled", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_FindsNestedMcpServers()
    {
        var shell = new FakePlatformShellService
        {
            ClipboardText = """
            {
              "workspace": {
                "tools": {
                  "profile": {
                    "mcpServers": {
                      "nested-filesystem": {
                        "command": "npx",
                        "args": ["-y", "@modelcontextprotocol/server-filesystem"]
                      }
                    }
                  }
                }
              }
            }
            """
        };
        var viewModel = CreateViewModel(new FakeMcpSettingsService(), shell);

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("nested-filesystem", viewModel.EditingServer!.Name);
        Assert.Equal(McpServerTransport.Stdio, viewModel.EditingServer.Transport);
        Assert.Equal("npx", viewModel.EditingServer.Command);
        Assert.Equal("-y @modelcontextprotocol/server-filesystem", viewModel.EditingServer.ArgumentsText);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_SkipsInvalidNestedCandidateWhenLaterCandidateIsValid()
    {
        var shell = new FakePlatformShellService
        {
            ClipboardText = """
            {
              "bad": {
                "mcpServers": {
                  "broken": {
                    "type": "http"
                  }
                }
              },
              "good": {
                "mcpServers": [
                  {
                    "name": "remote-search",
                    "type": "streamable-http",
                    "url": "https://example.com/mcp"
                  }
                ]
              }
            }
            """
        };
        var viewModel = CreateViewModel(new FakeMcpSettingsService(), shell);

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Equal("remote-search", viewModel.EditingServer!.Name);
        Assert.Equal(McpServerTransport.Http, viewModel.EditingServer.Transport);
        Assert.Equal("https://example.com/mcp", viewModel.EditingServer.Url);
        Assert.Equal("McpSettings_ClipboardFilled", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_WhenJsonInvalid_SetsLocalizedFailure()
    {
        var viewModel = CreateViewModel(
            new FakeMcpSettingsService(),
            new FakePlatformShellService { ClipboardText = "{ invalid" });

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Servers);
        Assert.Empty(viewModel.StatusMessage);
        Assert.Equal("McpSettings_ImportFailed", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task FillEditorFromClipboardCommand_WhenClipboardEmpty_SetsLocalizedFailure()
    {
        var viewModel = CreateViewModel(
            new FakeMcpSettingsService(),
            new FakePlatformShellService { ClipboardText = " " });

        await viewModel.FillEditorFromClipboardCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.StatusMessage);
        Assert.Equal("McpSettings_ClipboardEmpty", viewModel.ImportStatusMessage);
    }

    [Fact]
    public async Task RowSaveCommand_WhenAddedDraftIsIncomplete_ShowsValidationAndDoesNotPersist()
    {
        var service = new FakeMcpSettingsService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.AddServerCommand.Execute(null);

        Assert.NotNull(viewModel.EditingServer);
        var row = viewModel.EditingServer!;
        await row.SaveCommand.ExecuteAsync(null);

        Assert.Null(service.SavedSettings);
        Assert.Equal("McpSettings_SaveValidationCommandRequired", viewModel.StatusMessage);
        Assert.Equal("McpSettings_SaveValidationCommandRequired", row.StatusMessage);
    }

    [Fact]
    public async Task AddServerCommand_CreatesExpandedEditableDraft()
    {
        var viewModel = CreateViewModel(new FakeMcpSettingsService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.AddServerCommand.Execute(null);

        Assert.NotNull(viewModel.EditingServer);
        var row = viewModel.EditingServer!;
        Assert.Empty(viewModel.Servers);
        Assert.True(viewModel.IsEditorOpen);
        Assert.True(row.IsDetailsExpanded);
        Assert.Equal("McpSettings_RowUnsaved", row.StatusMessage);
    }

    [Fact]
    public async Task RowSaveCommand_WhenDisabledDraftIsIncomplete_PersistsAsDisabledDraft()
    {
        var service = new FakeMcpSettingsService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.AddServerCommand.Execute(null);
        Assert.NotNull(viewModel.EditingServer);
        var row = viewModel.EditingServer!;
        row.Enabled = false;

        await row.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(service.SavedSettings);
        var server = Assert.IsType<StdioMcpServer>(Assert.Single(service.SavedSettings!.Servers));
        Assert.False(server.Enabled);
        Assert.Equal("new-mcp-server", server.Name);
        Assert.Equal(string.Empty, server.Command);
        Assert.Single(viewModel.Servers);
        Assert.False(viewModel.IsEditorOpen);
    }

    [Fact]
    public async Task RowSaveCommand_WhenDisabledDraftHasNoName_ShowsValidationAndDoesNotPersist()
    {
        var service = new FakeMcpSettingsService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.AddServerCommand.Execute(null);
        Assert.NotNull(viewModel.EditingServer);
        var row = viewModel.EditingServer!;
        row.Enabled = false;
        var savesBeforeBlankName = service.SaveCount;
        row.Name = string.Empty;

        await row.SaveCommand.ExecuteAsync(null);

        Assert.Equal(savesBeforeBlankName, service.SaveCount);
        Assert.Equal("McpSettings_SaveValidationNameRequired", row.StatusMessage);
        Assert.Equal("McpSettings_SaveValidationNameRequired", viewModel.StatusMessage);
    }

    [Fact]
    public async Task EnabledChanged_PersistsSingleServerImmediately()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new HttpMcpServer("search", "https://example.com/mcp")
                    {
                        Enabled = false
                    }
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Servers[0].Enabled = true;

        Assert.NotNull(service.SavedSettings);
        Assert.True(Assert.Single(service.SavedSettings!.Servers).Enabled);
        Assert.Equal("McpSettings_RowSaved", viewModel.Servers[0].StatusMessage);
    }

    [Fact]
    public async Task EnabledChanged_WhenServerIncomplete_RevertsToDisabledAndShowsRowValidation()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new StdioMcpServer("draft", string.Empty)
                    {
                        Enabled = false
                    }
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.Servers);

        row.Enabled = true;

        Assert.False(row.Enabled);
        Assert.Equal("McpSettings_SaveValidationCommandRequired", row.StatusMessage);
    }

    [Fact]
    public async Task EditingSavedRow_MarksOnlyThatRowUnsaved()
    {
        var service = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new HttpMcpServer("search", "https://example.com/mcp"),
                    new HttpMcpServer("docs", "https://docs.example.com/mcp")
                }
            }
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Servers[0].EditCommand.Execute(null);
        viewModel.EditingServer!.Url = "https://new.example.com/mcp";

        Assert.Equal("McpSettings_RowUnsaved", viewModel.EditingServer.StatusMessage);
        Assert.Equal("McpSettings_RowSaved", viewModel.Servers[0].StatusMessage);
        Assert.Equal("McpSettings_RowSaved", viewModel.Servers[1].StatusMessage);
    }

    private static McpSettingsViewModel CreateViewModel(
        IMcpSettingsService settingsService,
        IPlatformShellService? platformShell = null)
        => new(
            settingsService,
            platformShell ?? new FakePlatformShellService(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<McpSettingsViewModel>>());

    private sealed class FakePlatformShellService : IPlatformShellService
    {
        public string? ClipboardText { get; set; }

        public Task<bool> OpenFolderAsync(string path) => Task.FromResult(false);

        public Task<bool> OpenFileAsync(string path) => Task.FromResult(false);

        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);

        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);

        public Task<string?> ReadClipboardTextAsync() => Task.FromResult(ClipboardText);
    }

    private sealed class FakeMcpSettingsService : IMcpSettingsService
    {
        public McpSettings Settings { get; set; } = new();

        public McpSettings? SavedSettings { get; private set; }

        public int SaveCount { get; private set; }

        public Task<McpSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task SaveAsync(McpSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedSettings = settings;
            return Task.CompletedTask;
        }
    }
}
