using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ApplicationStartupWorkflowTests
{
    [Fact]
    public async Task ActivateShellAsync_DelegatesToShellStartupOwner()
    {
        var shellStartup = new Mock<IShellStartupNavigationService>(MockBehavior.Strict);
        shellStartup.Setup(service => service.ActivateInitialContentAsync()).Returns(Task.CompletedTask);
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        var workflow = new ApplicationStartupWorkflow(
            shellStartup.Object,
            chatRuntime.Object,
            configurationRecoveryService: null,
            CreateSettingsService(),
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.ActivateShellAsync();

        shellStartup.Verify(service => service.ActivateInitialContentAsync(), Times.Once);
        chatRuntime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenCalledConcurrently_SharesProfileAndRestoreTasks()
    {
        var profileStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowProfileCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestoreCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime
            .Setup(runtime => runtime.InitializeAcpProfilesAsync())
            .Returns(async () =>
            {
                profileStarted.TrySetResult(null);
                await allowProfileCompletion.Task;
                return true;
            });
        chatRuntime
            .Setup(runtime => runtime.RestoreConversationsAsync())
            .Returns(async () =>
            {
                restoreStarted.TrySetResult(null);
                await allowRestoreCompletion.Task;
                return true;
            });
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            configurationRecoveryService: null,
            CreateSettingsService(),
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        var firstInitialization = workflow.InitializeRuntimeAsync();
        await Task.WhenAll(profileStarted.Task, restoreStarted.Task);
        var secondInitialization = workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Once);

        allowProfileCompletion.SetResult(null);
        allowRestoreCompletion.SetResult(null);
        await Task.WhenAll(firstInitialization, secondInitialization);
        await workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenCalledConcurrently_SharesRecoveryAndRunsItBeforeRuntimeInitialization()
    {
        var recoveryStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRecovery = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new Mock<IConfigurationRecoveryService>(MockBehavior.Strict);
        recovery
            .Setup(service => service.RecoverPendingTransactionsAsync(default))
            .Returns(async () =>
            {
                recoveryStarted.TrySetResult(null);
                await allowRecovery.Task;
            });
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime.Setup(runtime => runtime.InitializeAcpProfilesAsync()).ReturnsAsync(true);
        chatRuntime.Setup(runtime => runtime.RestoreConversationsAsync()).ReturnsAsync(true);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            recovery.Object,
            CreateSettingsService(),
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        var first = workflow.InitializeRuntimeAsync();
        await recoveryStarted.Task;
        var second = workflow.InitializeRuntimeAsync();

        recovery.Verify(service => service.RecoverPendingTransactionsAsync(default), Times.Once);
        chatRuntime.VerifyNoOtherCalls();
        allowRecovery.SetResult(null);
        await Task.WhenAll(first, second);
        await workflow.InitializeRuntimeAsync();

        recovery.Verify(service => service.RecoverPendingTransactionsAsync(default), Times.Once);
        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenProfileInitializationFails_RetriesOnlyProfiles()
    {
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime
            .SetupSequence(runtime => runtime.InitializeAcpProfilesAsync())
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        chatRuntime
            .Setup(runtime => runtime.RestoreConversationsAsync())
            .ReturnsAsync(true);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            configurationRecoveryService: null,
            CreateSettingsService(),
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();
        await workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Exactly(2));
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenConversationRestoreFails_RetriesOnlyRestore()
    {
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime
            .Setup(runtime => runtime.InitializeAcpProfilesAsync())
            .ReturnsAsync(true);
        chatRuntime
            .SetupSequence(runtime => runtime.RestoreConversationsAsync())
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            configurationRecoveryService: null,
            CreateSettingsService(),
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();
        await workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task InitializeRuntimeAsync_ActivatesTelemetryBeforeRuntimeInitialization()
    {
        // 遥测必须先激活：否则 profile 初始化与会话恢复这两段最需要 trace 的启动路径
        // 发生在没有 provider 的窗口内，永久采集不到。
        var order = new List<string>();
        var telemetry = new Mock<ITelemetryRuntime>(MockBehavior.Strict);
        telemetry
            .Setup(runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => order.Add("telemetry"));
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime
            .Setup(runtime => runtime.InitializeAcpProfilesAsync())
            .ReturnsAsync(true)
            .Callback(() => order.Add("profiles"));
        chatRuntime
            .Setup(runtime => runtime.RestoreConversationsAsync())
            .ReturnsAsync(true)
            .Callback(() => order.Add("restore"));
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            configurationRecoveryService: null,
            CreateSettingsService(),
            telemetry.Object,
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();

        Assert.Equal("telemetry", order[0]);
        Assert.Contains("profiles", order);
        Assert.Contains("restore", order);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_AppliesPersistedTelemetrySettings()
    {
        // 必须传"磁盘上的那一份"，而不是新建的默认 AppSettings：否则用户关闭遥测的意图
        // 在启动时会被忽略，直到下一次保存才生效。
        var persisted = new AppSettings
        {
            TelemetrySharingEnabled = false,
            TelemetryCustomEndpoint = "http://collector.internal:4318"
        };
        AppSettings? applied = null;
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Callback<AppSettings, CancellationToken>((settings, _) => applied = settings)
            .Returns(Task.CompletedTask);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            CreateSucceedingChatRuntime(),
            configurationRecoveryService: null,
            CreateSettingsService(persisted),
            telemetry.Object,
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();

        Assert.Same(persisted, applied);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenSettingsLoadThrows_StillInitializesRuntime()
    {
        // 遥测是旁路能力：读配置失败不得让用户完全打不开会话。
        var settingsService = new Mock<IAppSettingsService>();
        settingsService
            .Setup(service => service.LoadAsync())
            .ThrowsAsync(new UnauthorizedAccessException("config unreadable"));
        var chatRuntime = new Mock<IChatRuntimeInitialization>(MockBehavior.Strict);
        chatRuntime.Setup(runtime => runtime.InitializeAcpProfilesAsync()).ReturnsAsync(true);
        chatRuntime.Setup(runtime => runtime.RestoreConversationsAsync()).ReturnsAsync(true);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            chatRuntime.Object,
            configurationRecoveryService: null,
            settingsService.Object,
            Mock.Of<ITelemetryRuntime>(),
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenCalledConcurrently_SharesInFlightTelemetryActivation()
    {
        // 多个页面并发挂载时不得各自激活一次。用真正在途的 LoadAsync 来验证共享：若用同步完成的
        // 桩，第二次调用时首个任务已 IsCompleted，共享逻辑根本不会被触发，测试会假绿。
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoad = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settingsService = new Mock<IAppSettingsService>();
        settingsService
            .Setup(service => service.LoadAsync())
            .Returns(() =>
            {
                loadStarted.TrySetResult();
                return allowLoad.Task;
            });
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            CreateSucceedingChatRuntime(),
            configurationRecoveryService: null,
            settingsService.Object,
            telemetry.Object,
            NullLogger<ApplicationStartupWorkflow>.Instance);

        var first = workflow.InitializeRuntimeAsync();

        // 有界等待：若激活步骤被移除，LoadAsync 永远不会被调用。裸 await 会让本测试挂死而不是
        // 失败——挂死的门禁等于没有门禁（CI 只会超时，看不出是哪条不变式破了）。
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = workflow.InitializeRuntimeAsync();
        allowLoad.SetResult(new AppSettings());
        await Task.WhenAll(first, second);

        settingsService.Verify(service => service.LoadAsync(), Times.Once);
        telemetry.Verify(
            runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeRuntimeAsync_WhenRetriedAfterCompletion_ReappliesIdempotently()
    {
        // 刻意不记 completed 标志：ApplyAsync 契约上幂等（配置未变即 no-op），而记了标志会让
        // 首次失败后再也不重试。重复 apply 是可接受成本，永久不重试不是。
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var workflow = new ApplicationStartupWorkflow(
            Mock.Of<IShellStartupNavigationService>(),
            CreateSucceedingChatRuntime(),
            configurationRecoveryService: null,
            CreateSettingsService(),
            telemetry.Object,
            NullLogger<ApplicationStartupWorkflow>.Instance);

        await workflow.InitializeRuntimeAsync();
        await workflow.InitializeRuntimeAsync();

        telemetry.Verify(
            runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static IChatRuntimeInitialization CreateSucceedingChatRuntime()
    {
        var chatRuntime = new Mock<IChatRuntimeInitialization>();
        chatRuntime.Setup(runtime => runtime.InitializeAcpProfilesAsync()).ReturnsAsync(true);
        chatRuntime.Setup(runtime => runtime.RestoreConversationsAsync()).ReturnsAsync(true);
        return chatRuntime.Object;
    }

    private static IAppSettingsService CreateSettingsService(AppSettings? settings = null)
    {
        var service = new Mock<IAppSettingsService>();
        service.Setup(s => s.LoadAsync()).ReturnsAsync(settings ?? new AppSettings());
        return service.Object;
    }
}
