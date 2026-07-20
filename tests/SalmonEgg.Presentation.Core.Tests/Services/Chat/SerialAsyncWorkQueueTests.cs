using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class SerialAsyncWorkQueueTests
{
    [Fact]
    public async Task Enqueue_RunsWorkInOrder()
    {
        var queue = new SerialAsyncWorkQueue();
        var order = new List<int>();

        await queue.Enqueue(() => { order.Add(1); return Task.CompletedTask; });
        await queue.Enqueue(() => { order.Add(2); return Task.CompletedTask; });
        await queue.Enqueue(() => { order.Add(3); return Task.CompletedTask; });

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task Enqueue_SwallowsFaultedPreviousBeforeRunningNext()
    {
        var queue = new SerialAsyncWorkQueue();
        var ran = false;

        var faulted = queue.Enqueue(() => throw new InvalidOperationException("boom"));
        var next = queue.Enqueue(() => { ran = true; return Task.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted);
        await next;

        Assert.True(ran);
    }

    [Fact]
    public async Task Enqueue_AwaitedTaskCompletesWhenWorkCompletes()
    {
        var queue = new SerialAsyncWorkQueue();
        var tcs = new TaskCompletionSource<bool>();

        var enqueued = queue.Enqueue(() => tcs.Task);
        Assert.False(enqueued.IsCompleted);

        tcs.SetResult(true);
        await enqueued;

        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task Enqueue_NullWork_Throws()
    {
        var queue = new SerialAsyncWorkQueue();

        await Assert.ThrowsAsync<ArgumentNullException>(() => queue.Enqueue(null!));
    }
}
