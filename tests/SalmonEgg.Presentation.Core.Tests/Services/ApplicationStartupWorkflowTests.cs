using Moq;
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
        var workflow = new ApplicationStartupWorkflow(shellStartup.Object, chatRuntime.Object);

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
            chatRuntime.Object);

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
            chatRuntime.Object);

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
            chatRuntime.Object);

        await workflow.InitializeRuntimeAsync();
        await workflow.InitializeRuntimeAsync();

        chatRuntime.Verify(runtime => runtime.InitializeAcpProfilesAsync(), Times.Once);
        chatRuntime.Verify(runtime => runtime.RestoreConversationsAsync(), Times.Exactly(2));
    }
}
