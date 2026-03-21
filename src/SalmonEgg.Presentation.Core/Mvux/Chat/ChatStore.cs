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
    private long _lastProjectedGeneration = long.MinValue;

    public IState<ChatState> State { get; }

    public ChatStore(IState<ChatState> state, IWorkspaceWriter? workspaceWriter = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _workspaceWriter = workspaceWriter;
    }

    public async ValueTask Dispatch(ChatAction action)
    {
        var previousGeneration = 0L;
        ChatState? updatedState = null;

        await State.Update(s =>
        {
            var current = s ?? ChatState.Empty;
            previousGeneration = current.Generation;
            updatedState = ChatReducer.Reduce(current, action);
            return updatedState;
        }, default).ConfigureAwait(false);

        if (updatedState is null || updatedState.Generation <= previousGeneration)
        {
            return;
        }

        if (!TryAdvanceProjectedGeneration(updatedState.Generation))
        {
            return;
        }

        _workspaceWriter?.Enqueue(updatedState, scheduleSave: true);
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
