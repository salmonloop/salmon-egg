using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class AgentSelectorPolicyTests
{
    [Fact]
    public void Project_WhenConnecting_KeepsAgentsVisibleAndAddsBlockingLoadingPlaceholder()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|connecting",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: true,
            HasConnectionError: false,
            IsSelectionResolved: false));

        Assert.Equal(SelectorPlaceholderKind.Loading, projection.Placeholder!.PlaceholderKind);
        Assert.False(projection.ReplaceSelectionWithPlaceholder);
        Assert.False(projection.DisableRealItems);
        Assert.True(projection.Placeholder.BlocksSubmit);
        Assert.Single(projection.RealItems);
    }

    [Fact]
    public void Project_WhenConnectionFailed_AddsGenericErrorPlaceholder()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|error",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: false,
            HasConnectionError: true,
            IsSelectionResolved: false));

        Assert.Equal("Agent unavailable", projection.Placeholder!.DisplayName);
        Assert.Equal(SelectorPlaceholderKind.Error, projection.Placeholder.PlaceholderKind);
        Assert.True(projection.Placeholder.BlocksSubmit);
    }

    [Fact]
    public void Project_WhenSelectionResolved_UsesRealAgentItems()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|ready",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: false,
            HasConnectionError: false,
            IsSelectionResolved: true));

        Assert.Null(projection.Placeholder);
        Assert.False(projection.DisableRealItems);
        Assert.Equal("profile-1", projection.SelectedSemanticValue);
    }

    private static ServerConfiguration Agent(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Transport = TransportType.HttpSse,
            ServerUrl = "https://example.test"
        };
}
