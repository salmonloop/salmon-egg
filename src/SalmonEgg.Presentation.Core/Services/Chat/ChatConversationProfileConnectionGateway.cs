using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatConversationProfileConnectionGateway
{
    public AcpConnectionContext CreateConnectionContext(
        string? conversationId,
        ConversationBindingSlice? binding,
        string? profileId,
        bool preserveConversation,
        long? activationVersion = null)
    {
        if (!preserveConversation || string.IsNullOrWhiteSpace(conversationId))
        {
            return new AcpConnectionContext(conversationId, PreserveConversation: false, ActivationVersion: activationVersion);
        }

        var hasMatchingRemoteBinding =
            !string.IsNullOrWhiteSpace(binding?.RemoteSessionId)
            && !string.IsNullOrWhiteSpace(binding?.ProfileId)
            && string.Equals(binding.ProfileId, profileId, StringComparison.Ordinal);

        return new AcpConnectionContext(conversationId, hasMatchingRemoteBinding, ActivationVersion: activationVersion);
    }

    public Task<AcpTransportApplyResult> ConnectAsync(
        IAcpConnectionCommands connectionCommands,
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionCommands);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);

        return connectionContext.Equals(AcpConnectionContext.None)
            ? connectionCommands.ConnectToProfileAsync(profile, transportConfiguration, sink, cancellationToken)
            : connectionCommands.ConnectToProfileAsync(profile, transportConfiguration, sink, connectionContext, cancellationToken);
    }
}
