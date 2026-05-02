using System;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed class ChatSessionHeaderActionCoordinator
{
    public bool TryApplyProjectAffinityOverride(
        ChatConversationWorkspace conversationWorkspace,
        string? currentSessionId,
        string? selectedProjectId)
    {
        ArgumentNullException.ThrowIfNull(conversationWorkspace);

        if (string.IsNullOrWhiteSpace(currentSessionId)
            || string.IsNullOrWhiteSpace(selectedProjectId))
        {
            return false;
        }

        conversationWorkspace.UpdateProjectAffinityOverride(currentSessionId, selectedProjectId);
        return true;
    }

    public bool TryClearProjectAffinityOverride(
        ChatConversationWorkspace conversationWorkspace,
        string? currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(conversationWorkspace);

        if (string.IsNullOrWhiteSpace(currentSessionId))
        {
            return false;
        }

        conversationWorkspace.UpdateProjectAffinityOverride(currentSessionId, null);
        return true;
    }

    public bool TryBeginEditSessionName(
        bool isSessionActive,
        string? currentSessionId,
        string currentSessionDisplayName,
        out string editingSessionName)
    {
        if (!isSessionActive || string.IsNullOrWhiteSpace(currentSessionId))
        {
            editingSessionName = string.Empty;
            return false;
        }

        editingSessionName = currentSessionDisplayName ?? string.Empty;
        return true;
    }

    public string CommitSessionName(
        string sessionId,
        string editingSessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sanitized = SessionNamePolicy.Sanitize(editingSessionName);
        return string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(sessionId)
            : sanitized;
    }
}
