using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class NoOpGamepadInputServiceTests
{
    [Fact]
    public void NoOpGamepadInputService_StartAndStop_DoNotRaiseIntent()
    {
        var service = new NoOpGamepadInputService();
        var raised = false;
        service.IntentRaised += (_, _) => raised = true;

        service.Start();
        service.Stop();

        Assert.False(raised);
    }

    [Fact]
    public void NoOpGamepadInputService_StartAndStop_DoNotRaiseShortcut()
    {
        var service = new NoOpGamepadInputService();
        var raised = false;
        service.ShortcutRaised += (_, _) => raised = true;

        service.Start();
        service.Stop();

        Assert.False(raised);
    }

    [Fact]
    public void NoOpGamepadInputService_StartAndStop_DoNotRaiseContextIntent()
    {
        var service = new NoOpGamepadInputService();
        var raised = false;
        service.ContextIntentRaised += (_, _) => raised = true;

        service.Start();
        service.Stop();

        Assert.False(raised);
    }

    [Fact]
    public void NoOpGamepadInputService_StartStopAndDispose_RemainEventSilent()
    {
        var service = new NoOpGamepadInputService();
        var navigationRaised = false;
        var shortcutRaised = false;
        var contextRaised = false;
        service.IntentRaised += (_, _) => navigationRaised = true;
        service.ShortcutRaised += (_, _) => shortcutRaised = true;
        service.ContextIntentRaised += (_, _) => contextRaised = true;

        service.Start();
        service.Stop();
        service.Dispose();

        Assert.False(navigationRaised);
        Assert.False(shortcutRaised);
        Assert.False(contextRaised);
    }
}
