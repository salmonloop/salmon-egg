using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationMutationPipeline : IConversationMutationPipeline
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _conversationGates = new(StringComparer.Ordinal);

    public Task RunAsync(
        string conversationId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default)
        => RunCoreAsync(
            conversationId,
            async token =>
            {
                await mutation(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    public Task<TResult> RunAsync<TResult>(
        string conversationId,
        Func<CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
        => RunCoreAsync(conversationId, mutation, cancellationToken);

    private async Task<TResult> RunCoreAsync<TResult>(
        string conversationId,
        Func<CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("Conversation id is required.", nameof(conversationId));
        }

        ArgumentNullException.ThrowIfNull(mutation);

        var key = conversationId.Trim();
        var gate = _conversationGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await mutation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
