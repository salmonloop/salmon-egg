using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.ProjectAffinity;

public sealed class ProjectAffinityResolverTests
{
    [Fact]
    public void Resolve_DirectMatch_ReturnsProjectId()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "project-1",
                RootPath = @"C:\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Repo\Feature",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal("project-1", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.DirectMatch, result.Source);
        Assert.False(result.NeedsUserAttention);
    }

    [Fact]
    public void Resolve_LongestPrefixMatch_ReturnsNestedProject()
    {
        var resolver = new ProjectAffinityResolver();
        var baseRoot = @"C:\Repo";
        var nestedRoot = @"C:\Repo\Sub";
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "base",
                RootPath = baseRoot
            },
            new ProjectDefinition
            {
                ProjectId = "nested",
                RootPath = nestedRoot
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Repo\Sub\Feature",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal("nested", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.DirectMatch, result.Source);
    }

    [Fact]
    public void Resolve_CwdMissing_ReturnsUnclassified()
    {
        var resolver = new ProjectAffinityResolver();
        var request = CreateRequest(remoteCwd: null);

        var result = resolver.Resolve(request);

        Assert.Equal(NavigationProjectIds.Unclassified, result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, result.Source);
        Assert.False(result.NeedsUserAttention);
    }

    [Fact]
    public void Resolve_PathMappingMatch_ReturnsMappedProject()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "mapped",
                RootPath = @"C:\Local\Repo"
            }
        };
        var mappings = new[]
        {
            new ProjectPathMapping
            {
                ProfileId = "profile-1",
                RemoteRootPath = "/remote/repo",
                LocalRootPath = @"C:\Local\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: "/remote/repo/sub",
            boundProfileId: "profile-1",
            projects: projects,
            pathMappings: mappings);

        var result = resolver.Resolve(request);

        Assert.Equal("mapped", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.PathMapping, result.Source);
        Assert.Equal("mapped", result.MatchedProjectId);
        Assert.False(result.NeedsUserAttention);
    }

    [Fact]
    public void Resolve_RemoteBoundNoMatch_ReturnsNeedsMapping()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "local",
                RootPath = @"C:\Local\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: "/remote/repo",
            boundProfileId: "profile-1",
            remoteSessionId: "remote-1",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal(NavigationProjectIds.Unclassified, result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.NeedsMapping, result.Source);
        Assert.True(result.NeedsUserAttention);
    }

    [Fact]
    public void Resolve_NoMatchNotRemoteBound_ReturnsUnclassified()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "local",
                RootPath = @"C:\Local\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: "/remote/repo",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal(NavigationProjectIds.Unclassified, result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, result.Source);
        Assert.False(result.NeedsUserAttention);
    }

    [Fact]
    public void Resolve_OverrideWinsOverMappingAndDirectMatch()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "override",
                RootPath = @"C:\Override"
            },
            new ProjectDefinition
            {
                ProjectId = "direct",
                RootPath = @"C:\Local\Repo"
            }
        };
        var mappings = new[]
        {
            new ProjectPathMapping
            {
                ProfileId = "profile-1",
                RemoteRootPath = "/remote/repo",
                LocalRootPath = @"C:\Local\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: "/remote/repo/sub",
            boundProfileId: "profile-1",
            overrideProjectId: "override",
            projects: projects,
            pathMappings: mappings);

        var result = resolver.Resolve(request);

        Assert.Equal("override", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Override, result.Source);
    }

    [Fact]
    public void Resolve_DeletedOverride_FallsBackToDirectMatch()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "direct",
                RootPath = @"C:\Local\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Local\Repo\Sub",
            overrideProjectId: "missing",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal("direct", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.DirectMatch, result.Source);
    }

    [Fact]
    public void Resolve_SlashNormalization_AllowsMatch()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "project-1",
                RootPath = "C:/Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Repo\Sub",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal("project-1", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.DirectMatch, result.Source);
    }

    [Fact]
    public void Resolve_TrailingSeparatorNormalization_AllowsMatch()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "project-1",
                RootPath = @"C:\Repo\"
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Repo\Sub\",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal("project-1", result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.DirectMatch, result.Source);
    }

    [Fact]
    public void Resolve_PathBoundaryMismatch_DoesNotMatch()
    {
        var resolver = new ProjectAffinityResolver();
        var projects = new[]
        {
            new ProjectDefinition
            {
                ProjectId = "project-1",
                RootPath = @"C:\Repo"
            }
        };
        var request = CreateRequest(
            remoteCwd: @"C:\Repo2\Sub",
            projects: projects);

        var result = resolver.Resolve(request);

        Assert.Equal(NavigationProjectIds.Unclassified, result.EffectiveProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, result.Source);
    }

    private static ProjectAffinityRequest CreateRequest(
        string? remoteCwd,
        string? boundProfileId = null,
        string? remoteSessionId = null,
        string? overrideProjectId = null,
        IReadOnlyList<ProjectDefinition>? projects = null,
        IReadOnlyList<ProjectPathMapping>? pathMappings = null)
        => new(
            RemoteCwd: remoteCwd,
            BoundProfileId: boundProfileId,
            RemoteSessionId: remoteSessionId,
            OverrideProjectId: overrideProjectId,
            Projects: projects ?? Array.Empty<ProjectDefinition>(),
            PathMappings: pathMappings ?? Array.Empty<ProjectPathMapping>(),
            UnclassifiedProjectId: NavigationProjectIds.Unclassified);
}
