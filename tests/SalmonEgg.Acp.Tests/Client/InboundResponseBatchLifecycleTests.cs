using System.Runtime.CompilerServices;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class InboundResponseBatchLifecycleTests
{
    [Fact]
    public async Task ResumeSending_PreparedArrayPublishesItsPhysicalWriteBeforeCompletion()
    {
        // Arrange
        var write = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new List<bool>();
        InboundResponseBatch? batch = null;
        batch = new InboundResponseBatch(1, _ => write.Task, _ => { }, new NullAcpClientLogger(),
            () => notifications.Add(batch!.IsSending), () => { });
        batch.DeferSending();
        var response = batch.SubmitAsync(0, ErrorResponse());
        Assert.False(batch.IsSending);
        notifications.Clear();

        // Act
        try
        {
            batch.ResumeSending();

            // Assert
            Assert.True(batch.IsSending);
            Assert.Contains(true, notifications);
            Assert.False(response.IsCompleted);
        }
        finally
        {
            write.TrySetResult(true);
            await response.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SubmitCancellation_DeferredFailedWriteReplacement_PreservesSendingUntilTheReplacementSettles()
    {
        // Arrange
        var replacementWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<IReadOnlyList<JsonRpcResponse>>();
        var notifications = new List<bool>();
        var cancelledResponse = ErrorResponse();
        Task<bool>? cancellation = null;
        var failures = 0;
        InboundResponseBatch? batch = null;
        batch = new InboundResponseBatch(1, responses =>
        {
            writes.Add(responses);
            if (writes.Count == 1)
            {
                // Session cancellation prepares every sibling before allowing the replacement.
                batch!.DeferSending();
                cancellation = batch.SubmitCancellation(0, cancelledResponse);
                return Task.FromResult(false);
            }
            return replacementWrite.Task;
        }, _ => { }, new NullAcpClientLogger(), () => notifications.Add(batch!.IsSending), () => failures++);

        // Act: the first write fails while its cancellation is still being prepared.
        var completion = batch.SubmitAsync(0, ErrorResponse());
        try
        {
            // Assert
            Assert.Single(writes);
            Assert.NotNull(cancellation);
            Assert.True(batch.IsSending);
            Assert.False(completion.IsCompleted);
            Assert.False(cancellation.IsCompleted);
            Assert.NotEmpty(notifications);
            Assert.All(notifications, sending => Assert.True(sending));

            batch.ResumeSending();
            Assert.Equal(2, writes.Count);
            Assert.Same(cancelledResponse, writes[1][0]);
            Assert.True(batch.IsSending);
            Assert.False(completion.IsCompleted);
            Assert.False(cancellation.IsCompleted);
            Assert.Equal(0, failures);
            replacementWrite.TrySetResult(true);
            Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(await cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(batch.IsSending);
            Assert.Equal(2, writes.Count);
        }
        finally
        {
            replacementWrite.TrySetResult(false);
            batch.Abandon();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAsync_UnretryableWriteFailure_TerminatesAndReleasesResponse(bool throwOnWrite)
    {
        // Arrange
        var owner = new BatchOwner(throwOnWrite);
        var response = SubmitWithWeakResponse(owner.Batch);

        // Act
        Assert.False(await response.Completion.WaitAsync(TestContext.Current.CancellationToken));
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.Equal(1, owner.Failures);
        Assert.False(response.Reference.IsAlive);
        Assert.False(await owner.Batch.SubmitAsync(0, ErrorResponse()));
        Assert.False(await owner.Batch.SubmitCancellation(0, ErrorResponse()));
        Assert.Equal(1, owner.Writes);
        GC.KeepAlive(owner);
    }

    [Fact]
    public async Task SubmitAsync_RetryOwnerRetainsFailedBatch_ResendsAndCommitsOnlyOnSuccess()
    {
        // Arrange
        var failWrite = true;
        var committed = 0;
        var failed = 0;
        var writes = new List<IReadOnlyList<JsonRpcResponse>>();
        var batch = new InboundResponseBatch(2, responses =>
        {
            writes.Add(responses);
            return Task.FromResult(!failWrite);
        }, _ => committed++, new NullAcpClientLogger(), () => { }, () => failed++);
        var firstResponse = ErrorResponse();
        var secondResponse = ErrorResponse();

        // Act
        var first = batch.SubmitAsync(0, firstResponse);
        var second = batch.SubmitAsync(1, secondResponse);
        Assert.False(await first.WaitAsync(TestContext.Current.CancellationToken));
        Assert.False(await second.WaitAsync(TestContext.Current.CancellationToken));
        failWrite = false;
        Assert.True(await batch.SubmitAsync(1, secondResponse).WaitAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, failed);
        Assert.Equal(1, committed);
        Assert.Equal(2, writes.Count);
        Assert.Same(firstResponse, writes[1][0]);
        Assert.Same(secondResponse, writes[1][1]);
        Assert.False(await batch.SubmitAsync(0, firstResponse));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, Task<bool> Completion) SubmitWithWeakResponse(InboundResponseBatch batch)
    {
        // Keep the allocation out of the test frame: the test deliberately holds the live batch,
        // but no JIT-extended local may keep its discarded response alive through collection.
        var response = ErrorResponse();
        return (new WeakReference(response), batch.SubmitAsync(0, response));
    }

    private static JsonRpcResponse ErrorResponse()
        => new(null, JsonRpcError.CreateInvalidRequest());

    private sealed class BatchOwner
    {
        internal BatchOwner(bool throwOnWrite)
        {
            Batch = new InboundResponseBatch(1, _ =>
            {
                Writes++;
                return throwOnWrite
                    ? Task.FromException<bool>(new IOException("The write failed."))
                    : Task.FromResult(false);
            }, _ => throw new InvalidOperationException("A failed write must not commit."),
                new NullAcpClientLogger(), () => { }, OnFailure);
        }

        internal InboundResponseBatch Batch { get; }
        internal int Writes { get; private set; }
        internal int Failures { get; private set; }

        private void OnFailure()
        {
            Failures++;
            Batch.Abandon();
        }
    }
}
