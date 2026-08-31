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
            IsSelectionResolved: false,
            Labels: Labels()));

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
            IsSelectionResolved: false,
            Labels: Labels()));

        Assert.Equal("agent-error", projection.Placeholder!.DisplayName);
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
            IsSelectionResolved: true,
            Labels: Labels()));

        Assert.Null(projection.Placeholder);
        Assert.False(projection.DisableRealItems);
        Assert.Equal("profile-1", projection.SelectedSemanticValue);
    }

    [Fact]
    public void Project_WhenNoAgentSlotsAreAvailable_ReturnsNonBlockingEmptyPlaceholder()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "agent||",
            Agents: Array.Empty<ServerConfiguration>(),
            SelectedProfileId: null,
            IsConnecting: false,
            HasConnectionError: false,
            IsSelectionResolved: true,
            Labels: Labels()));

        Assert.Equal(SelectorPlaceholderKind.Default, projection.Placeholder!.PlaceholderKind);
        Assert.Equal("agent-empty", projection.Placeholder.DisplayName);
        Assert.False(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
        Assert.False(projection.SelectorEnabled);
    }

    private static ServerConfiguration Agent(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Transport = TransportType.StreamableHttp,
            ServerUrl = "https://example.test"
        };

    private static AgentSelectorPlaceholderLabels Labels()
        => new(
            Loading: "agent-loading",
            Error: "agent-error",
            Unresolved: "agent-unresolved",
            Empty: "agent-empty");
}
