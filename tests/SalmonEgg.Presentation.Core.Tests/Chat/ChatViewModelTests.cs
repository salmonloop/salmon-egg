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
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SerilogLogger = Serilog.ILogger;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public class ChatViewModelTests
{
    private static ChatViewModel CreateViewModel(SynchronizationContext? syncContext = null)
    {
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
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext ?? new SynchronizationContext());

            return new ChatViewModel(
                chatServiceFactory,
                configService.Object,
                preferences,
                profiles,
                sessionManager.Object,
                conversationStore.Object,
                vmLogger.Object);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task TrySwitchToSessionAsync_NewSession_DoesNotSeedRemoteSessionId()
    {
        var viewModel = CreateViewModel();
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
        var viewModel = CreateViewModel(syncContext);
        var sessionId = Guid.NewGuid().ToString("N");

        var switchTask = viewModel.TrySwitchToSessionAsync(sessionId);
        for (var i = 0; i < 4 && !switchTask.IsCompleted; i++)
        {
            Assert.True(await syncContext.WaitForPostAsync(TimeSpan.FromSeconds(1)));
            syncContext.RunAll();
        }

        var completed = await Task.WhenAny(switchTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(switchTask, completed);
        await switchTask;

        Assert.Equal(sessionId, viewModel.CurrentSessionId);
    }

    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback callback, object? state)> _work = new();
        private readonly SemaphoreSlim _posted = new(0);

        public override void Post(SendOrPostCallback d, object? state)
        {
            _work.Enqueue((d, state));
            _posted.Release();
        }

        public Task<bool> WaitForPostAsync(TimeSpan timeout)
        {
            return _posted.WaitAsync(timeout);
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
}
