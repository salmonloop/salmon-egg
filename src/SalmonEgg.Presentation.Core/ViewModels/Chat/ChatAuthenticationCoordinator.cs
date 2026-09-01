using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed record AuthenticationHintPresentation(
    string Message,
    string? ResourceKey = null,
    string? Fallback = null,
    object[]? FormatArgs = null);

public sealed class ChatAuthenticationCoordinator
{
    private IReadOnlyList<AuthMethodDefinition>? _advertisedAuthMethods;

    public void CacheAuthMethods(InitializeResponse initResponse)
    {
        ArgumentNullException.ThrowIfNull(initResponse);
        _advertisedAuthMethods = initResponse.AuthMethods;
    }

    public Task UpdateAgentInfoAsync(IChatService? chatService, IChatStore chatStore, string? selectedProfileId)
    {
        ArgumentNullException.ThrowIfNull(chatStore);

        if (chatService?.AgentInfo is not { } agentInfo)
        {
            return Task.CompletedTask;
        }

        return chatStore.Dispatch(new SetAgentIdentityAction(
            selectedProfileId,
            ResolveDisplayedAgentName(agentInfo),
            agentInfo.Version)).AsTask();
    }

    public void ClearAuthenticationRequirement(IAcpConnectionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        _ = coordinator.ClearAuthenticationRequiredAsync();
    }

    public void MarkAuthenticationRequired(
        IAcpConnectionCoordinator coordinator,
        ILogger logger,
        Action<string> showTransientNotificationToast,
        AuthMethodDefinition? method,
        AuthenticationHintPresentation? messageOverride = null,
        AuthenticationHintPresentation? requiredFallback = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(showTransientNotificationToast);

        var presentation =
            messageOverride
            ?? (method?.Description is { } description
                ? new AuthenticationHintPresentation(description)
                : null)
            ?? requiredFallback
            ?? new AuthenticationHintPresentation(
                "The agent requires authentication before it can respond.");

        _ = coordinator.SetAuthenticationRequiredAsync(
            presentation.Message,
            presentation.ResourceKey,
            presentation.Fallback,
            presentation.FormatArgs);

        if (method != null)
        {
            logger.LogInformation(
                "Agent requires authentication. id={MethodId}, name={Name}, hint={Hint}",
                method.Id,
                method.Name,
                presentation.Message);
        }
        else
        {
            logger.LogInformation(
                "Agent requires authentication but did not advertise a usable methodId. hint={Hint}",
                presentation.Message);
        }

        showTransientNotificationToast(presentation.Message);
    }

    public async Task<bool> TryAuthenticateAsync(
        IChatService? chatService,
        bool isInitialized,
        IAcpConnectionCoordinator coordinator,
        ILogger logger,
        Action<string> showTransientNotificationToast,
        CancellationToken cancellationToken,
        AuthenticationHintPresentation? requiredFallback = null,
        Func<string, AuthenticationHintPresentation>? formatAuthenticationFailed = null,
        AuthenticationHintPresentation? unsupportedMethodTypeFallback = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(showTransientNotificationToast);

        if (chatService is null || !isInitialized)
        {
            return false;
        }

        var method = GetPrimaryAuthMethod();
        if (method == null)
        {
            // Fail closed: the agent may have advertised methods we are forbidden or unable to use.
            // Report that as its own state instead of substituting an ineligible method.
            var ineligible = GetMethodsIneligibleForAuthenticate();
            if (ineligible.Count > 0)
            {
                logger.LogWarning(
                    "Agent advertised no authentication method that may be passed to authenticate. ineligibleTypes={IneligibleTypes}",
                    string.Join(", ", ineligible.Select(static candidate => candidate.ResolvedType)));
            }

            MarkAuthenticationRequired(
                coordinator,
                logger,
                showTransientNotificationToast,
                method: null,
                messageOverride: ineligible.Count > 0 ? unsupportedMethodTypeFallback : null,
                requiredFallback: requiredFallback);
            return false;
        }

        MarkAuthenticationRequired(
            coordinator,
            logger,
            showTransientNotificationToast,
            method,
            requiredFallback: requiredFallback);

        try
        {
            await chatService
                .AuthenticateAsync(new AuthenticateParams(method.Id), cancellationToken)
                .ConfigureAwait(false);

            ClearAuthenticationRequirement(coordinator);
            return true;
        }
        catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.MethodNotFound)
        {
            MarkAuthenticationRequired(
                coordinator,
                logger,
                showTransientNotificationToast,
                method,
                requiredFallback: requiredFallback);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authenticate failed");
            var failedPresentation = formatAuthenticationFailed is null
                ? new AuthenticationHintPresentation($"Authentication failed: {ex.Message}")
                : formatAuthenticationFailed(ex.Message);
            MarkAuthenticationRequired(
                coordinator,
                logger,
                showTransientNotificationToast,
                method,
                messageOverride: failedPresentation,
                requiredFallback: requiredFallback);
            return false;
        }
    }

    public static bool IsAuthenticationRequiredError(Exception ex)
        => ex is AcpException acp && acp.ErrorCode == JsonRpcErrorCode.AuthenticationRequired;

    /// <summary>
    /// Picks the first advertised method that may legally be passed to <c>authenticate</c>.
    /// </summary>
    /// <remarks>
    /// A method is eligible only when it carries an id and its ACP <c>AuthMethod</c> discriminator resolves
    /// to <see cref="AuthMethodDefinition.AgentType"/>. Terminal methods are excluded because the ACP schema
    /// states a client MUST NOT pass them to <c>authenticate</c>; unrecognized discriminators are excluded
    /// because their flow is undefined for this client. Ineligible methods must not be substituted for an
    /// eligible one, so this returns null rather than degrading to the first entry.
    /// </remarks>
    private AuthMethodDefinition? GetPrimaryAuthMethod()
        => _advertisedAuthMethods?.FirstOrDefault(static method =>
            !string.IsNullOrWhiteSpace(method.Id) && method.SupportsAuthenticateRequest);

    /// <summary>
    /// Advertised methods carrying an id that this client must not pass to <c>authenticate</c>.
    /// </summary>
    private IReadOnlyList<AuthMethodDefinition> GetMethodsIneligibleForAuthenticate()
        => _advertisedAuthMethods?
            .Where(static method =>
                !string.IsNullOrWhiteSpace(method.Id) && !method.SupportsAuthenticateRequest)
            .ToArray()
            ?? [];

    private static string? ResolveDisplayedAgentName(AgentInfo? agentInfo)
    {
        if (agentInfo is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(agentInfo.Title)
            ? agentInfo.Name
            : agentInfo.Title;
    }
}
