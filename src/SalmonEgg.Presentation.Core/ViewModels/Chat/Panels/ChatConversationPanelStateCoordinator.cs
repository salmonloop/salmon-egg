using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public readonly record struct ConversationInteractionSummary(
    bool HasPermissionRequest,
    bool HasInputRequest,
    bool HasFailure,
    DateTime? ActivityAtUtc = null);

public sealed class ChatConversationPanelStateCoordinator
{
    private readonly IUiDispatcher? _dispatcher;
    private readonly IAcpConnectionSessionRegistry? _sessionRegistry;
    private readonly Dictionary<object, IDisposable> _requestUsageLeases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ObservableCollection<TerminalPanelSessionViewModel>> _terminalSessionsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _selectedTerminalIdByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AskUserRequestViewModel> _pendingAskUserRequestsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElicitationRequestViewModel> _pendingElicitationRequestsByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PermissionRequestViewModel>> _pendingPermissionRequestsByConversation = new(StringComparer.Ordinal);
    private readonly List<PermissionRequestViewModel> _unboundPermissionCancellations = [];

    public ChatConversationPanelStateCoordinator(IUiDispatcher? dispatcher = null, IAcpConnectionSessionRegistry? sessionRegistry = null)
    {
        _dispatcher = dispatcher;
        _sessionRegistry = sessionRegistry;
    }

    public event Action<string>? Changed;

