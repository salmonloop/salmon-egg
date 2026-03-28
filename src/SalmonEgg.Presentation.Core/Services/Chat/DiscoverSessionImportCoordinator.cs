using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IDiscoverSessionImportCoordinator
{
    Task<DiscoverSessionImportResult> ImportAsync(
        string remoteSessionId,
        string? remoteSessionCwd,
        string? profileId,
        string? remoteSessionTitle = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct DiscoverSessionImportResult(
    bool Succeeded,
    string? LocalConversationId,
    string? ErrorMessage);

public sealed class DiscoverSessionImportCoordinator : IDiscoverSessionImportCoordinator
{
    private readonly ISessionManager _sessionManager;
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IConversationBindingCommands _bindingCommands;
    private readonly ILogger<DiscoverSessionImportCoordinator> _logger;

    public DiscoverSessionImportCoordinator(
        ISessionManager sessionManager,
        ChatConversationWorkspace conversationWorkspace,
        IConversationBindingCommands bindingCommands,
        ILogger<DiscoverSessionImportCoordinator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationWorkspace = conversationWorkspace ?? throw new ArgumentNullException(nameof(conversationWorkspace));
        _bindingCommands = bindingCommands ?? throw new ArgumentNullException(nameof(bindingCommands));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DiscoverSessionImportResult> ImportAsync(
        string remoteSessionId,
        string? remoteSessionCwd,
        string? profileId,
        string? remoteSessionTitle = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return new DiscoverSessionImportResult(false, null, "RemoteSessionIdMissing");
        }

        var localConversationId = Guid.NewGuid().ToString("N");

        try
        {
            await _sessionManager.CreateSessionAsync(localConversationId, remoteSessionCwd).ConfigureAwait(false);
            var sanitizedTitle = SessionNamePolicy.Sanitize(remoteSessionTitle);
            if (!string.IsNullOrWhiteSpace(sanitizedTitle))
            {
                _sessionManager.UpdateSession(
                    localConversationId,
                    session => session.DisplayName = sanitizedTitle,
                    updateActivity: false);
            }

            await _conversationWorkspace.RegisterConversationAsync(
                localConversationId,
                createdAt: DateTime.UtcNow,
                lastUpdatedAt: DateTime.UtcNow,
                cancellationToken).ConfigureAwait(false);

            var bindingResult = await _bindingCommands
                .UpdateBindingAsync(localConversationId, remoteSessionId.Trim(), profileId)
                .ConfigureAwait(false);
            if (bindingResult.Status is not BindingUpdateStatus.Success)
            {
                RollBackImportedConversation(localConversationId);
                return new DiscoverSessionImportResult(
                    false,
                    null,
                    bindingResult.ErrorMessage ?? $"BindingUpdateFailed:{bindingResult.Status}");
            }

            return new DiscoverSessionImportResult(true, localConversationId, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to import discovered remote session {RemoteSessionId}",
                remoteSessionId);
            return new DiscoverSessionImportResult(false, null, ex.Message);
        }
    }

    private void RollBackImportedConversation(string localConversationId)
    {
        if (string.IsNullOrWhiteSpace(localConversationId))
        {
            return;
        }

        try
        {
            _conversationWorkspace.DeleteConversation(localConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to roll back imported conversation workspace state {ConversationId}",
                localConversationId);
            _sessionManager.RemoveSession(localConversationId);
        }
    }
}
