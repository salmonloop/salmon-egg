using System;
using System.Text.Json;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed class ChatTerminalProjectionCoordinator
{
    public bool TryApplyRequest(
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string conversationId,
        TerminalRequestEventArgs request,
        bool isCurrentConversation,
        out ChatConversationPanelSelection selection)
    {
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        ArgumentNullException.ThrowIfNull(request);

        selection = EmptySelection();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        var terminalId = string.IsNullOrWhiteSpace(request.TerminalId)
            ? TryReadTerminalId(request.RawParams)
            : request.TerminalId;
        if (string.IsNullOrWhiteSpace(terminalId))
        {
            return false;
        }

        var terminal = panelStateCoordinator.GetOrCreateTerminalSession(conversationId, terminalId);
        terminal.LastMethod = request.Method ?? string.Empty;
        ApplyTerminalPayload(terminal, request.RawParams);
        selection = panelStateCoordinator.SelectTerminal(conversationId, terminal, isCurrentConversation);
        return true;
    }

    public bool TryApplyState(
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string conversationId,
        TerminalStateChangedEventArgs update,
        bool isCurrentConversation,
        out ChatConversationPanelSelection selection)
    {
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        ArgumentNullException.ThrowIfNull(update);

        selection = EmptySelection();
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(update.TerminalId))
        {
            return false;
        }

        var terminal = panelStateCoordinator.GetOrCreateTerminalSession(conversationId, update.TerminalId);
        terminal.LastMethod = update.Method ?? string.Empty;

        if (update.Output != null)
        {
            terminal.Output = update.Output;
        }

        if (update.Truncated.HasValue)
        {
            terminal.IsTruncated = update.Truncated.Value;
        }

        if (update.ExitStatus != null)
        {
            terminal.ExitCode = update.ExitStatus.ExitCode;
        }

        if (update.IsReleased)
        {
            terminal.IsReleased = true;
        }

        selection = panelStateCoordinator.SelectTerminal(conversationId, terminal, isCurrentConversation);
        return true;
    }

    private static string? TryReadTerminalId(object? rawParams)
    {
        return TryGetRawParamsElement(rawParams, out var element)
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("terminalId", out var terminalId)
            && terminalId.ValueKind == JsonValueKind.String
                ? terminalId.GetString()
                : null;
    }

    private static void ApplyTerminalPayload(TerminalPanelSessionViewModel terminal, object? rawParams)
    {
        if (!TryGetRawParamsElement(rawParams, out var rawElement) || rawElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (rawElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
        {
            terminal.Output = output.GetString() ?? string.Empty;
        }

        if (rawElement.TryGetProperty("truncated", out var truncated)
            && truncated.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            terminal.IsTruncated = truncated.GetBoolean();
        }

        if (rawElement.TryGetProperty("exitStatus", out var exitStatus)
            && exitStatus.ValueKind == JsonValueKind.Object
            && exitStatus.TryGetProperty("exitCode", out var exitCode)
            && exitCode.ValueKind == JsonValueKind.Number
            && exitCode.TryGetInt32(out var value))
        {
            terminal.ExitCode = value;
        }

        if (string.Equals(terminal.LastMethod, "terminal/release", StringComparison.Ordinal))
        {
            terminal.IsReleased = true;
        }
    }

    private static bool TryGetRawParamsElement(object? rawParams, out JsonElement element)
    {
        switch (rawParams)
        {
            case JsonElement jsonElement:
                element = jsonElement;
                return true;
            case JsonDocument jsonDocument:
                element = jsonDocument.RootElement;
                return true;
            default:
                element = default;
                return false;
        }
    }

    private static ChatConversationPanelSelection EmptySelection()
        => new(
            new(),
            null,
            new(),
            null,
            null);
}
