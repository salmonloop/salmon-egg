using System;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal readonly record struct AcpAuthoritativeConnectionSnapshot(
    IChatService ChatService,
    string? ProfileId,
    string? ConnectionInstanceId);

internal sealed class AcpAuthoritativeConnectionResolver
{
    private readonly IAcpConnectionSessionRegistry? _connectionSessionRegistry;

    public AcpAuthoritativeConnectionResolver(IAcpConnectionSessionRegistry? connectionSessionRegistry)
    {
        _connectionSessionRegistry = connectionSessionRegistry;
    }

    public bool TryResolveReadyForegroundConnection(
        IChatService? foregroundChatService,
        ChatConnectionState connectionState,
        string? requiredProfileId,
        out AcpAuthoritativeConnectionSnapshot snapshot)
    {
        snapshot = default;

        if (foregroundChatService is not { IsConnected: true, IsInitialized: true })
        {
            return false;
        }

        if (connectionState.Phase is ConnectionPhase.Connecting or ConnectionPhase.Initializing)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(connectionState.Error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredProfileId))
        {
            snapshot = new(
                foregroundChatService,
                connectionState.ForegroundTransportProfileId,
                connectionState.ConnectionInstanceId);
            return true;
        }

        if (!string.Equals(requiredProfileId, connectionState.ForegroundTransportProfileId, StringComparison.Ordinal))
        {
            return false;
        }

        if (_connectionSessionRegistry is null)
        {
            snapshot = new(
                foregroundChatService,
                requiredProfileId,
                connectionState.ConnectionInstanceId);
            return true;
        }

        if (!_connectionSessionRegistry.TryGetByProfile(requiredProfileId, out var session))
        {
            return false;
        }

        if (!session.Service.IsConnected || !session.Service.IsInitialized)
        {
            return false;
        }

        if (!ReferenceEquals(session.Service, foregroundChatService))
        {
            return false;
        }

        if (!ConnectionInstanceMatches(connectionState.ConnectionInstanceId, session.ConnectionInstanceId))
        {
            return false;
        }

        snapshot = new(
            session.Service,
            requiredProfileId,
            session.ConnectionInstanceId ?? connectionState.ConnectionInstanceId);
        return true;
    }

    private static bool ConnectionInstanceMatches(string? foregroundConnectionInstanceId, string? authoritativeConnectionInstanceId)
    {
        if (string.IsNullOrWhiteSpace(foregroundConnectionInstanceId)
            || string.IsNullOrWhiteSpace(authoritativeConnectionInstanceId))
        {
            return string.IsNullOrWhiteSpace(foregroundConnectionInstanceId)
                && string.IsNullOrWhiteSpace(authoritativeConnectionInstanceId);
        }

        return string.Equals(
            foregroundConnectionInstanceId,
            authoritativeConnectionInstanceId,
            StringComparison.Ordinal);
    }
}
