using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationActivationOrchestratorTests
{
    [Fact]
    public async Task ActivateAsync_WhenWarmConversationSupersedesPendingActivation_CompletesWarmPathOnly()
    {
        using var orchestrator = new ConversationActivationOrchestrator(
            NullLogger<ConversationActivationOrchestrator>.Instance);
        var sink = new RecordingSink
        {
            CanReuseWarmCurrentConversation = true
        };
        var request = new ConversationActivationOrchestratorRequest("conv-1", true);

        var result = await orchestrator.ActivateAsync(request, sink);

        Assert.True(result.Succeeded);
        Assert.True(result.UsedWarmReuse);
        Assert.False(result.WasSuperseded);
        Assert.Equal(1, sink.PrepareActivationCallCount);
        Assert.Equal(1, sink.WarmSupersedeCallCount);
        Assert.Equal(0, sink.ExecuteActivationCallCount);
        Assert.Equal(1, sink.CompletedCallCount);
    }

    [Fact]
    public async Task ActivateAsync_WhenPendingHydrationAlreadyOwnsConversation_SkipsAuthoritativeActivation()
    {
        using var orchestrator = new ConversationActivationOrchestrator(
            NullLogger<ConversationActivationOrchestrator>.Instance);
        var sink = new RecordingSink
        {
            CanReusePendingHydration = true
        };
        var request = new ConversationActivationOrchestratorRequest("conv-1", true);

        var result = await orchestrator.ActivateAsync(request, sink);

        Assert.True(result.Succeeded);
        Assert.False(result.UsedWarmReuse);
        Assert.False(result.WasSuperseded);
        Assert.Equal(0, sink.PrepareActivationCallCount);
        Assert.Equal(0, sink.ExecuteActivationCallCount);
        Assert.Equal(0, sink.CompletedCallCount);
    }

    [Fact]
    public async Task ActivateAsync_WhenNewerIntentCancelsOlderActivation_SuppressesStaleCompletion()
    {
        using var orchestrator = new ConversationActivationOrchestrator(
            NullLogger<ConversationActivationOrchestrator>.Instance);
        var sink = new RecordingSink();
        sink.WaitForCancellationOnFirstExecution = true;

        var firstTask = orchestrator.ActivateAsync(
            new ConversationActivationOrchestratorRequest("conv-1", true),
            sink);

        await sink.WaitForExecuteStartedAsync();

        var secondTask = orchestrator.ActivateAsync(
            new ConversationActivationOrchestratorRequest("conv-2", true),
            sink);
        var secondResult = await secondTask;
        var firstResult = await firstTask;

        Assert.True(secondResult.Succeeded);
        Assert.True(firstResult.WasSuperseded);
        Assert.Single(sink.CompletedConversationIds);
        Assert.Contains("conv-2", sink.CompletedConversationIds);
        Assert.DoesNotContain("conv-1", sink.CompletedConversationIds);
    }

    private sealed class RecordingSink : IConversationActivationOrchestratorSink
    {
        private readonly TaskCompletionSource<object?> _executeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _executeRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanReuseWarmCurrentConversation { get; set; }

        public bool CanReusePendingHydration { get; set; }

        public bool BlockExecution { get; set; }

        public bool BlockOnlyFirstExecution { get; set; }

        public bool WaitForCancellationOnFirstExecution { get; set; }

        public int PrepareActivationCallCount { get; private set; }

        public int WarmSupersedeCallCount { get; private set; }

        public int ExecuteActivationCallCount { get; private set; }

        public int CompletedCallCount { get; private set; }

        public List<string> CompletedConversationIds { get; } = [];

        public Task PrepareActivationAsync(
            ConversationActivationOrchestratorRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareActivationCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> CanReuseWarmCurrentConversationAsync(
            ConversationActivationOrchestratorRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CanReuseWarmCurrentConversation);
        }

        public Task SupersedePendingActivationForWarmConversationAsync(
            ConversationActivationOrchestratorRequest request,
            ConversationActivationContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WarmSupersedeCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> CanReusePendingRemoteHydrationCurrentConversationAsync(
            ConversationActivationOrchestratorRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CanReusePendingHydration);
        }

        public async Task<ConversationActivationOrchestratorResult> ExecuteActivationAsync(
            ConversationActivationOrchestratorRequest request,
            ConversationActivationContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteActivationCallCount++;
            _executeStarted.TrySetResult(null);

            var shouldBlock = BlockExecution;
            if (BlockOnlyFirstExecution && ExecuteActivationCallCount > 1)
            {
                shouldBlock = false;
            }

            if (WaitForCancellationOnFirstExecution && ExecuteActivationCallCount == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }

            if (shouldBlock)
            {
                await _executeRelease.Task.WaitAsync(context.CancellationToken);
            }

            return ConversationActivationOrchestratorResult.Success();
        }

        public Task OnActivationCompletedAsync(
            ConversationActivationOrchestratorRequest request,
            ConversationActivationContext context,
            ConversationActivationOrchestratorResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedCallCount++;
            CompletedConversationIds.Add(request.ConversationId);
            return Task.CompletedTask;
        }

        public Task WaitForExecuteStartedAsync() => _executeStarted.Task;

        public void ReleaseExecution() => _executeRelease.TrySetResult(null);
    }
}
