using System.Collections.Immutable;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task ElicitationRequestReceived_WhenOwnerChangesBeforeUiDispatch_DoesNotOccupyNewFormSlot()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var previousService = CreateConnectedChatService();
        await syncContext.RunUntilCompletedAsync(viewModel.ReplaceChatServiceAsync(
            RegisterInteractionMock(fixture, previousService.Object, "profile-1"), TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await syncContext.RunUntilIdleAsync();
        var currentService = CreateConnectedChatService();
        var previousCancelled = 0;
        previousService.Raise(service => service.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("same-id", new FormElicitationRequest
            {
                Scope = ElicitationScope.ForSession("remote-1"),
                Message = "Stale form"
            }, _ => Task.FromResult(false), () => Task.FromResult(false),
                () => { Interlocked.Increment(ref previousCancelled); return Task.FromResult(false); }));
        Assert.Null(viewModel.PendingElicitationRequest);

        // Replace the owner on the UI thread while the previous request's insertion remains queued.
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            viewModel.ReplaceChatService(RegisterInteractionMock(fixture, currentService.Object, "profile-1"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
        syncContext.RunAll();
        Assert.Null(viewModel.PendingElicitationRequest);
        await WaitForConditionAsync(() => Task.FromResult(Volatile.Read(ref previousCancelled) == 1));
        var currentCancelled = 0;
        currentService.Raise(service => service.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("same-id", new FormElicitationRequest
            {
                Scope = ElicitationScope.ForSession("remote-1"),
                Message = "Current form"
            }, _ => Task.FromResult(true), () => Task.FromResult(true),
                () => { Interlocked.Increment(ref currentCancelled); return Task.FromResult(true); }));
        syncContext.RunAll();

        Assert.Equal("Current form", viewModel.PendingElicitationRequest?.Prompt);
        Assert.Equal(0, Volatile.Read(ref currentCancelled));
        await syncContext.RunUntilCompletedAsync(viewModel.PendingElicitationRequest!.SubmitCommand.ExecuteAsync(null));
        Assert.Null(viewModel.PendingElicitationRequest);
    }

    [Fact]
    public async Task ElicitationRequestReceived_WhenDisposedBeforeUiDispatch_DoesNotRepopulateForm()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(syncContext);
        var service = CreateConnectedChatService();
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.ReplaceChatServiceAsync(
            RegisterInteractionMock(fixture, service.Object, "profile-1"), TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await syncContext.RunUntilIdleAsync();
        var cancelled = 0;
        service.Raise(chat => chat.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("form-1", new FormElicitationRequest
            {
                Scope = ElicitationScope.ForSession("remote-1"),
                Message = "Stale form"
            }, _ => Task.FromResult(false), () => Task.FromResult(false),
                () => { Interlocked.Increment(ref cancelled); return Task.FromResult(false); }));

        fixture.ViewModel.Dispose();
        syncContext.RunAll();

        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
        await WaitForConditionAsync(() => Task.FromResult(Volatile.Read(ref cancelled) == 1));
    }

    [Fact]
    public async Task ElicitationRequestReceived_WhenPoolServiceChangesBeforeUiDispatch_PreservesForm()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(syncContext);
        var service = CreateConnectedChatService();
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.ReplaceChatServiceAsync(
            RegisterInteractionMock(fixture, service.Object, "profile-1"), TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await syncContext.RunUntilIdleAsync();
        var cancelled = 0;
        var replacement = ((IAcpChatCoordinatorSink)fixture.ViewModel).ReplaceChatServiceAsync(
            RegisterInteractionMock(fixture, CreateConnectedChatService().Object, "profile-other"),
            ServiceReplaceIntent.PoolOnly, TestContext.Current.CancellationToken);
        service.Raise(chat => chat.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("form-1", new FormElicitationRequest
            {
                Scope = ElicitationScope.ForSession("remote-1"),
                Message = "Current form"
            }, _ => Task.FromResult(true), () => Task.FromResult(true),
                () => { Interlocked.Increment(ref cancelled); return Task.FromResult(true); }));
        await syncContext.RunUntilCompletedAsync(replacement);

        Assert.Equal("Current form", fixture.ViewModel.PendingElicitationRequest?.Prompt);
        Assert.Equal(0, Volatile.Read(ref cancelled));
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.PendingElicitationRequest!.SubmitCommand.ExecuteAsync(null));
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationRequestReceived_WhenFormAlreadyHeld_CancelsNewRequest(bool firstResponseInFlight)
    {
        await using var fixture = CreateInteractionViewModel();
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(RegisterInteractionMock(fixture, chatService.Object, "profile-1"));
        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        };
        await fixture.UpdateStateAsync(_ => initialState);
        await WaitForConditionAsync(() => Task.FromResult(viewModel.CurrentSessionId == "conv-1"));
        var responseSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var form = new FormElicitationRequest
        {
            Scope = ElicitationScope.ForSession("remote-1"),
            Message = "First request"
        };
        chatService.Raise(service => service.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("form-1", form,
                _ => responseSent.Task, () => Task.FromResult(true), () => Task.FromResult(true)));
        await WaitForConditionAsync(() => Task.FromResult(viewModel.PendingElicitationRequest is not null));
        var first = viewModel.PendingElicitationRequest!;
        var sending = firstResponseInFlight ? first.SubmitCommand.ExecuteAsync(null) : Task.CompletedTask;
        var secondCancelled = 0;

        chatService.Raise(service => service.ElicitationRequestReceived += null,
            new ElicitationRequestEventArgs("form-2", new FormElicitationRequest
            {
                Scope = ElicitationScope.ForSession("remote-1"),
                Message = "Second request"
            }, _ => Task.FromResult(true), () => Task.FromResult(true),
                () => { Interlocked.Increment(ref secondCancelled); return Task.FromResult(true); }));
        await WaitForConditionAsync(() => Task.FromResult(Volatile.Read(ref secondCancelled) == 1));

        Assert.Same(first, viewModel.PendingElicitationRequest);
        Assert.False(viewModel.IsInputEnabled);
        responseSent.SetResult(true);
        await sending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (!firstResponseInFlight)
        {
            await first.CancelCommand.ExecuteAsync(null);
        }
        Assert.Null(viewModel.PendingElicitationRequest);
    }
}
