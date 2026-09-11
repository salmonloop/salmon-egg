using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData("form")]
    [InlineData("url")]
    public async Task Elicitation_SessionCancellation_RetiresTheFinishedCard(string mode)
    {
        // Arrange: the production SDK, ChatService and ChatViewModel hold the visible request.
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("first", mode, "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var previous = fixture.ViewModel.PendingElicitationRequest;
        Assert.NotNull(previous);

        // Act: cancellation originates outside the card's own CancelCommand.
        await AwaitWithSynchronizationContextAsync(dispatcher,
            peer.Service.CancelSessionAsync(new SessionCancelParams("remote-1")));
        await dispatcher.RunUntilIdleAsync();

        // Assert: the SDK has delivered the terminal response; the UI must release this exact slot.
        Assert.Single(peer.Responses);
        Assert.False(Assert.Single(peer.Elicitations).State.CanCancel);
        Assert.True(peer.IsConnected);
        var cancelledCard = fixture.ViewModel.PendingElicitationRequest;
        peer.Elicit("second", "form", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"Mode={mode}; CancelledCardStillVisible={cancelledCard is not null}; "
            + $"CurrentCard={fixture.ViewModel.PendingElicitationRequest?.MessageId}; "
            + $"WireReplies={string.Join(";", peer.Responses.Select(response => response.GetRawText()))}");
        Assert.Null(cancelledCard);
        Assert.Equal("second", fixture.ViewModel.PendingElicitationRequest?.MessageId.ToString());
        Assert.Single(peer.Responses);
    }
}