    public ConversationInteractionSummary GetSummary(string conversationId)
    {
        _pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var permissions);
        var permission = false;
        var askUser = GetPendingAskUserRequest(conversationId);
        var elicitation = GetPendingElicitationRequest(conversationId);
        var hasElicitation = elicitation is { IsCompleted: false }
            && (elicitation.CanRespond || elicitation.CanCancel || elicitation.IsSubmitting || elicitation.IsAwaitingCompletion);
        DateTime? activityAt = askUser?.ActivityAtUtc;
        if (hasElicitation && (activityAt is null || elicitation!.ActivityAtUtc > activityAt.Value))
            activityAt = elicitation!.ActivityAtUtc;
        if (permissions is not null)
        {
            foreach (var request in permissions)
            {
                if (!request.IsAvailable) continue;
                permission = true;
                if (activityAt is null || request.ActivityAtUtc > activityAt.Value) activityAt = request.ActivityAtUtc;
            }
        }
        return new(
            permission,
            askUser is not null || hasElicitation,
            askUser?.HasError == true || elicitation?.HasError == true
                || permissions?.Any(static request => request.IsAvailable && request.IsCancellationOnly) == true,
            activityAt);
    }

    public async ValueTask<IReadOnlyList<AcpSessionEventSource>> GetPendingConnectionSourcesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            return GetPendingConnectionSources();
        }

        IReadOnlyList<AcpSessionEventSource> sources = Array.Empty<AcpSessionEventSource>();
        await _dispatcher.EnqueueAsync(() => sources = GetPendingConnectionSources()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return sources;
    }

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
        if (!TryHoldRequestUsage(request, request.Source)) return;
        if (_pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var previous))
        {
            previous.PropertyChanged -= OnRequestPropertyChanged;
            if (!ReferenceEquals(previous, request)) ReleaseRequestUsage(previous);
        }

        _pendingAskUserRequestsByConversation[conversationId] = request;
        request.PropertyChanged += OnRequestPropertyChanged;
        NotifyChanged(conversationId);
    }

    public void RemoveAskUserRequest(string conversationId, AskUserRequestViewModel? expectedRequest = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (_pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var request)
            && (expectedRequest is null || ReferenceEquals(request, expectedRequest)))
        {
            _pendingAskUserRequestsByConversation.Remove(conversationId);
            request.PropertyChanged -= OnRequestPropertyChanged;
            ReleaseRequestUsage(request);
            NotifyChanged(conversationId);
        }
    }

    public void ClearAskUserRequests()
    {
        foreach (var conversationId in _pendingAskUserRequestsByConversation.Keys.ToArray())
        {
            RemoveAskUserRequest(conversationId);
        }
    }

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
        var existing = GetPendingElicitationRequest(conversationId);
        if (existing is not null && !existing.IsAwaitingCompletion) return false;
        if (!TryHoldRequestUsage(request, request.Source)) return false;
        if (existing is { IsAwaitingCompletion: true })
        {
            existing.PropertyChanged -= OnRequestPropertyChanged;
            ReleaseRequestUsage(existing);
            existing.Dispose();
            _pendingElicitationRequestsByConversation.Remove(conversationId);
        }

        if (!_pendingElicitationRequestsByConversation.TryAdd(conversationId, request))
        {
            ReleaseRequestUsage(request);
            return false;
        }

        request.PropertyChanged += OnRequestPropertyChanged;
        NotifyChanged(conversationId);
        return true;
    }

    internal IReadOnlyList<ElicitationRequestViewModel> GetElicitationRequests()
        => _pendingElicitationRequestsByConversation.Values.ToArray();

    public bool RemoveElicitationRequest(string conversationId, ElicitationRequestViewModel request)
    {
        // A response can finish after disconnect and another form's arrival, even with the same id.
        // Only its original projection may be removed by that response callback.
        if (string.IsNullOrWhiteSpace(conversationId)
            || !ReferenceEquals(GetPendingElicitationRequest(conversationId), request))
        {
            return false;
        }

        _pendingElicitationRequestsByConversation.Remove(conversationId);
        request.PropertyChanged -= OnRequestPropertyChanged;
        ReleaseRequestUsage(request);
        request.Dispose();
        NotifyChanged(conversationId);
        return true;
    }

    public void ClearElicitationRequests()
    {
        foreach (var (conversationId, request) in _pendingElicitationRequestsByConversation.ToArray())
        {
            RemoveElicitationRequest(conversationId, request);
        }
    }

    public PermissionRequestViewModel? GetPendingPermissionRequest(string? conversationId, string? toolCallId = null)
        => GetPendingPermissionRequest(conversationId, toolCallId, currentRequest: null);

    internal PermissionRequestViewModel? GetPendingPermissionRequest(
        string? conversationId, string? toolCallId, PermissionRequestViewModel? currentRequest)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || !_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests))
        {
            return null;
        }

        RemoveUnavailablePermissionRequests(requests);
        if (currentRequest is { IsSendingResponse: true } && requests.Contains(currentRequest)
            && (toolCallId is null || string.Equals(currentRequest.ToolCallId, toolCallId, StringComparison.Ordinal)))
        {
            // Every prepared sibling becomes sending together. Preserve the visible member of
            // that batch until delivery instead of jumping back to its first answered question.
            return currentRequest;
        }
        var candidates = requests.Where(request => (request.IsAwaitingInput || request.IsSendingResponse) && (toolCallId is null
            || string.Equals(request.ToolCallId, toolCallId, StringComparison.Ordinal)));
        return candidates.FirstOrDefault(static request => request.IsSendingResponse)
            ?? candidates.FirstOrDefault(static request => !request.IsCancellationOnly)
            ?? candidates.FirstOrDefault();
    }

    public void StorePermissionRequest(string conversationId, PermissionRequestViewModel request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);
        if (!TryHoldRequestUsage(request, request.Source)) return;
        if (!_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests))
        {
            requests = [];
            _pendingPermissionRequestsByConversation.Add(conversationId, requests);
        }

        // The first unanswered request retains its surface; later requests wait or attach to their
        // own tool card. Invalidated SDK identities cannot keep an obsolete prompt in front.
        RemoveUnavailablePermissionRequests(requests);
        requests.Add(request);
        request.PropertyChanged += OnRequestPropertyChanged;
        NotifyChanged(conversationId);
    }

    public bool RemovePermissionRequest(string conversationId, PermissionRequestViewModel request)
    {
        if (!_pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests)
            || !requests.Remove(request)) return false;
        request.PropertyChanged -= OnRequestPropertyChanged;
        ReleaseRequestUsage(request);
        request.DetachRequest();
        NotifyChanged(conversationId);
        return true;
    }

    internal PermissionRequestViewModel? GetUnboundPermissionCancellation(PermissionRequestViewModel? currentRequest = null)
    {
        RemoveUnavailablePermissionRequests(_unboundPermissionCancellations);
        if (currentRequest is { IsSendingResponse: true } && _unboundPermissionCancellations.Contains(currentRequest))
        {
            return currentRequest;
        }
        return _unboundPermissionCancellations.FirstOrDefault(static request => request.IsSendingResponse)
            ?? _unboundPermissionCancellations.FirstOrDefault(static request => request.IsAwaitingInput);
    }

    internal void StoreUnboundPermissionCancellation(PermissionRequestViewModel request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryHoldRequestUsage(request, request.Source)) return;
        RemoveUnavailablePermissionRequests(_unboundPermissionCancellations);
        _unboundPermissionCancellations.Add(request);
    }

    internal bool RemoveUnboundPermissionCancellation(PermissionRequestViewModel request)
    {
        if (!_unboundPermissionCancellations.Remove(request)) return false;
        ReleaseRequestUsage(request);
        request.DetachRequest();
        return true;
    }

    internal bool ContainsPermissionRequest(string? conversationId, PermissionRequestViewModel request)
        => conversationId is null ? _unboundPermissionCancellations.Contains(request)
            : _pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests) && requests.Contains(request);

    internal IReadOnlyList<PermissionRequestViewModel> GetPendingPermissionRequests(string conversationId, AcpSessionEventSource source)
        => _pendingPermissionRequestsByConversation.TryGetValue(conversationId, out var requests)
            ? requests.Where(request => request.IsAvailable && request.Source is { } owner && owner.Matches(source)).ToArray()
            : Array.Empty<PermissionRequestViewModel>();

    internal IReadOnlyList<(string ConversationId, PermissionRequestViewModel Request)> GetObsoletePermissionRequests(
        IImmutableDictionary<string, ConversationBindingSlice>? bindings)
    {
        var obsolete = new List<(string, PermissionRequestViewModel)>();
        foreach (var (conversationId, requests) in _pendingPermissionRequestsByConversation)
        {
            var currentBinding = bindings?.GetValueOrDefault(conversationId);
            foreach (var request in requests)
            {
                if (request.Binding is not null && request.IsAvailable
                    && (request.Binding != currentBinding || request.IsBindingCurrent?.Invoke() == false))
                {
                    obsolete.Add((conversationId, request));
                }
            }
        }
        return obsolete;
    }

    public void ClearPermissionRequests()
    {
        foreach (var conversationId in _pendingPermissionRequestsByConversation.Keys.ToArray())
        {
            var requests = _pendingPermissionRequestsByConversation[conversationId];
            foreach (var request in requests)
            {
                request.PropertyChanged -= OnRequestPropertyChanged;
                ReleaseRequestUsage(request);
                request.DetachRequest();
            }

            _pendingPermissionRequestsByConversation.Remove(conversationId);
            NotifyChanged(conversationId);
        }
        foreach (var request in _unboundPermissionCancellations)
        {
            ReleaseRequestUsage(request);
            request.DetachRequest();
        }
        _unboundPermissionCancellations.Clear();
    }

    internal void NotifyPermissionRequestChanged(string? conversationId, PermissionRequestViewModel request)
    {
        if (!request.IsAvailable) ReleaseRequestUsage(request);
        if (conversationId is not null && ContainsPermissionRequest(conversationId, request))
        {
            NotifyChanged(conversationId);
        }
    }

    internal void RetireConnection(AcpSessionEventSource source)
    {
        foreach (var (conversationId, request) in _pendingAskUserRequestsByConversation.ToArray())
        {
            if (request.Source is { } owner && owner.Matches(source))
            {
                RemoveAskUserRequest(conversationId, request);
            }
        }

        foreach (var (conversationId, request) in _pendingElicitationRequestsByConversation.ToArray())
        {
            if (request.Source is { } owner && owner.Matches(source))
            {
                RemoveElicitationRequest(conversationId, request);
            }
        }

        foreach (var (conversationId, requests) in _pendingPermissionRequestsByConversation.ToArray())
        {
            foreach (var request in requests.ToArray())
            {
                if (request.Source is { } owner && owner.Matches(source))
                {
                    RemovePermissionRequest(conversationId, request);
                }
            }
        }

        foreach (var request in _unboundPermissionCancellations.ToArray())
        {
            if (request.Source is { } owner && owner.Matches(source))
            {
                RemoveUnboundPermissionCancellation(request);
            }
        }
    }

    internal void ReprojectPermissionLocalizedText(
        string defaultTitle, string cancellationTitle, string bindingCancellationDescription, string peerCancellationDescription)
    {
        foreach (var request in _pendingPermissionRequestsByConversation.Values.SelectMany(static requests => requests))
        {
            request.ReprojectLocalizedText(defaultTitle, cancellationTitle, bindingCancellationDescription, peerCancellationDescription);
        }
        foreach (var request in _unboundPermissionCancellations)
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
        RemoveAskUserRequest(conversationId);
        if (_pendingElicitationRequestsByConversation.Remove(conversationId, out var request))
        {
            request.PropertyChanged -= OnRequestPropertyChanged;
            ReleaseRequestUsage(request);
            request.Dispose();
        }
        if (_pendingPermissionRequestsByConversation.Remove(conversationId, out var permissions))
        {
            foreach (var permission in permissions)
            {
                permission.PropertyChanged -= OnRequestPropertyChanged;
                ReleaseRequestUsage(permission);
                permission.DetachRequest();
            }
        }

        NotifyChanged(conversationId);

        return isCurrentConversation ? EmptySelection() : NoUiChange();
    }

    private void RemoveUnavailablePermissionRequests(List<PermissionRequestViewModel> requests)
    {
        for (var index = requests.Count - 1; index >= 0; index--)
        {
            if (requests[index].IsAvailable) continue;
            requests[index].PropertyChanged -= OnRequestPropertyChanged;
            ReleaseRequestUsage(requests[index]);
            requests[index].DetachRequest();
            requests.RemoveAt(index);
        }
    }

    private IReadOnlyList<AcpSessionEventSource> GetPendingConnectionSources()
    {
        var sources = new HashSet<AcpSessionEventSource>();
        foreach (var request in _pendingPermissionRequestsByConversation.Values.SelectMany(static requests => requests))
        {
            if (request.IsAvailable && request.Source is { } source)
            {
                sources.Add(source);
            }
        }

        foreach (var request in _pendingAskUserRequestsByConversation.Values)
        {
            if (request.Source is { } source)
            {
                sources.Add(source);
            }
        }

        foreach (var request in _pendingElicitationRequestsByConversation.Values)
        {
            if (!request.IsCompleted && (request.CanRespond || request.CanCancel || request.IsSubmitting || request.IsAwaitingCompletion)
                && request.Source is { } source)
            {
                sources.Add(source);
            }
        }

        return sources.ToArray();
    }

    private void OnRequestPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is ElicitationRequestViewModel { IsCompleted: true } completed) ReleaseRequestUsage(completed);
        if (args.PropertyName is not ("HasError" or "CanRespond" or "CanCancel" or "IsSubmitting"
            or "IsAwaitingCompletion" or "IsCompleted" or "Description"))
        {
            return;
        }

        foreach (var (conversationId, request) in _pendingAskUserRequestsByConversation)
        {
            if (ReferenceEquals(sender, request)) NotifyChanged(conversationId);
        }

        foreach (var (conversationId, request) in _pendingElicitationRequestsByConversation)
        {
            if (ReferenceEquals(sender, request)) NotifyChanged(conversationId);
        }

        foreach (var (conversationId, requests) in _pendingPermissionRequestsByConversation)
        {
            if (requests.Any(request => ReferenceEquals(sender, request))) NotifyChanged(conversationId);
        }
    }

    private void NotifyChanged(string conversationId)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.Enqueue(() => Changed?.Invoke(conversationId));
            return;
        }

        Changed?.Invoke(conversationId);
    }

    private bool TryHoldRequestUsage(object request, AcpSessionEventSource? source)
    {
        if (_requestUsageLeases.ContainsKey(request) || _sessionRegistry is null || source?.ProfileId is null) return true;
        if (!_sessionRegistry.TryAcquireUsage(source.Value, out var lease)) return false;
        _requestUsageLeases.Add(request, lease!);
        return true;
    }

    private void ReleaseRequestUsage(object request)
    {
        if (_requestUsageLeases.Remove(request, out var lease)) lease.Dispose();
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
