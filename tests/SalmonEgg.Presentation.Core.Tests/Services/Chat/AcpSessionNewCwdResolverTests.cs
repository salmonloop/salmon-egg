using System;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class AcpSessionNewCwdResolverTests
{
    [Fact]
    public void Resolve_StdioWithoutRequestedCwd_UsesUserProfileDirectory()
    {
        var profile = new ServerConfiguration
        {
            Transport = TransportType.Stdio
        };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: null,
            profile: profile,
            pathMappings: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithoutRequestedCwd_ReturnsFailure()
    {
        var profile = new ServerConfiguration
        {
            Transport = TransportType.WebSocket
        };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: "  ",
            profile: profile,
            pathMappings: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSessionNewCwdResolver.MissingRemoteCwdMessage, result.ErrorMessage);
        Assert.Null(result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithMatchingPathMapping_MapsToRemoteRoot()
    {
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Transport = TransportType.WebSocket
        };

        var mappings = new[]
        {
            new ProjectPathMapping
            {
                ProfileId = "profile-1",
                LocalRootPath = @"C:\repos\workspace\project",
                RemoteRootPath = @"/home/user/project"
            }
        };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: @"C:\repos\workspace\project\src",
            profile: profile,
            pathMappings: mappings);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"/home/user/project/src", result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithoutMatch_ReturnsOriginalTrimmedCwd()
    {
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Transport = TransportType.WebSocket
        };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: @"C:\repos\unmapped\project",
            profile: profile,
            pathMappings: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\repos\unmapped\project", result.Cwd);
    }
}
