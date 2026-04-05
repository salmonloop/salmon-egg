using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Threading;

public sealed class ThreadingComplianceTests
{
    [Fact]
    public async Task ActivateSessionAsync_WhenSwitcherIsSlow_DoesNotBlockCallerThread()
    {
        var selectionStore = new ShellSelectionStateStore();
        var switchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSwitchCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var switcher = new Mock<IConversationSessionSwitcher>();
        switcher
            .Setup(x => x.SwitchConversationAsync("session-1", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                switchStarted.TrySetResult(null);
                await allowSwitchCompletion.Task;
                return true;
            });

        var shellNavigation = new Mock<IShellNavigationService>();
        shellNavigation.Setup(x => x.NavigateToChat()).Returns(ValueTask.FromResult(ShellNavigationResult.Success()));

        var coordinator = new NavigationCoordinator(
            selectionStore,
            selectionStore,
            switcher.Object,
            Mock.Of<INavigationProjectSelectionStore>(),
            shellNavigation.Object,
            Mock.Of<ILogger<NavigationCoordinator>>());

        var invokeStopwatch = Stopwatch.StartNew();
        var activationTask = coordinator.ActivateSessionAsync("session-1", "project-1");
        invokeStopwatch.Stop();

        Assert.True(invokeStopwatch.ElapsedMilliseconds < 1000, $"Invoke path was unexpectedly slow. elapsedMs={invokeStopwatch.ElapsedMilliseconds}");
        await switchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(activationTask.IsCompleted, "Activation should still be pending while switcher is blocked.");

        allowSwitchCompletion.TrySetResult(null);
        Assert.True(await activationTask);

        shellNavigation.Verify(x => x.NavigateToChat(), Times.Once);
        switcher.Verify(x => x.SwitchConversationAsync("session-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
