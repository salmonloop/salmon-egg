using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationAttentionStore
{
    IState<ConversationAttentionState> State { get; }

    ValueTask Dispatch(ConversationAttentionAction action);
}

public sealed class ConversationAttentionStore : IConversationAttentionStore
{
    public IState<ConversationAttentionState> State { get; }

    public ConversationAttentionStore(IState<ConversationAttentionState> state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public async ValueTask Dispatch(ConversationAttentionAction action)
    {
        await State.Update(s => ConversationAttentionReducer.Reduce(s, action), default);
    }
}
