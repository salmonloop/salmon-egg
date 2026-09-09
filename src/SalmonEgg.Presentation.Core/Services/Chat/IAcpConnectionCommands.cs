using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpTransportApplyResult(
    SalmonEgg.Application.Services.Chat.IChatService ChatService,
    InitializeResponse InitializeResponse);

public readonly record struct AcpRemoteSessionResult(
    string RemoteSessionId,
    SessionNewResponse Session,
    bool UsedExistingBinding);

public readonly record struct AcpPromptDispatchResult(
    string RemoteSessionId,
    SessionPromptResponse Response,
    bool RetriedAfterSessionRecovery);

public readonly record struct AcpConnectionContext(
    string? ConversationId,
    bool PreserveConversation,
    long? ActivationVersion = null)
{
    public static AcpConnectionContext None { get; } = new(null, PreserveConversation: false);

    public bool HasConversationTarget => !string.IsNullOrWhiteSpace(ConversationId);

    /// <summary>Interactive sign-in changes the agent's credentials; an existing pooled process cannot observe them.</summary>
    public bool ForceReconnect { get; init; }

    public IChatService? ExpectedChatService { get; init; }

    public string? ExpectedConnectionInstanceId { get; init; }

    public string? ExpectedProfileId { get; init; }

    public bool MatchesExpectedConnection(IAcpChatCoordinatorSink sink)
        => !ForceReconnect || (ReferenceEquals(ExpectedChatService, sink.CurrentChatService)
            && string.Equals(ExpectedConnectionInstanceId, sink.ConnectionInstanceId, System.StringComparison.Ordinal)
            && string.Equals(ExpectedProfileId, sink.SelectedProfileId, System.StringComparison.Ordinal)
            && string.Equals(ConversationId, sink.CurrentSessionId, System.StringComparison.Ordinal));
}

/// <summary>
/// ACP coordinator command surface.
/// This is intentionally service-oriented so ChatViewModel can delegate behavior without a MVUX rewrite.
/// </summary>
public interface IAcpConnectionCommands
{
    Task<AcpTransportApplyResult> ConnectToProfileAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default);

    Task<AcpTransportApplyResult> ConnectToProfileAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken = default);

    Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        bool preserveConversation,
        CancellationToken cancellationToken = default);

    Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken = default);

    Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        System.Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default);

    Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        string? promptMessageId,
        IAcpChatCoordinatorSink sink,
        System.Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default);

    Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        string? promptMessageId,
        IAcpChatCoordinatorSink sink,
        System.Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default);

    Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default);

    /// <summary>Ends only the service whose undisplayed interaction could not be cancelled.</summary>
    /// <remarks>The default keeps existing coordinators source compatible without redirecting failure to a newer service.</remarks>
    Task DisconnectAfterInteractionFailureAsync(
        IChatService expectedService,
        IAcpChatCoordinatorSink sink,
        string errorMessage)
        => AcpInteractionFailureCleanup.DisconnectAsync(expectedService, sink, errorMessage);

    Task<AcpTransportApplyResult> ConnectProfileInPoolAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        CancellationToken cancellationToken = default);

    Task DisconnectProfileInPoolAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory seam so the coordinator can be tested without depending on the concrete ChatServiceFactory.
/// A future DI adapter can wrap the existing factory without changing coordinator consumers.
/// </summary>
public interface IAcpChatServiceFactory
{
    IChatService CreateChatService(ServerConfiguration configuration);

    IChatService CreateChatService(
        TransportType transportType,
        string? command = null,
        IReadOnlyList<string>? arguments = null,
        string? url = null);
}
