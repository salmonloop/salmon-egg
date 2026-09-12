using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private readonly object _promptOperationSync = new();
    private readonly Dictionary<string, PromptOperation> _promptOperations = new(StringComparer.Ordinal);
    private bool _promptOperationsDraining;
    private Task _promptDraftDispatchTask = Task.CompletedTask;
    private readonly object _poolCleanupSync = new();
    private Task _poolCleanupTask = Task.CompletedTask;
    private bool _poolCleanupRequested;
    private bool _poolCleanupStopped;

    private sealed class PromptOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sourceSync = new();
        private AcpSessionEventSource _source;
        private IDisposable? _usageLease;

        public PromptOperation(PromptSendContext context, AcpSessionEventSource source, CancellationToken lifetime, IDisposable? usageLease = null)
        {
            Context = context;
            Source = source;
            _usageLease = usageLease;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            CancellationToken = _cancellation.Token;
        }

        public PromptSendContext Context { get; }
        public AcpSessionEventSource Source
        {
            get { lock (_sourceSync) return _source; }
            set { lock (_sourceSync) _source = value; }
        }
        public CancellationToken CancellationToken { get; }
        public Task Completion => _completion.Task;
        public bool IsReplacingConnectionForAuthentication { get; set; }
        public long? ClearedDraftRevision { get; set; }
        public string? RemoteSessionId { get; set; }

        public void Cancel()
        {
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            ReleaseUsage();
            _cancellation.Dispose();
            _completion.TrySetResult();
        }

        public void ReplaceUsage(IDisposable? lease) => Interlocked.Exchange(ref _usageLease, lease)?.Dispose();

        public void ReleaseUsage() => ReplaceUsage(null);
    }

    private PromptOperation? ResolvePromptOperation(string? conversationId)
    {
        lock (_promptOperationSync)
        {
            return conversationId is not null && _promptOperations.TryGetValue(conversationId, out var operation)
                ? operation
                : null;
        }
    }

    private PromptOperation? TryAdmitPromptOperation(PromptSendContext context, AcpSessionEventSource source)
    {
        lock (_promptOperationSync)
        {
            if (_disposed || _promptOperationsDraining || _promptOperations.ContainsKey(context.ConversationId))
            {
                return null;
            }

            if (!TryAcquireConnectionUsage(source, out var usage)) return null;
            var operation = new PromptOperation(context, source, _disposeCts.Token, usage);
            _promptOperations.Add(context.ConversationId, operation);
            return operation;
        }
    }

    private bool TryAcquireConnectionUsage(AcpSessionEventSource source, out IDisposable? lease)
    {
        lease = null;
        return _connectionSessionRegistry is null || source.ProfileId is null
            || _connectionSessionRegistry.TryAcquireUsage(source, out lease);
    }

    private void RemovePromptOperation(PromptOperation operation)
    {
        lock (_promptOperationSync)
        {
            if (_promptOperations.TryGetValue(operation.Context.ConversationId, out var current)
                && ReferenceEquals(current, operation))
            {
                _promptOperations.Remove(operation.Context.ConversationId);
            }
        }
    }

    private Task RequestPoolCleanupAsync()
    {
        TaskCompletionSource? completion = null;
        Task cleanup;
        lock (_poolCleanupSync)
        {
            if (_poolCleanupStopped || _disposed) return _poolCleanupTask;
            _poolCleanupRequested = true;
            if (_poolCleanupTask.IsCompleted)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _poolCleanupTask = completion.Task;
            }

            cleanup = _poolCleanupTask;
        }

        if (completion is not null) _ = RunPoolCleanupAsync(completion);
        return cleanup;
    }

    private async Task RunPoolCleanupAsync(TaskCompletionSource completion)
    {
        // Coalesce native request changes from one dispatcher turn without polling or another timer.
        await Task.Yield();
        try
        {
            while (true)
            {
                lock (_poolCleanupSync)
                {
                    if (_poolCleanupStopped || !_poolCleanupRequested)
                    {
                        completion.TrySetResult();
                        return;
                    }

                    _poolCleanupRequested = false;
                }

                try
                {
                    await _acpConnectionCommands.ReevaluatePoolAsync(_chatService, _disposeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    Logger.LogWarning(error, "ACP pool cleanup could not finish after conversation activity changed.");
                }
            }
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void OnPendingInteractionChanged(string conversationId) => _ = RequestPoolCleanupAsync();

    private Task StopPoolCleanupAsync()
    {
        _panelStateCoordinator.Changed -= OnPendingInteractionChanged;
        lock (_poolCleanupSync)
        {
            _poolCleanupStopped = true;
            _poolCleanupRequested = false;
            return _poolCleanupTask;
        }
    }

    private bool IsOperationReplacingConnection(AcpSessionEventSource source, string? conversationId = null)
    {
        lock (_promptOperationSync)
        {
            return _promptOperations.Values.Any(operation =>
                operation.Source.Matches(source) && operation.IsReplacingConnectionForAuthentication
                && (conversationId is null || string.Equals(conversationId, operation.Context.ConversationId, StringComparison.Ordinal)));
        }
    }

    private void CancelPromptOperationsForSource(AcpSessionEventSource source, AcpConnectionRetirementReason reason)
    {
        PromptOperation[] operations;
        lock (_promptOperationSync)
        {
            operations = _promptOperations.Values.Where(operation =>
                operation.Source.Matches(source)
                && !(reason == AcpConnectionRetirementReason.Replaced && operation.IsReplacingConnectionForAuthentication)).ToArray();
        }

        foreach (var operation in operations) operation.Cancel();
    }

    private void CancelAllPromptOperations()
    {
        PromptOperation[] operations;
        lock (_promptOperationSync)
        {
            _promptOperationsDraining = true;
            operations = _promptOperations.Values.ToArray();
        }

        foreach (var operation in operations) operation.Cancel();
    }

    public async Task DrainPromptOperationsAsync(CancellationToken cancellationToken = default)
    {
        PromptOperation[] operations;
        lock (_promptOperationSync)
        {
            _promptOperationsDraining = true;
            operations = _promptOperations.Values.ToArray();
        }

        foreach (var operation in operations) operation.Cancel();
        await Task.WhenAll(operations.Select(operation => operation.Completion))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private AcpSessionEventSource? ResolvePromptSource(IChatService? service, string? profileId)
        => service is null ? null : ResolveRegisteredOrCurrentEventSource(service, profileId);

    private AcpSessionEventSource? ResolveRegisteredOrCurrentEventSource(IChatService service, string? requiredProfileId = null)
    {
        if (_connectionSessionRegistry?.TryGetProfileId(service, out var registeredProfileId) == true)
        {
            return _connectionSessionRegistry.TryGetByProfile(registeredProfileId, out var session)
                && ReferenceEquals(session.Service, service)
                && (requiredProfileId is null || string.Equals(requiredProfileId, session.ProfileId, StringComparison.Ordinal))
                    ? session.EventSource
                    : null;
        }

        if (!ReferenceEquals(service, _chatService)) return null;
        var directProfileId = _connectionSessionRegistry is null
            ? ForegroundTransportProfileId ?? SelectedProfileId
            : null;
        if (requiredProfileId is not null
            && !string.Equals(requiredProfileId, directProfileId, StringComparison.Ordinal))
        {
            return null;
        }

        return new(directProfileId, ConnectionInstanceId, service);
    }

    private bool IsPromptSourceCurrent(AcpSessionEventSource source)
        => !_disposed
            && ResolveRegisteredOrCurrentEventSource(source.Service, source.ProfileId) is { } current
            && current.Matches(source);

    private bool IsPromptForegroundCurrent(PromptOperation operation)
        => !_disposed
            && string.Equals(CurrentSessionId, operation.Context.ConversationId, StringComparison.Ordinal)
            && string.Equals(SelectedProfileId, operation.Source.ProfileId, StringComparison.Ordinal)
            && _conversationActivationOrchestrator.IsLatestActivationVersion(operation.Context.ActivationVersion);

    private void ThrowIfPromptSourceChanged(PromptOperation operation)
    {
        operation.CancellationToken.ThrowIfCancellationRequested();
        if (!IsPromptSourceCurrent(operation.Source))
        {
            throw new InvalidOperationException(Localize("ChatOperation_ConnectionInterrupted", "The connection was interrupted. Reconnect to continue."));
        }
    }

    private async Task<bool> AuthenticatePromptOperationAsync(PromptOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsPromptForegroundCurrent(operation) || !ReferenceEquals(_chatService, operation.Source.Service))
        {
            throw new InvalidOperationException(Localize("ChatAuth_Required", "Select this conversation to complete agent authentication."));
        }

        lock (_promptOperationSync) operation.IsReplacingConnectionForAuthentication = true;
        try
        {
            var authenticated = await TryAuthenticateAsync(cancellationToken, () => IsPromptForegroundCurrent(operation)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPromptForegroundCurrent(operation))
            {
                throw new OperationCanceledException("The conversation changed during authentication.", cancellationToken);
            }

            if (!authenticated) return false;
            var source = ResolvePromptSource(_chatService, operation.Source.ProfileId)
                ?? throw new InvalidOperationException("The authenticated connection is unavailable.");
            await BindPromptOperationAsync(operation, source, remoteSessionId: null).ConfigureAwait(false);
            return true;
        }
        finally
        {
            lock (_promptOperationSync) operation.IsReplacingConnectionForAuthentication = false;
        }
    }

    private async Task BindPromptOperationAsync(PromptOperation operation, AcpSessionEventSource source, string? remoteSessionId)
    {
        var previousSource = operation.Source;
        if (!previousSource.Matches(source))
        {
            if (!TryAcquireConnectionUsage(source, out var usage))
                throw new OperationCanceledException("The replacement connection was retired before admission.", operation.CancellationToken);
            operation.ReplaceUsage(usage);
        }
        await _chatStore.Dispatch(new SetTurnBindingAction(
            operation.Context.ConversationId,
            operation.Context.TurnId,
            source.ProfileId,
            remoteSessionId,
            source.ConnectionInstanceId,
            previousSource.ConnectionInstanceId)).ConfigureAwait(false);
        var turn = (await _chatStore.GetCurrentStateAsync().ConfigureAwait(false)).ResolveTurn(operation.Context.ConversationId);
        if (turn?.TurnId != operation.Context.TurnId
            || turn.ProfileId != source.ProfileId
            || turn.ConnectionInstanceId != source.ConnectionInstanceId
            || turn.Phase is ChatTurnPhase.Completed or ChatTurnPhase.Failed or ChatTurnPhase.Cancelled)
        {
            throw new OperationCanceledException("The prompt turn changed before its connection was bound.", operation.CancellationToken);
        }

        lock (_promptOperationSync) operation.Source = source;
        operation.RemoteSessionId = remoteSessionId ?? turn.RemoteSessionId;
    }

    private async Task OnPromptOperationDispatchedAsync(PromptOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfPromptSourceChanged(operation);
        var binding = await GetConversationRemoteBindingAsync(operation.Context.ConversationId, cancellationToken).ConfigureAwait(false);
        await BindPromptOperationAsync(operation, operation.Source, binding?.RemoteSessionId).ConfigureAwait(false);
        var turn = (await _chatStore.GetCurrentStateAsync().ConfigureAwait(false)).ResolveTurn(operation.Context.ConversationId);
        if (turn?.TurnId != operation.Context.TurnId || turn.Phase != ChatTurnPhase.DispatchingPrompt) return;
        await _chatStore.Dispatch(new AdvanceTurnPhaseAction(
            turn.ConversationId, turn.TurnId, ChatTurnPhase.WaitingForAgent,
            ConnectionInstanceId: operation.Source.ConnectionInstanceId)).ConfigureAwait(false);
        Logger.LogInformation(
            "Chat prompt request dispatched. ConversationId={ConversationId} TurnId={TurnId} RemoteSessionId={RemoteSessionId} TurnPhase={TurnPhase}",
            turn.ConversationId, turn.TurnId, operation.RemoteSessionId, ChatTurnPhase.WaitingForAgent);
    }

    private Task ClearPromptForOperationAsync(PromptOperation operation)
        => PostToUiAsync(async () =>
        {
            var context = operation.Context;
            if (!IsPromptForegroundCurrent(operation) || !string.Equals(CurrentPrompt, context.PromptText, StringComparison.Ordinal)) return;
            await _chatStore.Dispatch(new SetDraftTextAction(string.Empty, context.ConversationId, context.DraftRevision)).ConfigureAwait(true);
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
            if (!IsPromptForegroundCurrent(operation)
                || state.DraftRevision != context.DraftRevision + 1
                || state.DraftText.Length != 0
                || (!string.Equals(CurrentPrompt, context.PromptText, StringComparison.Ordinal) && CurrentPrompt.Length != 0)) return;
            operation.ClearedDraftRevision = state.DraftRevision;
            ApplyOperationDraftProjection(string.Empty, state.DraftRevision);
        });

    private Task RestorePromptTextAfterSendFailureAsync(PromptOperation operation)
        => PostToUiAsync(async () =>
        {
            if (!IsPromptForegroundCurrent(operation)
                || operation.ClearedDraftRevision is not { } revision
                || !string.IsNullOrEmpty(CurrentPrompt)) return;
            await _chatStore.Dispatch(new SetDraftTextAction(operation.Context.PromptText, operation.Context.ConversationId, revision)).ConfigureAwait(true);
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
            if (!IsPromptForegroundCurrent(operation)
                || state.DraftRevision != revision + 1
                || !string.Equals(state.DraftText, operation.Context.PromptText, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(CurrentPrompt)) return;
            ApplyOperationDraftProjection(operation.Context.PromptText, state.DraftRevision);
        });

    private void ApplyOperationDraftProjection(string text, long revision)
    {
        _minimumPromptDraftRevision = Math.Max(_minimumPromptDraftRevision, revision);
        ClearPendingLocalPromptProjection();
        var wasSuppressed = _suppressStorePromptProjection;
        _suppressStorePromptProjection = true;
        try { CurrentPrompt = text; }
        finally { _suppressStorePromptProjection = wasSuppressed; }
    }

    private sealed class PromptBindingCommands(ChatViewModel owner, PromptOperation operation) : IConversationBindingCommands
    {
        public async ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
        {
            owner.ThrowIfPromptSourceChanged(operation);
            if (!string.Equals(conversationId, operation.Context.ConversationId, StringComparison.Ordinal)
                || (boundProfileId is not null && !string.Equals(boundProfileId, operation.Source.ProfileId, StringComparison.Ordinal)))
            {
                return BindingUpdateResult.Error("PromptBindingOwnerMismatch");
            }

            var turn = (await owner._chatStore.GetCurrentStateAsync().ConfigureAwait(false)).ResolveTurn(conversationId);
            if (turn?.TurnId != operation.Context.TurnId || turn.ConnectionInstanceId != operation.Source.ConnectionInstanceId)
            {
                return BindingUpdateResult.Error("PromptTurnChanged");
            }

            var result = await owner.ConversationBindingCommands.UpdateBindingAsync(
                conversationId, remoteSessionId, boundProfileId, turn).ConfigureAwait(false);
            if (result.Status == BindingUpdateStatus.Success)
            {
                await owner.BindPromptOperationAsync(operation, operation.Source, remoteSessionId).ConfigureAwait(false);
            }

            return result;
        }
    }
}
