using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpMcpServerProvider
{
    Task<IReadOnlyList<McpServer>> GetMcpServersAsync(
        ServerConfiguration? profile,
        CancellationToken cancellationToken = default);
}

public sealed class GlobalAcpMcpServerProvider : IAcpMcpServerProvider
{
    private readonly IMcpSettingsService _settingsService;

    public GlobalAcpMcpServerProvider(IMcpSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task<IReadOnlyList<McpServer>> GetMcpServersAsync(
        ServerConfiguration? profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.IsEnabled
            ? McpServerJsonConverter.CloneServers(settings.Servers)
            : Array.Empty<McpServer>();
    }
}

public interface IAcpMcpServerResolver
{
    Task<IReadOnlyList<McpServer>> ResolveCurrentMcpServersAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default);
}

public sealed class AcpMcpServerResolver : IAcpMcpServerResolver
{
    private readonly IAcpMcpServerProvider _provider;

    public AcpMcpServerResolver(IAcpMcpServerProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<IReadOnlyList<McpServer>> ResolveCurrentMcpServersAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var servers = await _provider.GetMcpServersAsync(profile: null, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = McpServerJsonConverter.CloneServers(servers);
        sink.SetCurrentMcpServers(snapshot);
        return snapshot;
    }
}
