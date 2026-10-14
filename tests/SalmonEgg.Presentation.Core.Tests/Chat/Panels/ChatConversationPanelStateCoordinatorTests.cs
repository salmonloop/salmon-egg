using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Panels;

public sealed class ChatConversationPanelStateCoordinatorTests
{
    [Fact]
    public void PendingRequests_HoldConnectionUntilEveryOriginalRequestIsReleased()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        using var service = new AcpChatServiceAdapter(Mock.Of<IChatService>(), new AcpEventAdapter(_ => { }, new ImmediateUiDispatcher()));
        var session = new AcpConnectionSession("profile", service, new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, "", "", ""), "connection");
        registry.Upsert(session);
        var sut = new ChatConversationPanelStateCoordinator(new ImmediateUiDispatcher(), registry);
        var permission = new PermissionRequestViewModel { Source = session.EventSource };
        var question = new AskUserRequestViewModel("question", "remote", "Prompt", []) { Source = session.EventSource };

        sut.StorePermissionRequest("conversation", permission);
        sut.StoreAskUserRequest("conversation", question);
        Assert.False(registry.TryEvict(session));
        sut.RemovePermissionRequest("conversation", permission);
        Assert.False(registry.TryEvict(session));
        sut.RemoveAskUserRequest("conversation", question);

        Assert.True(registry.TryEvict(session));
    }

    [Fact]
    public void StoreRequest_AfterConnectionWasEvicted_DoesNotPublishUnanswerablePrompt()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        using var service = new AcpChatServiceAdapter(Mock.Of<IChatService>(), new AcpEventAdapter(_ => { }, new ImmediateUiDispatcher()));
        var session = new AcpConnectionSession("profile", service, new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, "", "", ""), "connection");
        registry.Upsert(session);
        var sut = new ChatConversationPanelStateCoordinator(new ImmediateUiDispatcher(), registry);
        Assert.True(registry.TryEvict(session));

        sut.StoreAskUserRequest("conversation", new("question", "remote", "Prompt", []) { Source = session.EventSource });

        Assert.Null(sut.GetPendingAskUserRequest("conversation"));
    }

    [Fact]
    public void SyncConversation_WithoutExistingState_CreatesTerminalPanelSelection()
    {
        var sut = new ChatConversationPanelStateCoordinator();

        var selection = sut.SyncConversation("conv-1");

        Assert.Empty(selection.TerminalSessions);
        Assert.Null(selection.SelectedTerminal);
        Assert.Null(selection.PendingAskUserRequest);
    }

    [Fact]
    public void SelectTerminal_ForCurrentConversation_ReturnsSelectedTerminalSnapshot()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        sut.SyncConversation("conv-1");
        var terminal = sut.GetOrCreateTerminalSession("conv-1", "term-1");

        var selection = sut.SelectTerminal("conv-1", terminal, isCurrentConversation: true);

        Assert.Single(selection.TerminalSessions);
        Assert.Same(terminal, selection.SelectedTerminal);
    }

    [Fact]
    public void StoreAskUserRequest_ExposesPendingRequestForConversation()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        var request = new AskUserRequestViewModel("message-1", "remote-1", "prompt", []);

        sut.StoreAskUserRequest("conv-1", request);

        var selection = sut.SyncConversation("conv-1");
        Assert.Same(request, selection.PendingAskUserRequest);
    }

    [Fact]
    public void RemoveConversation_WhenCurrentConversation_ReturnsEmptySelection()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        sut.SyncConversation("conv-1");
        var terminal = sut.GetOrCreateTerminalSession("conv-1", "term-1");
        sut.SelectTerminal("conv-1", terminal, isCurrentConversation: true);
        sut.StoreAskUserRequest("conv-1", new AskUserRequestViewModel("message-1", "remote-1", "prompt", []));

        var selection = sut.RemoveConversation("conv-1", isCurrentConversation: true);

        Assert.Empty(selection.TerminalSessions);
        Assert.Null(selection.SelectedTerminal);
        Assert.Null(selection.PendingAskUserRequest);
    }

    [Fact]
    public void RemoveElicitationRequest_WhenPreviousOwnerCompletes_DoesNotRemoveReusedId()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        var previous = new ElicitationRequestViewModel("same-id", "remote", "First", []);
        var current = new ElicitationRequestViewModel("same-id", "remote", "Second", []);
        Assert.True(sut.TryStoreElicitationRequest("conversation", previous));
        sut.ClearElicitationRequests();
        Assert.True(sut.TryStoreElicitationRequest("conversation", current));

        Assert.False(sut.RemoveElicitationRequest("conversation", previous));

        Assert.Same(current, sut.GetPendingElicitationRequest("conversation"));
        Assert.True(sut.RemoveElicitationRequest("conversation", current));
        Assert.Null(sut.GetPendingElicitationRequest("conversation"));
    }

    [Fact]
    public void GetSummary_WhenBackgroundRequestChanges_PublishesItsConversationAndReadsOriginalRequest()
    {
        // Arrange
        var sut = new ChatConversationPanelStateCoordinator();
        var request = new AskUserRequestViewModel("request", "remote", "Question", []);
        var changes = new List<string>();
        sut.Changed += changes.Add;

        // Act
        sut.StoreAskUserRequest("background", request);
        request.ErrorMessage = "Could not deliver answer";

        // Assert
        Assert.True(sut.GetSummary("background").HasInputRequest);
        Assert.True(sut.GetSummary("background").HasFailure);
        Assert.False(sut.GetSummary("foreground").HasInputRequest);
        Assert.All(changes, conversation => Assert.Equal("background", conversation));
        Assert.True(changes.Count >= 2);
    }

    [Fact]
    public void RetireConnection_WhenSameConversationHasNewConnectionRequest_RetainsNewRequestAndDetachesOldChanges()
    {
        // Arrange
        var sut = new ChatConversationPanelStateCoordinator();
        var oldSource = new AcpSessionEventSource("profile", "old", Mock.Of<IChatService>());
        var newSource = new AcpSessionEventSource("profile", "new", Mock.Of<IChatService>());
        var previous = new AskUserRequestViewModel("same-id", "remote", "Old", []) { Source = oldSource };
        var current = new AskUserRequestViewModel("same-id", "remote", "New", []) { Source = newSource };
        sut.StoreAskUserRequest("conversation", previous);
        sut.StoreAskUserRequest("conversation", current);
        var changed = 0;
        sut.Changed += _ => changed++;

        // Act
        sut.RetireConnection(oldSource);
        sut.RemoveAskUserRequest("conversation", previous);
        previous.ErrorMessage = "Late old error";

        // Assert
        Assert.Same(current, sut.GetPendingAskUserRequest("conversation"));
        Assert.Equal(0, changed);
        sut.RetireConnection(newSource);
        Assert.False(sut.GetSummary("conversation").HasInputRequest);
    }

    [Fact]
    public async Task GetPendingConnectionSourcesAsync_WhenPermissionExpires_ReleasesProtectionWithoutCopyingState()
    {
        // Arrange
        var sut = new ChatConversationPanelStateCoordinator();
        var available = true;
        var source = new AcpSessionEventSource("profile", "connection", Mock.Of<IChatService>());
        var request = new PermissionRequestViewModel { Source = source, IsRequestAvailable = () => available };
        sut.StorePermissionRequest("conversation", request);
        Assert.Single(await sut.GetPendingConnectionSourcesAsync(TestContext.Current.CancellationToken));

        // Act
        available = false;
        sut.NotifyPermissionRequestChanged("conversation", request);

        // Assert
        Assert.Empty(await sut.GetPendingConnectionSourcesAsync(TestContext.Current.CancellationToken));
        Assert.False(sut.GetSummary("conversation").HasPermissionRequest);
    }
}
