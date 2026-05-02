using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat;

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
        string? messageOverride = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(showTransientNotificationToast);

        var message =
            messageOverride
            ?? method?.Description
            ?? "The agent requires authentication before it can respond.";

        _ = coordinator.SetAuthenticationRequiredAsync(message);

        if (method != null)
        {
            logger.LogInformation(
                "Agent requires authentication. id={MethodId}, name={Name}, hint={Hint}",
                method.Id,
                method.Name,
                message);
        }
        else
        {
            logger.LogInformation("Agent requires authentication but did not advertise a usable methodId. hint={Hint}", message);
        }

        showTransientNotificationToast(message);
    }

    public async Task<bool> TryAuthenticateAsync(
        IChatService? chatService,
        bool isInitialized,
        IAcpConnectionCoordinator coordinator,
        ILogger logger,
        Action<string> showTransientNotificationToast,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(showTransientNotificationToast);

        if (chatService is null || !isInitialized)
        {
            return false;
        }

        var method = GetPrimaryAuthMethod();
        if (method == null || string.IsNullOrWhiteSpace(method.Id))
        {
            MarkAuthenticationRequired(coordinator, logger, showTransientNotificationToast, method);
            return false;
        }

        MarkAuthenticationRequired(coordinator, logger, showTransientNotificationToast, method);

        try
        {
            var response = await chatService
                .AuthenticateAsync(new AuthenticateParams(method.Id), cancellationToken)
                .ConfigureAwait(false);

            if (response.Authenticated)
            {
                ClearAuthenticationRequirement(coordinator);
                return true;
            }

            MarkAuthenticationRequired(coordinator, logger, showTransientNotificationToast, method, response.Message);
            return false;
        }
        catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.MethodNotFound)
        {
            MarkAuthenticationRequired(coordinator, logger, showTransientNotificationToast, method);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authenticate failed");
            MarkAuthenticationRequired(coordinator, logger, showTransientNotificationToast, method, $"Authentication failed: {ex.Message}");
            return false;
        }
    }

    public static bool IsAuthenticationRequiredError(Exception ex)
        => ex is AcpException acp && acp.ErrorCode == JsonRpcErrorCode.AuthenticationRequired;

    private AuthMethodDefinition? GetPrimaryAuthMethod()
        => _advertisedAuthMethods?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Id));

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
