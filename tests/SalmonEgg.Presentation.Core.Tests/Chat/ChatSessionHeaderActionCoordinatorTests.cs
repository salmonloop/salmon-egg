using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Session;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatSessionHeaderActionCoordinatorTests
{
    [Fact]
    public void TryBeginEditSessionName_WhenSessionIsActive_ReturnsDisplayName()
    {
        var coordinator = new ChatSessionHeaderActionCoordinator();

        var started = coordinator.TryBeginEditSessionName(true, "conversation-1", "My Session", out var editingName);

        Assert.True(started);
        Assert.Equal("My Session", editingName);
    }

    [Fact]
    public void CommitSessionName_WhenInputSanitizesEmpty_UsesDefault()
    {
        var coordinator = new ChatSessionHeaderActionCoordinator();

        var finalName = coordinator.CommitSessionName("conversation-1", "   ");

        Assert.Equal(SessionNamePolicy.CreateDefault("conversation-1"), finalName);
    }

    [Fact]
    public void TryApplyProjectAffinityOverride_WhenInputsAreValid_PersistsOverride()
    {
        var coordinator = new ChatSessionHeaderActionCoordinator();
        var workspace = new ChatConversationWorkspace(
            Mock.Of<ISessionManager>(),
            Mock.Of<IConversationStore>(),
            Mock.Of<IConversationWorkspacePreferences>(),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            new InlineUiDispatcher());

        var applied = coordinator.TryApplyProjectAffinityOverride(workspace, "conversation-1", "project-1");

        Assert.True(applied);
        Assert.Equal("project-1", workspace.GetProjectAffinityOverride("conversation-1")?.ProjectId);
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
