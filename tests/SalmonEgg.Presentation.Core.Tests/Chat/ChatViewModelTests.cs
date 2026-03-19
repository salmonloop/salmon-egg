using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public class ChatViewModelTests
{
    private static ViewModelFixture CreateViewModel(SynchronizationContext? syncContext = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), default));
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<SerilogLogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);

        var conversationStore = new Mock<IConversationStore>();
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext ?? new SynchronizationContext());

            var viewModel = new ChatViewModel(
                chatStore.Object,
                chatServiceFactory,
                configService.Object,
                preferences,
                profiles,
                sessionManager.Object,
                conversationStore.Object,
                miniWindow.Object,
                vmLogger.Object,
                syncContext);
            return new ViewModelFixture(viewModel, state, chatStore.Object);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task TrySwitchToSessionAsync_NewSession_DoesNotSeedRemoteSessionId()
    {
        await using var fixture = CreateViewModel();
        var viewModel = fixture.ViewModel;
        var localSessionId = Guid.NewGuid().ToString("N");

        await viewModel.TrySwitchToSessionAsync(localSessionId);

        var field = typeof(ChatViewModel).GetField("_conversationBindings", BindingFlags.Instance | BindingFlags.NonPublic);
        var bindings = (IDictionary)field!.GetValue(viewModel)!;
        var binding = bindings[localSessionId];
        var remoteProp = binding?.GetType().GetProperty("RemoteSessionId", BindingFlags.Instance | BindingFlags.Public);
        var remote = (string?)remoteProp?.GetValue(binding);

        Assert.Null(remote);
    }

    [Fact]
    public async Task TrySwitchToSessionAsync_WaitsForUiStateBeforeCompleting()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var sessionId = Guid.NewGuid().ToString("N");
        var syncField = typeof(ChatViewModel).GetField("_syncContext", BindingFlags.Instance | BindingFlags.NonPublic);
        var capturedContext = syncField?.GetValue(viewModel);
        Assert.Same(syncContext, capturedContext);
        var gateField = typeof(ChatViewModel).GetField("_sessionSwitchGate", BindingFlags.Instance | BindingFlags.NonPublic);
        var gate = (SemaphoreSlim?)gateField?.GetValue(viewModel);
        Assert.NotNull(gate);
        Assert.Equal(1, gate!.CurrentCount);

        var switchTask = viewModel.TrySwitchToSessionAsync(sessionId);
        await Task.Yield();
        Assert.False(switchTask.IsCompleted);
        for (var i = 0; i < 4 && !switchTask.IsCompleted; i++)
        {
            syncContext.RunAll();
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        var completed = await Task.WhenAny(switchTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(switchTask, completed);
        await switchTask;

        Assert.Equal(sessionId, viewModel.CurrentSessionId);
    }

    [Fact]
    public async Task Dispose_CancelsStoreSubscription_DoesNotUpdateAfterDispose()
    {
        // 1. Setup store with initial state
        var initialState = ChatState.Empty with { IsThinking = false };
        var chatStore = new Mock<IChatStore>();
        await using var state = State.Value(this, () => initialState);
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

        // 2. Create VM with queueing sync context
        var syncContext = new QueueingSynchronizationContext();
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        using var viewModel = new ChatViewModel(
            chatStore.Object,
            chatServiceFactory,
            configService.Object,
            preferences,
            profiles,
            sessionManager.Object,
            conversationStore.Object,
            miniWindow.Object,
            vmLogger.Object,
            syncContext);

        // 3. Dispatch initial state update and verify projection
        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);

        // 4. Dispose the ViewModel
        viewModel.Dispose();

        // 5. Update store state (thinking = true)
        // Note: In real MVUX, the ForEachAsync loop will stop due to CTS cancellation.
        // For this test to be robust, we verify that after dispose, no further updates reach the UI properties.
        var newState = initialState with { IsThinking = true };
        await state.Update(s => newState, CancellationToken.None);

        // 6. Flush sync context and verify thinking is still false
        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);
    }

    [Fact]
    public async Task Dispose_DropsAlreadyQueuedStoreProjection()
    {
        var initialState = ChatState.Empty with { IsThinking = false };
        await using var state = State.Value(this, () => initialState);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

        var syncContext = new QueueingSynchronizationContext();
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);
        var conversationStore = new Mock<IConversationStore>();
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        using var viewModel = new ChatViewModel(
            chatStore.Object,
            chatServiceFactory,
            configService.Object,
            preferences,
            profiles,
            sessionManager.Object,
            conversationStore.Object,
            miniWindow.Object,
            vmLogger.Object,
            syncContext);

        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);

        await state.Update(_ => initialState with { IsThinking = true }, CancellationToken.None);
        viewModel.Dispose();

        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);
    }

    [Fact]
    public async Task CurrentPrompt_UpdatesDraftTextInStore()
    {
        await using var fixture = CreateViewModel();
        var viewModel = fixture.ViewModel;

        viewModel.CurrentPrompt = "draft text";

        await Task.Delay(50);

        Assert.Equal("draft text", viewModel.CurrentPrompt);
        Assert.Equal("draft text", (await fixture.GetStateAsync()).DraftText);
    }

    [Fact]
    public async Task StoreDraftText_ProjectsToCurrentPrompt()
    {
        await using var fixture = CreateViewModel();
        var viewModel = fixture.ViewModel;

        await fixture.DispatchAsync(new SetDraftTextAction("from store"));
        await Task.Delay(50);

        Assert.Equal("from store", viewModel.CurrentPrompt);
    }

    [Fact]
    public async Task TrySwitchToSessionAsync_OnTargetSynchronizationContext_CompletesWithoutQueuePump()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        syncContext.RunAll();
        var sessionId = Guid.NewGuid().ToString("N");

        var original = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            var switchTask = viewModel.TrySwitchToSessionAsync(sessionId);
            var completed = await Task.WhenAny(switchTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(switchTask, completed);
            Assert.True(await switchTask);
            Assert.Equal(sessionId, viewModel.CurrentSessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    [Fact]
    public async Task PlanEntries_CollectionChanges_RaiseDerivedPropertyNotifications()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var raised = new List<string>();
        syncContext.RunAll();

        viewModel.ShowPlanPanel = true;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        viewModel.PlanEntries.Add(new PlanEntryViewModel
        {
            Content = "Step 1"
        });

        await Task.Yield();

        Assert.Contains(nameof(ChatViewModel.HasPlanEntries), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowPlanList), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowPlanEmpty), raised);
        Assert.True(viewModel.HasPlanEntries);
        Assert.True(viewModel.ShouldShowPlanList);
        Assert.False(viewModel.ShouldShowPlanEmpty);
    }

    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback callback, object? state)> _work = new();

        public int PendingCount => _work.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            _work.Enqueue((d, state));
        }

        public void RunAll()
        {
            while (_work.Count > 0)
            {
                var (callback, state) = _work.Dequeue();
                callback(state);
            }
        }
    }

    private sealed class ViewModelFixture : IDisposable, IAsyncDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IChatStore _store;
        public ChatViewModel ViewModel { get; }

        public ViewModelFixture(ChatViewModel viewModel, IState<ChatState> state, IChatStore store)
        {
            ViewModel = viewModel;
            _state = state;
            _store = store;
        }

        public async Task<ChatState> GetStateAsync() => await _state ?? ChatState.Empty;

        public ValueTask DispatchAsync(ChatAction action) => _store.Dispatch(action);

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            await _state.DisposeAsync();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
