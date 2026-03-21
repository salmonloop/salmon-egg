using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

/// <summary>
/// Defines the Single Source of Truth for the Chat feature.
/// </summary>
public interface IChatStore
{
    /// <summary>
    /// Gets the current state of the chat.
    /// </summary>
    IState<ChatState> State { get; }

    /// <summary>
    /// Dispatches an action to update the state via the reducer.
    /// </summary>
    /// <param name="action">The action to dispatch.</param>
    ValueTask Dispatch(ChatAction action);
}

/// <summary>
/// Implementation of the Chat Store using Uno.Extensions.Reactive.
/// </summary>
public sealed class ChatStore : IChatStore
{
    private readonly IWorkspaceWriter? _workspaceWriter;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private long _lastProjectedGeneration = long.MinValue;
    private ChatState? _cachedState;

    public IState<ChatState> State { get; }

    public ChatStore(IState<ChatState> state, IWorkspaceWriter? workspaceWriter = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _workspaceWriter = workspaceWriter;
    }

    public async ValueTask Dispatch(ChatAction action)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var currentState = _cachedState ?? await State ?? ChatState.Empty;
            var updatedState = ChatReducer.Reduce(currentState, action);
            _cachedState = updatedState;

            await State.Update(_ => updatedState, CancellationToken.None).ConfigureAwait(false);

            if (updatedState.Generation <= currentState.Generation)
            {
                return;
            }

            if (!TryAdvanceProjectedGeneration(updatedState.Generation))
            {
                return;
            }

            _workspaceWriter?.Enqueue(updatedState, scheduleSave: true);
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    private bool TryAdvanceProjectedGeneration(long generation)
    {
        while (true)
        {
            var projectedGeneration = Interlocked.Read(ref _lastProjectedGeneration);
            if (generation <= projectedGeneration)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                ref _lastProjectedGeneration,
                generation,
                projectedGeneration) == projectedGeneration)
            {
                return true;
            }
        }
    }
}
