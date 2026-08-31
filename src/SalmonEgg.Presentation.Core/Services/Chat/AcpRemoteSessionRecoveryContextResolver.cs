using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Persisted recovery metadata used only when the connected agent cannot provide an authoritative session snapshot.
/// </summary>
public readonly record struct AcpRemoteSessionRecoveryFallback(
    string? Cwd,
    IReadOnlyList<string>? AdditionalDirectories);

/// <summary>
/// Immutable protocol context for one remote session recovery request.
/// </summary>
public readonly record struct AcpRemoteSessionRecoveryContext(
    string Cwd,
    ImmutableArray<string> AdditionalDirectories);

/// <summary>
/// Result of resolving remote session recovery facts from the connected agent or persisted fallback metadata.
/// </summary>
public readonly record struct AcpRemoteSessionRecoveryResolution(
    AcpRemoteSessionRecoveryContext? Context,
    AgentSessionInfo? AuthoritativeSessionInfo);

/// <summary>
/// Resolves the authoritative ACP recovery context shared by normal hydration and stream resynchronization.
/// </summary>
public interface IAcpRemoteSessionRecoveryContextResolver
{
    Task<AcpRemoteSessionRecoveryResolution> ResolveAsync(
        IChatService chatService,
        string remoteSessionId,
        AcpRemoteSessionRecoveryFallback fallback,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AcpRemoteSessionRecoveryContextResolver : IAcpRemoteSessionRecoveryContextResolver
{
    private readonly ILogger<AcpRemoteSessionRecoveryContextResolver> _logger;

    public AcpRemoteSessionRecoveryContextResolver(
        ILogger<AcpRemoteSessionRecoveryContextResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AcpRemoteSessionRecoveryResolution> ResolveAsync(
        IChatService chatService,
        string remoteSessionId,
        AcpRemoteSessionRecoveryFallback fallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            throw new ArgumentException("Remote session id must not be empty.", nameof(remoteSessionId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var authoritativeSessionInfo = await TryFindAuthoritativeSessionInfoAsync(
                chatService,
                remoteSessionId,
                cancellationToken)
            .ConfigureAwait(false);
        var cwd = authoritativeSessionInfo?.Cwd ?? fallback.Cwd;
        IReadOnlyCollection<string>? additionalDirectories = authoritativeSessionInfo is null
            ? fallback.AdditionalDirectories
            : authoritativeSessionInfo.AdditionalDirectories is { } directories
                ? directories
                : Array.Empty<string>();
        var context = CreateContext(
            cwd,
            additionalDirectories,
            chatService.AgentCapabilities?.SupportsSessionAdditionalDirectories == true);

        return new AcpRemoteSessionRecoveryResolution(context, authoritativeSessionInfo);
    }

    private static AcpRemoteSessionRecoveryContext? CreateContext(
        string? cwd,
        IReadOnlyCollection<string>? additionalDirectories,
        bool supportsAdditionalDirectories)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        var effectiveAdditionalDirectories = supportsAdditionalDirectories
            && additionalDirectories is { Count: > 0 }
                ? additionalDirectories.ToImmutableArray()
                : ImmutableArray<string>.Empty;
        return new AcpRemoteSessionRecoveryContext(cwd.Trim(), effectiveAdditionalDirectories);
    }

    private async Task<AgentSessionInfo?> TryFindAuthoritativeSessionInfoAsync(
        IChatService chatService,
        string remoteSessionId,
        CancellationToken cancellationToken)
    {
        if (chatService.AgentCapabilities?.SupportsSessionList != true)
        {
            return null;
        }

        try
        {
            return await FindAuthoritativeSessionInfoAsync(chatService, remoteSessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve authoritative ACP session metadata; persisted recovery metadata will be used. RemoteSessionId={RemoteSessionId}",
                remoteSessionId);
            return null;
        }
    }

    private async Task<AgentSessionInfo?> FindAuthoritativeSessionInfoAsync(
        IChatService chatService,
        string remoteSessionId,
        CancellationToken cancellationToken)
    {
        var visitedCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await chatService
                .ListSessionsAsync(new SessionListParams { Cursor = cursor }, cancellationToken)
                .ConfigureAwait(false);
            var match = response.Sessions.FirstOrDefault(session =>
                string.Equals(session.SessionId, remoteSessionId, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }

            cursor = response.NextCursor;
            if (!string.IsNullOrWhiteSpace(cursor) && !visitedCursors.Add(cursor))
            {
                _logger.LogWarning(
                    "Stopping ACP session/list pagination because the agent repeated a cursor. RemoteSessionId={RemoteSessionId} Cursor={Cursor}",
                    remoteSessionId,
                    cursor);
                return null;
            }
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return null;
    }
}

/// <summary>
/// Creates ACP recovery requests from a capability-resolved context.
/// </summary>
public static class AcpRemoteSessionRecoveryRequestFactory
{
    public static SessionLoadParams CreateLoadParams(
        string remoteSessionId,
        AcpRemoteSessionRecoveryContext context,
        IReadOnlyList<McpServer> mcpServers)
        => new(
            remoteSessionId,
            context.Cwd,
            McpServerSnapshots.CloneServers(mcpServers),
            CreateAdditionalDirectories(context));

    public static SessionResumeParams CreateResumeParams(
        string remoteSessionId,
        AcpRemoteSessionRecoveryContext context,
        IReadOnlyList<McpServer> mcpServers,
        SessionReplayFrom? replayFrom = null)
        => new(
            remoteSessionId,
            context.Cwd,
            McpServerSnapshots.CloneServers(mcpServers),
            CreateAdditionalDirectories(context),
            replayFrom);

    private static List<string>? CreateAdditionalDirectories(AcpRemoteSessionRecoveryContext context)
        => context.AdditionalDirectories.IsDefaultOrEmpty
            ? null
            : context.AdditionalDirectories.ToList();
}
