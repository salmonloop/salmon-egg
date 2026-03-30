using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;

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
    bool PreserveConversation)
{
    public static AcpConnectionContext None { get; } = new(null, PreserveConversation: false);

    public bool HasConversationTarget => !string.IsNullOrWhiteSpace(ConversationId);
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
        IAcpChatCoordinatorSink sink,
        System.Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default);

    Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        IAcpChatCoordinatorSink sink,
        System.Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default);

    Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory seam so the coordinator can be tested without depending on the concrete ChatServiceFactory.
/// A future DI adapter can wrap the existing factory without changing coordinator consumers.
/// </summary>
public interface IAcpChatServiceFactory
{
    IChatService CreateChatService(
        TransportType transportType,
        string? command = null,
        string? args = null,
        string? url = null);
}
