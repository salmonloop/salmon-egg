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
            profile: profile);

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
            profile: profile);

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSessionNewCwdResolver.MissingRemoteCwdMessage, result.ErrorMessage);
        Assert.Null(result.Cwd);
    }

    [Fact]
    public void Resolve_RemoteWithAbsoluteCwd_TrustsRemotePath()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.WebSocket };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: " /srv/agent/worktree ",
            profile: profile);

        Assert.True(result.IsSuccess);
        Assert.Equal("/srv/agent/worktree", result.Cwd);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Resolve_RemoteWithWindowsAbsoluteCwd_TrustsRemotePath()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.HttpSse };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: @"C:\agent\worktree",
            profile: profile);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\agent\worktree", result.Cwd);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Resolve_RemoteWithRelativeCwd_ReturnsProtocolFailure()
    {
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.WebSocket };

        var result = AcpSessionNewCwdResolver.Resolve(
            requestedCwd: "agent/worktree",
            profile: profile);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Cwd);
        Assert.Equal(AcpSessionNewCwdResolver.InvalidRemoteCwdMessage, result.ErrorMessage);
    }
}
