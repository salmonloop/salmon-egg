using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatConnectionStore
{
    IState<ChatConnectionState> State { get; }

    ValueTask Dispatch(ChatConnectionAction action);
}

public sealed class ChatConnectionStore : IChatConnectionStore
{
    public IState<ChatConnectionState> State { get; }

    public ChatConnectionStore(IState<ChatConnectionState> state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public async ValueTask Dispatch(ChatConnectionAction action)
    {
        await State.Update(s => ChatConnectionReducer.Reduce(s, action), default);
    }
}
