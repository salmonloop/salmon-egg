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
}
