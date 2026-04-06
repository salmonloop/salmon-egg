using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationMutationPipelineTests
{
    [Fact]
    public async Task RunAsync_SameConversationId_ExecutesSerially()
    {
        var pipeline = new ConversationMutationPipeline();
        var order = new List<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = pipeline.RunAsync(
            "conv-1",
            async _ =>
            {
                lock (order)
                {
                    order.Add(1);
                }

                await gate.Task;

                lock (order)
                {
                    order.Add(2);
                }
            });

        await Task.Delay(30);

        var secondCompleted = false;
        var second = pipeline.RunAsync(
            "conv-1",
            _ =>
            {
                lock (order)
                {
                    order.Add(3);
                }

                secondCompleted = true;
                return Task.CompletedTask;
            });

        await Task.Delay(30);
        Assert.False(secondCompleted);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task RunAsync_DifferentConversationIds_AllowsParallelExecution()
    {
        var pipeline = new ConversationMutationPipeline();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = pipeline.RunAsync(
            "conv-a",
            async _ =>
            {
                Interlocked.Increment(ref started);
                await release.Task;
            },
            cts.Token);

        var second = pipeline.RunAsync(
            "conv-b",
            async _ =>
            {
                Interlocked.Increment(ref started);
                await release.Task;
            },
            cts.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (Volatile.Read(ref started) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, cts.Token);
        }

        Assert.Equal(2, Volatile.Read(ref started));

        release.SetResult();
        await Task.WhenAll(first, second);
    }
}
