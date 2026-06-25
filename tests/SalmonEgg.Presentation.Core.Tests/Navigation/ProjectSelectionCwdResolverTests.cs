using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class ProjectSelectionCwdResolverTests
{
    [Fact]
    public void BuildRemoteDirectoryProjectId_ReturnsSemanticRemoteDirectoryId()
    {
        var result = ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId(" dir-alpha ");

        Assert.Equal("remote-directory:dir-alpha", result);
    }

    [Fact]
    public void TryParseRemoteDirectoryId_WhenRemoteDirectoryProjectId_ReturnsDirectoryId()
    {
        var result = ProjectSelectionCwdResolver.TryParseRemoteDirectoryId("remote-directory:dir-alpha");

        Assert.Equal("dir-alpha", result);
    }

    [Fact]
    public void TryParseRemoteDirectoryId_WhenLocalProjectId_ReturnsNull()
    {
        var result = ProjectSelectionCwdResolver.TryParseRemoteDirectoryId("project-alpha");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveCwd_WhenProjectIdIsRemoteDirectory_ReturnsConfiguredRemotePath()
    {
        var result = ProjectSelectionCwdResolver.ResolveCwd(
            "remote-directory:dir-alpha",
            projects:
            [
                new ProjectDefinition
                {
                    ProjectId = "project-alpha",
                    Name = "Alpha",
                    RootPath = @"C:\Repo\Alpha"
                }
            ],
            remoteDirectories:
            [
                new AgentRemoteDirectory
                {
                    DirectoryId = "dir-alpha",
                    DisplayName = "Remote Alpha",
                    RemotePath = " /remote/alpha "
                }
            ]);

        Assert.Equal("/remote/alpha", result);
    }

    [Fact]
    public void ResolveCwd_WhenProjectIdIsLocalProject_ReturnsConfiguredRootPath()
    {
        var result = ProjectSelectionCwdResolver.ResolveCwd(
            "project-alpha",
            projects:
            [
                new ProjectDefinition
                {
                    ProjectId = "project-alpha",
                    Name = "Alpha",
                    RootPath = @" C:\Repo\Alpha "
                }
            ],
            remoteDirectories: []);

        Assert.Equal(@"C:\Repo\Alpha", result);
    }

    [Fact]
    public void ResolveCwd_WhenRemoteDirectoryIsUnknown_ReturnsNull()
    {
        var result = ProjectSelectionCwdResolver.ResolveCwd(
            "remote-directory:dir-missing",
            projects:
            [
                new ProjectDefinition
                {
                    ProjectId = "dir-missing",
                    Name = "Local Id Collision",
                    RootPath = @"C:\Repo\Wrong"
                }
            ],
            remoteDirectories: []);

        Assert.Null(result);
    }
}
