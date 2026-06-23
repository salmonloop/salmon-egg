using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class McpSettingsService : IMcpSettingsService
{
    private const int CurrentSchemaVersion = 1;

    private readonly IAppFileStore _fileStore;
    private readonly ILogger<McpSettingsService> _logger;
    private readonly string _mcpYamlPath;

    public McpSettingsService(IAppFileStore fileStore, IAppDataService appData, ILogger<McpSettingsService> logger)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _mcpYamlPath = System.IO.Path.Combine(appData.ConfigRootPath, "mcp.yaml");
    }

    public async Task<McpSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(_mcpYamlPath, cancellationToken).ConfigureAwait(false);
            if (yaml is null)
            {
                return new McpSettings();
            }

            var model = YamlSerialization.CreateDeserializer().Deserialize<McpSettingsYamlV1>(yaml);
            if (model.SchemaVersion <= 0)
            {
                return new McpSettings();
            }

            var settings = new McpSettings
            {
                Servers = McpServerYamlMapper.FromYamlServers(model.Servers)
            };

            return HasValidServers(settings) ? CloneSettings(settings) : new McpSettings();
        }
        catch (YamlException)
        {
            return new McpSettings();
        }
        catch (IOException)
        {
            return new McpSettings();
        }
        catch (InvalidDataException)
        {
            return new McpSettings();
        }
    }

    public async Task SaveAsync(McpSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var validation = McpServerSupportPolicy.Validate(
            settings.Servers.Where(server => server.Enabled),
            McpServerSupportPolicy.SupportAllTransports);
        if (!validation.IsSupported)
        {
            throw new InvalidOperationException($"MCP server configuration is invalid: {validation.ErrorMessage}");
        }

        await EnsureWritableSchemaAsync(cancellationToken).ConfigureAwait(false);

        var model = new McpSettingsYamlV1
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Servers = McpServerYamlMapper.ToYamlServers(settings.Servers)
        };

        var yaml = YamlSerialization.CreateSerializer().Serialize(model);
        await _fileStore.WriteAllTextAsync(_mcpYamlPath, yaml, cancellationToken).ConfigureAwait(false);
    }

    private static McpSettings CloneSettings(McpSettings settings)
        => new()
        {
            Servers = McpServerJsonConverter.CloneServers(settings.Servers)
        };

    private static bool HasValidServers(McpSettings settings)
        => McpServerSupportPolicy
            .Validate(settings.Servers.Where(server => server.Enabled), McpServerSupportPolicy.SupportAllTransports)
            .IsSupported;

    private async Task EnsureWritableSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(_mcpYamlPath, cancellationToken).ConfigureAwait(false);
            if (yaml is null)
            {
                return;
            }

            var existing = YamlSerialization.CreateDeserializer().Deserialize<McpSettingsYamlV1>(yaml);
            if (existing.SchemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"MCP settings schema_version {existing.SchemaVersion} is newer than supported version {CurrentSchemaVersion}. Refusing to overwrite.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (YamlException ex)
        {
            // 文件存在但 YAML 已损坏（例如 WASM IDBFS 在浏览器崩溃后被截断）。
            // 允许用合法数据覆写——拒绝写入会把用户锁在无法保存的死路上。
            _logger.LogWarning(ex, "Existing MCP settings file {Path} is corrupted; will overwrite with new data", _mcpYamlPath);
        }
    }
}
