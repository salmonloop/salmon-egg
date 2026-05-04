using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProfileSelection;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.ProfileSelection;

[Collection("NonParallel")]
public sealed class ChatAcpProfileSelectionResolverTests
{
    private readonly ChatAcpProfileSelectionResolver _sut = new();

    [Fact]
    public void ResolveById_WithMatchingId_ReturnsProfile()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Name = "Agent A" };

        var result = _sut.ResolveById([profile], "profile-1");

        Assert.Same(profile, result);
    }

    [Fact]
    public void ResolveLoadedProfileSelection_WithId_PrefersCanonicalListInstance()
    {
        var canonical = new ServerConfiguration { Id = "profile-1", Name = "Agent A" };
        var detached = new ServerConfiguration { Id = "profile-1", Name = "Detached" };

        var result = _sut.ResolveLoadedProfileSelection([canonical], detached);

        Assert.Same(canonical, result);
    }

    [Fact]
    public void ResolveLoadedProfileSelection_WithoutId_FallsBackToReferenceMatch()
    {
        var profile = new ServerConfiguration { Name = "Agent A" };
        var profiles = new List<ServerConfiguration> { profile };

        var result = _sut.ResolveLoadedProfileSelection(profiles, profile);

        Assert.Same(profile, result);
    }
}
