using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed class ChatConversationPanelStateCoordinator
{
    private readonly Dictionary<string, ObservableCollection<TerminalPanelSessionViewModel>> _terminalSessionsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _selectedTerminalIdByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AskUserRequestViewModel> _pendingAskUserRequestsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElicitationRequestViewModel> _pendingElicitationRequestsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PermissionRequestViewModel>> _pendingPermissionRequestsByConversation = new(StringComparer.Ordinal);

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
            _pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var request) ? request : null,
            _pendingElicitationRequestsByConversation.TryGetValue(conversationId, out var elicitation) ? elicitation : null);
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

    public ElicitationRequestViewModel? GetPendingElicitationRequest(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        return _pendingElicitationRequestsByConversation.TryGetValue(conversationId, out var request)
            ? request
            : null;
    }

    public bool TryStoreElicitationRequest(string conversationId, ElicitationRequestViewModel request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);
        return _pendingElicitationRequestsByConversation.TryAdd(conversationId, request);
    }

    public bool RemoveElicitationRequest(string conversationId, ElicitationRequestViewModel request)
    {
        // A response can finish after disconnect and another form's arrival, even with the same id.
        // Only its original projection may be removed by that response callback.
        if (string.IsNullOrWhiteSpace(conversationId)
            || !ReferenceEquals(GetPendingElicitationRequest(conversationId), request))
        {
            return false;
        }

        return _pendingElicitationRequestsByConversation.Remove(conversationId);
    }

    public void ClearElicitationRequests()
        => _pendingElicitationRequestsByConversation.Clear();

    public PermissionRequestViewModel? GetPendingPermissionRequest(string? conversationId, string? toolCallId = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || !_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests))
        {
            return null;
        }

        RemoveUnavailablePermissionRequests(requests);
        var candidates = requests.Where(request => request.IsAwaitingInput && (toolCallId is null
            || string.Equals(request.ToolCallId, toolCallId, StringComparison.Ordinal)));
        return candidates.FirstOrDefault(static request => !request.IsCancellationOnly)
            ?? candidates.FirstOrDefault();
    }

    public void StorePermissionRequest(string conversationId, PermissionRequestViewModel request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);
        if (!_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests))
        {
            requests = [];
            _pendingPermissionRequestsByConversation.Add(conversationId, requests);
        }

        // The first unanswered request retains its surface; later requests wait or attach to their
        // own tool card. Invalidated SDK identities cannot keep an obsolete prompt in front.
        RemoveUnavailablePermissionRequests(requests);
        requests.Add(request);
    }

    public bool RemovePermissionRequest(string conversationId, PermissionRequestViewModel request)
    {
        if (!_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests)
            || !requests.Remove(request)) return false;
        request.DetachRequest();
        return true;
    }

    internal bool ContainsPermissionRequest(string conversationId, PermissionRequestViewModel request)
        => _pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests) && requests.Contains(request);

    internal IReadOnlyList<(string ConversationId, PermissionRequestViewModel Request)> GetObsoletePermissionRequests(
        IImmutableDictionary<string, ConversationBindingSlice>? bindings)
    {
        var obsolete = new List<(string, PermissionRequestViewModel)>();
        foreach (var (conversationId, requests) in _pendingPermissionRequestsByConversation)
        {
            var currentBinding = bindings?.GetValueOrDefault(conversationId);
            foreach (var request in requests)
            {
                if (request.Binding is not null && request.Binding != currentBinding && request.IsAvailable)
                {
                    obsolete.Add((conversationId, request));
                }
            }
        }
        return obsolete;
    }

    public void ClearPermissionRequests()
    {
        foreach (var requests in _pendingPermissionRequestsByConversation.Values)
        {
            foreach (var request in requests) request.DetachRequest();
        }
        _pendingPermissionRequestsByConversation.Clear();
    }

    internal void ReprojectPermissionLocalizedText(
        string defaultTitle, string cancellationTitle, string bindingCancellationDescription, string peerCancellationDescription)
    {
        foreach (var request in _pendingPermissionRequestsByConversation.Values.SelectMany(static requests => requests))
        {
            request.ReprojectLocalizedText(defaultTitle, cancellationTitle, bindingCancellationDescription, peerCancellationDescription);
        }
    }

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
        _pendingElicitationRequestsByConversation.Remove(conversationId);
        if (_pendingPermissionRequestsByConversation.Remove(conversationId, out var permissions))
        {
            foreach (var permission in permissions) permission.DetachRequest();
        }

        return isCurrentConversation ? EmptySelection() : NoUiChange();
    }

    private static void RemoveUnavailablePermissionRequests(List<PermissionRequestViewModel> requests)
    {
        for (var index = requests.Count - 1; index >= 0; index--)
        {
            if (requests[index].IsAvailable) continue;
            requests[index].DetachRequest();
            requests.RemoveAt(index);
        }
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
            null,
            null);

    private static ChatConversationPanelSelection NoUiChange()
        => new(
            new ObservableCollection<TerminalPanelSessionViewModel>(),
            null,
            null,
            null);
}
