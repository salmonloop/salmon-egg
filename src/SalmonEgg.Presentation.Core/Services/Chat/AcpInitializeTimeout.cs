using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class AcpInitializeTimeout
{
    public static TimeSpan Resolve(ServerConfiguration? profile)
        => AcpConnectionTimeoutPolicy.ResolveTimeout(profile?.ConnectionTimeout ?? 0);

    public static async Task<InitializeResponse> WaitForInitializeAsync(
        IChatService chatService,
        TransportType transportType,
        string? profileId,
        string? conversationId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await chatService
                .InitializeAsync(AcpInitializeRequestFactory.CreateDefault())
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(CreateTimeoutMessage(transportType, profileId, conversationId, timeout), ex);
        }
    }

    public static string CreateTimeoutMessage(
        TransportType transportType,
        string? profileId,
        string? conversationId,
        TimeSpan timeout)
        => "Timed out waiting for ACP initialize response. "
            + $"profileId={profileId ?? "(none)"} "
            + $"transport={transportType} "
            + $"timeoutSeconds={timeout.TotalSeconds:0.###} "
            + $"conversationId={conversationId ?? "(none)"}";
}
