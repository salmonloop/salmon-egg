using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

namespace SalmonEgg.Presentation.Core.Tests.Chat.ProjectAffinity;

public sealed class ChatProjectAffinityCorrectionCoordinatorTests
{
    [Fact]
    public void Present_WhenCurrentConversationNeedsFallbackRemoteBinding_UsesCurrentRuntimeValues()
    {
        var resolver = new ProjectAffinityResolver();
        var coordinator = new ChatProjectAffinityCorrectionCoordinator(resolver);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(x => x.GetSession("conversation-1"))
            .Returns(new Session { SessionId = "conversation-1", Cwd = "C:\\repo" });
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            Mock.Of<IConversationStore>(),
            Mock.Of<IConversationWorkspacePreferences>(),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            new InlineUiDispatcher());

        var state = coordinator.Present(
            workspace,
            sessionManager.Object,
            requestedConversationId: null,
            currentConversationId: "conversation-1",
            currentRemoteSessionId: "remote-1",
            selectedProfileId: "profile-1",
            selectedOverrideProjectId: null,
            projects: [],
            pathMappings: []);

        Assert.True(state.IsVisible);
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;
        public void Enqueue(Action action) => action();
        public Task EnqueueAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> action) => action();
    }
}
