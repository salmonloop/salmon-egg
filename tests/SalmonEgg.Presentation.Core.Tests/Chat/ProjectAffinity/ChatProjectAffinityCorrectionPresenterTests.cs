using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.ProjectAffinity;

public sealed class ChatProjectAffinityCorrectionPresenterTests
{
    private readonly ChatProjectAffinityCorrectionPresenter _sut = new(new ProjectAffinityResolver());

    [Fact]
    public void Present_WithoutConversation_ReturnsHiddenEmptyState()
    {
        var state = _sut.Present(new ChatProjectAffinityCorrectionInput(
            ConversationId: null,
            RemoteSessionId: null,
            BoundProfileId: null,
            RemoteCwd: null,
            OverrideProjectId: null,
            SelectedOverrideProjectId: "project-1",
            Projects: new List<ProjectDefinition>(),
            PathMappings: new List<ProjectPathMapping>()));

        Assert.False(state.IsVisible);
        Assert.False(state.HasOverride);
        Assert.Null(state.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, state.EffectiveSource);
        Assert.Equal(string.Empty, state.Message);
        Assert.Null(state.SelectedOverrideProjectId);
        Assert.Empty(state.Options);
    }

    [Fact]
    public void Present_RemoteBoundUnclassifiedConversation_ShowsCorrectionWithSortedOptions()
    {
        var state = _sut.Present(new ChatProjectAffinityCorrectionInput(
            ConversationId: "conv-1",
            RemoteSessionId: "remote-1",
            BoundProfileId: "profile-1",
            RemoteCwd: @"C:\repo\unknown",
            OverrideProjectId: null,
            SelectedOverrideProjectId: null,
            Projects:
            [
                new ProjectDefinition { ProjectId = "project-b", Name = "Zulu" },
                new ProjectDefinition { ProjectId = "project-a", Name = "Alpha" }
            ],
            PathMappings: new List<ProjectPathMapping>()));

        Assert.True(state.IsVisible);
        Assert.False(state.HasOverride);
        Assert.Equal(NavigationProjectIds.Unclassified, state.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.NeedsMapping, state.EffectiveSource);
        Assert.Equal("远程会话未匹配到本地项目，请手动更正。", state.Message);
        Assert.Collection(
            state.Options,
            first => Assert.Equal("Alpha", first.DisplayName),
            second => Assert.Equal("Zulu", second.DisplayName));
    }

    [Fact]
    public void Present_WithOverride_PreservesOverrideSelection()
    {
        var state = _sut.Present(new ChatProjectAffinityCorrectionInput(
            ConversationId: "conv-1",
            RemoteSessionId: "remote-1",
            BoundProfileId: "profile-1",
            RemoteCwd: @"C:\repo\unknown",
            OverrideProjectId: "project-1",
            SelectedOverrideProjectId: null,
            Projects:
            [
                new ProjectDefinition { ProjectId = "project-1", Name = "Project 1" }
            ],
            PathMappings: new List<ProjectPathMapping>()));

        Assert.True(state.IsVisible);
        Assert.True(state.HasOverride);
        Assert.Equal("project-1", state.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Override, state.EffectiveSource);
        Assert.Equal("已应用本地项目覆盖，可随时清除。", state.Message);
        Assert.Equal("project-1", state.SelectedOverrideProjectId);
    }

    [Fact]
    public void Present_WithStaleSelectedOverride_ClearsSelection()
    {
        var state = _sut.Present(new ChatProjectAffinityCorrectionInput(
            ConversationId: "conv-1",
            RemoteSessionId: "remote-1",
            BoundProfileId: "profile-1",
            RemoteCwd: @"C:\repo\unknown",
            OverrideProjectId: null,
            SelectedOverrideProjectId: "missing-project",
            Projects:
            [
                new ProjectDefinition { ProjectId = "project-1", Name = "Project 1" }
            ],
            PathMappings: new List<ProjectPathMapping>()));

        Assert.Null(state.SelectedOverrideProjectId);
    }
}
