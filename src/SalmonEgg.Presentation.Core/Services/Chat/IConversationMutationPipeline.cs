using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationMutationPipeline
{
    Task RunAsync(
        string conversationId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default);

    Task<TResult> RunAsync<TResult>(
        string conversationId,
        Func<CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken = default);
}
