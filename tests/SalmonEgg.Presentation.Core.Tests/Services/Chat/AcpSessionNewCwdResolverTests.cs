using System;
using SalmonEgg.Domain.Models;
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
            remoteDirectories: null);

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
            remoteDirectories: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSessionNewCwdResolver.MissingRemoteCwdMessage, result.ErrorMessage);
        Assert.Null(result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithConfiguredRemoteDirectory_ReturnsRemotePath()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.WebSocket };
        var directories = new[]
        {
            new AgentRemoteDirectory { DirectoryId = "dir-1", DisplayName = "Workspace", RemotePath = "/home/user/project" }
        };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: " /home/user/project ",
            profile: profile,
            remoteDirectories: directories);

        Assert.True(result.IsSuccess);
        Assert.Equal("/home/user/project", result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithUnconfiguredCwd_ReturnsRequestedPath()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.WebSocket };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: @"C:\repos\local",
            profile: profile,
            remoteDirectories: Array.Empty<AgentRemoteDirectory>());

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\repos\local", result.Cwd);
        Assert.Null(result.ErrorMessage);
    }
}
