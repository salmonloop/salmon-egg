using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class StartSessionCwdResolverTests
{
    [Fact]
    public void Resolve_WhenLastSelectedProjectIsRemoteDirectory_ReturnsConfiguredRemotePath()
    {
        var result = StartSessionCwdResolver.Resolve(
            pendingProjectRootPath: null,
            lastSelectedProjectId: "remote-directory:dir-alpha",
            projects: [],
            remoteDirectories:
            [
                new AgentRemoteDirectory
                {
                    DirectoryId = "dir-alpha",
                    DisplayName = "Alpha",
                    RemotePath = " /remote/alpha "
                }
            ]);

        Assert.Equal("/remote/alpha", result);
    }

    [Fact]
    public void Resolve_WhenPendingProjectRootExists_UsesPendingRootBeforeLastRemoteDirectory()
    {
        var result = StartSessionCwdResolver.Resolve(
            pendingProjectRootPath: @" C:\Repo\Pending ",
            lastSelectedProjectId: "remote-directory:dir-alpha",
            projects: [],
            remoteDirectories:
            [
                new AgentRemoteDirectory
                {
                    DirectoryId = "dir-alpha",
                    DisplayName = "Alpha",
                    RemotePath = "/remote/alpha"
                }
            ]);

        Assert.Equal(@"C:\Repo\Pending", result);
    }

    [Fact]
    public void Resolve_WhenLastSelectedProjectIsLocalProject_ReturnsConfiguredRootPath()
    {
        var result = StartSessionCwdResolver.Resolve(
            pendingProjectRootPath: null,
            lastSelectedProjectId: "project-alpha",
            projects:
            [
                new ProjectDefinition
                {
                    ProjectId = "project-alpha",
                    Name = "Alpha",
                    RootPath = " C:\\Repo\\Alpha "
                }
            ],
            remoteDirectories: []);

        Assert.Equal(@"C:\Repo\Alpha", result);
    }
}
