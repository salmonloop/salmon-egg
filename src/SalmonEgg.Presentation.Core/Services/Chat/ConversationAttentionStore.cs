using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationAttentionStore
{
    IState<ConversationAttentionState> State { get; }

    ValueTask Dispatch(ConversationAttentionAction action);

    ValueTask<ConversationAttentionState> GetCurrentStateAsync();
}

public sealed class ConversationAttentionStore : IConversationAttentionStore
{
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private ConversationAttentionState? _cachedState;

    public IState<ConversationAttentionState> State { get; }

    public ConversationAttentionStore(IState<ConversationAttentionState> state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public async ValueTask Dispatch(ConversationAttentionAction action)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var currentState = Volatile.Read(ref _cachedState) ?? await State ?? ConversationAttentionState.Empty;
            var updatedState = ConversationAttentionReducer.Reduce(currentState, action);
            Volatile.Write(ref _cachedState, updatedState);

            await State.Update(_ => updatedState, default).ConfigureAwait(false);
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    public async ValueTask<ConversationAttentionState> GetCurrentStateAsync()
        => Volatile.Read(ref _cachedState) ?? await State ?? ConversationAttentionState.Empty;
}
