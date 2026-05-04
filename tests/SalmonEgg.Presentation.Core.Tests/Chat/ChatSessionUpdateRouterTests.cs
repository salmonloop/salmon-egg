using Moq;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatSessionUpdateRouterTests
{
    [Fact]
    public void Route_WhenAvailableCommandsUpdate_ReturnsProjectedDelta()
    {
        var args = new SessionUpdateEventArgs("remote-1", new AvailableCommandsUpdate());
        var expected = new AcpSessionUpdateDelta(
            AvailableCommands: [new AcpAvailableCommandSnapshot("plan", "Planning command", "target")]);
        var projector = new Mock<IAcpSessionUpdateProjector>(MockBehavior.Strict);
        projector.Setup(x => x.Project(args)).Returns(expected);
        var router = new ChatSessionUpdateRouter(projector.Object);

        var route = router.Route(args, isConversationConfigAuthoritative: false);

        Assert.True(route.Handled);
        Assert.False(route.Ignored);
        Assert.False(route.ShouldSetConfigAuthoritative);
        Assert.Equal(expected, route.Delta);
    }

    [Fact]
    public void Route_WhenCurrentModeUpdateAndConfigIsAuthoritative_IgnoresLegacyModeProjection()
    {
        var args = new SessionUpdateEventArgs("remote-1", new CurrentModeUpdate("plan"));
        var projector = new Mock<IAcpSessionUpdateProjector>(MockBehavior.Strict);
        var router = new ChatSessionUpdateRouter(projector.Object);

        var route = router.Route(args, isConversationConfigAuthoritative: true);

        Assert.True(route.Handled);
        Assert.True(route.Ignored);
        Assert.Equal("ConfigOptionsAuthoritative", route.IgnoredReason);
        projector.Verify(x => x.Project(It.IsAny<SessionUpdateEventArgs>()), Times.Never);
    }

    [Fact]
    public void Route_WhenConfigOptionUpdateContainsConfigOptions_SetsConversationConfigAuthority()
    {
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new ConfigOptionUpdate
            {
                ConfigOptions = []
            });
        var expected = new AcpSessionUpdateDelta(ConfigOptions: []);
        var projector = new Mock<IAcpSessionUpdateProjector>(MockBehavior.Strict);
        projector.Setup(x => x.Project(args)).Returns(expected);
        var router = new ChatSessionUpdateRouter(projector.Object);

        var route = router.Route(args, isConversationConfigAuthoritative: false);

        Assert.True(route.Handled);
        Assert.False(route.Ignored);
        Assert.True(route.ShouldSetConfigAuthoritative);
        Assert.Equal(expected, route.Delta);
    }

    [Fact]
    public void Route_WhenUpdateIsUnhandled_ReturnsUnhandled()
    {
        var args = new SessionUpdateEventArgs("remote-1", new AgentThoughtUpdate());
        var projector = new Mock<IAcpSessionUpdateProjector>(MockBehavior.Strict);
        var router = new ChatSessionUpdateRouter(projector.Object);

        var route = router.Route(args, isConversationConfigAuthoritative: false);

        Assert.False(route.Handled);
        Assert.False(route.Ignored);
        Assert.Null(route.Delta);
    }
}
