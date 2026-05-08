using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed class ChatConversationPanelStateCoordinator
{
    private readonly Dictionary<string, ObservableCollection<TerminalPanelSessionViewModel>> _terminalSessionsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _selectedTerminalIdByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AskUserRequestViewModel> _pendingAskUserRequestsByConversation = new(StringComparer.Ordinal);

    public ChatConversationPanelSelection SyncConversation(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return EmptySelection();
        }

        if (!_terminalSessionsByConversation.TryGetValue(conversationId, out var sessions))
        {
            sessions = new ObservableCollection<TerminalPanelSessionViewModel>();
            _terminalSessionsByConversation[conversationId] = sessions;
        }

        return new ChatConversationPanelSelection(
            sessions,
            ResolveSelectedTerminal(conversationId, sessions),
            _pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var request) ? request : null);
    }

    public AskUserRequestViewModel? GetPendingAskUserRequest(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        return _pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var request)
            ? request
            : null;
    }

    public void StoreAskUserRequest(string conversationId, AskUserRequestViewModel request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);
        _pendingAskUserRequestsByConversation[conversationId] = request;
    }

    public void RemoveAskUserRequest(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _pendingAskUserRequestsByConversation.Remove(conversationId);
    }

    public void ClearAskUserRequests()
        => _pendingAskUserRequestsByConversation.Clear();

    public TerminalPanelSessionViewModel GetOrCreateTerminalSession(string conversationId, string terminalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);

        if (!_terminalSessionsByConversation.TryGetValue(conversationId, out var sessions))
        {
            sessions = new ObservableCollection<TerminalPanelSessionViewModel>();
            _terminalSessionsByConversation[conversationId] = sessions;
        }

        var terminal = sessions.FirstOrDefault(session =>
            string.Equals(session.TerminalId, terminalId, StringComparison.Ordinal));
        if (terminal != null)
        {
            return terminal;
        }

        terminal = new TerminalPanelSessionViewModel(terminalId)
        {
            DisplayName = terminalId
        };
        sessions.Add(terminal);
        return terminal;
    }

    public ChatConversationPanelSelection SelectTerminal(string conversationId, TerminalPanelSessionViewModel terminal, bool isCurrentConversation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(terminal);

        _selectedTerminalIdByConversation[conversationId] = terminal.TerminalId;

        return isCurrentConversation
            ? SyncConversation(conversationId)
            : new ChatConversationPanelSelection(
                new ObservableCollection<TerminalPanelSessionViewModel>(),
                null,
                null);
    }

    public ChatConversationPanelSelection RemoveConversation(string conversationId, bool isCurrentConversation)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return isCurrentConversation ? EmptySelection() : NoUiChange();
        }

        _terminalSessionsByConversation.Remove(conversationId);
        _selectedTerminalIdByConversation.Remove(conversationId);
        _pendingAskUserRequestsByConversation.Remove(conversationId);

        return isCurrentConversation ? EmptySelection() : NoUiChange();
    }

    private TerminalPanelSessionViewModel? ResolveSelectedTerminal(
        string conversationId,
        ObservableCollection<TerminalPanelSessionViewModel> sessions)
    {
        if (_selectedTerminalIdByConversation.TryGetValue(conversationId, out var selectedId))
        {
            var selected = sessions.FirstOrDefault(session =>
                string.Equals(session.TerminalId, selectedId, StringComparison.Ordinal));
            if (selected != null)
            {
                return selected;
            }
        }

        return sessions.LastOrDefault();
    }

    private static ChatConversationPanelSelection EmptySelection()
        => new(
            new ObservableCollection<TerminalPanelSessionViewModel>(),
            null,
            null);

    private static ChatConversationPanelSelection NoUiChange()
        => new(
            new ObservableCollection<TerminalPanelSessionViewModel>(),
            null,
            null);
}
