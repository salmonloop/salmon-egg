using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

public sealed class ProjectAffinityResolver : IProjectAffinityResolver
{
    private const string ReasonOverride = "Override";
    private const string ReasonPathMapping = "PathMapping";
    private const string ReasonDirectMatch = "DirectMatch";
    private const string ReasonNeedsMapping = "NeedsMapping";
    private const string ReasonUnclassified = "Unclassified";
    private const string ReasonMissingCwd = "MissingCwd";

    public ProjectAffinityResolution Resolve(ProjectAffinityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unclassifiedProjectId = GetUnclassifiedProjectId(request);
        var projects = request.Projects ?? Array.Empty<ProjectDefinition>();
        var mappings = request.PathMappings ?? Array.Empty<ProjectPathMapping>();
        var overrideProjectId = NormalizeToken(request.OverrideProjectId);

        var overrideResolution = ResolveOverride(request.RemoteCwd, overrideProjectId, projects);
        if (overrideResolution != null)
        {
            return overrideResolution;
        }

        var normalizedRemoteCwd = NormalizePath(request.RemoteCwd);
        if (string.IsNullOrWhiteSpace(normalizedRemoteCwd))
        {
            return CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.Unclassified,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: request.RemoteCwd,
                LocalResolvedPath: null,
                NeedsUserAttention: false,
                Reason: ReasonMissingCwd);
        }

        var normalizedProfileId = NormalizeToken(request.BoundProfileId);

        var mappingResolution = ResolveMapping(
            normalizedRemoteCwd,
            normalizedProfileId,
            overrideProjectId,
            request.RemoteCwd,
            projects,
            mappings,
            out var mappedPath);
        if (mappingResolution != null)
        {
            return mappingResolution;
        }

        var directResolution = ResolveDirect(
            normalizedRemoteCwd,
            overrideProjectId,
            request.RemoteCwd,
            projects);
        if (directResolution != null)
        {
            return directResolution;
        }

        return ResolveFallback(
            unclassifiedProjectId,
            normalizedProfileId,
            request.RemoteSessionId,
            overrideProjectId,
            request.RemoteCwd,
            mappedPath);
    }

    private static string GetUnclassifiedProjectId(ProjectAffinityRequest request)
        => string.IsNullOrWhiteSpace(request.UnclassifiedProjectId)
            ? NavigationProjectIds.Unclassified
            : request.UnclassifiedProjectId;

    private static ProjectAffinityResolution? ResolveOverride(
        string? remoteCwd,
        string? overrideProjectId,
        IReadOnlyList<ProjectDefinition> projects)
    {
        if (!TryResolveOverride(overrideProjectId, projects, out var overrideProject))
        {
            return null;
        }

        return CreateResolution(
            EffectiveProjectId: overrideProject!.ProjectId,
            Source: ProjectAffinitySource.Override,
            MatchedProjectId: overrideProject.ProjectId,
            OverrideProjectId: overrideProjectId,
            RemoteCwd: remoteCwd,
            LocalResolvedPath: NormalizePath(overrideProject.RootPath),
            NeedsUserAttention: false,
            Reason: ReasonOverride);
    }

    private static ProjectAffinityResolution? ResolveMapping(
        string normalizedRemoteCwd,
        string? normalizedProfileId,
        string? overrideProjectId,
        string? remoteCwd,
        IReadOnlyList<ProjectDefinition> projects,
        IReadOnlyList<ProjectPathMapping> mappings,
        out string? mappedPath)
    {
        mappedPath = null;
        if (!TryResolveMappedPath(normalizedRemoteCwd, normalizedProfileId, mappings, out mappedPath))
        {
            return null;
        }

        var mappedProjectId = TryMatchProjectId(mappedPath, projects);
        if (string.IsNullOrWhiteSpace(mappedProjectId))
        {
            return null;
        }

        return CreateResolution(
            EffectiveProjectId: mappedProjectId!,
            Source: ProjectAffinitySource.PathMapping,
            MatchedProjectId: mappedProjectId,
            OverrideProjectId: overrideProjectId,
            RemoteCwd: remoteCwd,
            LocalResolvedPath: mappedPath,
            NeedsUserAttention: false,
            Reason: ReasonPathMapping);
    }

    private static ProjectAffinityResolution? ResolveDirect(
        string normalizedRemoteCwd,
        string? overrideProjectId,
        string? remoteCwd,
        IReadOnlyList<ProjectDefinition> projects)
    {
        var directProjectId = TryMatchProjectId(normalizedRemoteCwd, projects);
        if (string.IsNullOrWhiteSpace(directProjectId))
        {
            return null;
        }

        return CreateResolution(
            EffectiveProjectId: directProjectId!,
            Source: ProjectAffinitySource.DirectMatch,
            MatchedProjectId: directProjectId,
            OverrideProjectId: overrideProjectId,
            RemoteCwd: remoteCwd,
            LocalResolvedPath: normalizedRemoteCwd,
            NeedsUserAttention: false,
            Reason: ReasonDirectMatch);
    }

    private static ProjectAffinityResolution ResolveFallback(
        string unclassifiedProjectId,
        string? normalizedProfileId,
        string? remoteSessionId,
        string? overrideProjectId,
        string? remoteCwd,
        string? mappedPath)
        => IsRemoteBound(normalizedProfileId, remoteSessionId)
            ? CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.NeedsMapping,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: remoteCwd,
                LocalResolvedPath: mappedPath,
                NeedsUserAttention: true,
                Reason: ReasonNeedsMapping)
            : CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.Unclassified,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: remoteCwd,
                LocalResolvedPath: mappedPath,
                NeedsUserAttention: false,
                Reason: ReasonUnclassified);

    private static ProjectAffinityResolution CreateResolution(
        string EffectiveProjectId,
        ProjectAffinitySource Source,
        string? MatchedProjectId,
        string? OverrideProjectId,
        string? RemoteCwd,
        string? LocalResolvedPath,
        bool NeedsUserAttention,
        string Reason)
        => new(
            EffectiveProjectId,
            Source,
            MatchedProjectId,
            OverrideProjectId,
            RemoteCwd,
            LocalResolvedPath,
            NeedsUserAttention,
            Reason);

    private static bool TryResolveOverride(
        string? overrideProjectId,
        IReadOnlyList<ProjectDefinition> projects,
        out ProjectDefinition? project)
    {
        project = null;
        if (string.IsNullOrWhiteSpace(overrideProjectId))
        {
            return false;
        }

        foreach (var candidate in projects)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.ProjectId))
            {
                continue;
            }

            if (string.Equals(candidate.ProjectId, overrideProjectId, StringComparison.Ordinal))
            {
                project = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveMappedPath(
        string normalizedRemoteCwd,
        string? profileId,
        IReadOnlyList<ProjectPathMapping> mappings,
        out string? mappedPath)
    {
        mappedPath = null;
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(normalizedRemoteCwd))
        {
            return false;
        }

        ProjectPathMapping? bestMapping = null;
        var bestLength = -1;
        foreach (var mapping in mappings)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.ProfileId))
            {
                continue;
            }

            if (!string.Equals(mapping.ProfileId.Trim(), profileId, StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedRemoteRoot = NormalizePath(mapping.RemoteRootPath);
            if (string.IsNullOrWhiteSpace(normalizedRemoteRoot))
            {
                continue;
            }

            if (!IsPathPrefix(normalizedRemoteRoot, normalizedRemoteCwd))
            {
                continue;
            }

            if (normalizedRemoteRoot.Length > bestLength)
            {
                bestMapping = mapping;
                bestLength = normalizedRemoteRoot.Length;
            }
        }

        if (bestMapping == null)
        {
            return false;
        }

        var remoteRoot = NormalizePath(bestMapping.RemoteRootPath);
        var localRoot = NormalizePath(bestMapping.LocalRootPath);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            return false;
        }

        var suffix = string.Empty;
        if (normalizedRemoteCwd.Length > remoteRoot.Length)
        {
            suffix = normalizedRemoteCwd.Substring(remoteRoot.Length).TrimStart('/');
        }

        mappedPath = string.IsNullOrEmpty(suffix)
            ? localRoot
            : $"{localRoot}/{suffix}";
        return true;
    }

    private static string? TryMatchProjectId(string? normalizedPath, IReadOnlyList<ProjectDefinition> projects)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        string? bestId = null;
        var bestLength = -1;

        foreach (var project in projects)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.ProjectId))
            {
                continue;
            }

            var normalizedRoot = NormalizePath(project.RootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                continue;
            }

            if (!IsPathPrefix(normalizedRoot, normalizedPath))
            {
                continue;
            }

            if (normalizedRoot.Length > bestLength)
            {
                bestId = project.ProjectId;
                bestLength = normalizedRoot.Length;
            }
        }

        return bestId;
    }

    private static bool IsRemoteBound(string? profileId, string? remoteSessionId)
        => !string.IsNullOrWhiteSpace(profileId)
            || !string.IsNullOrWhiteSpace(remoteSessionId);

    private static bool IsPathPrefix(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Length == root.Length)
        {
            return true;
        }

        return path[root.Length] == '/';
    }

    private static string NormalizePath(string? path)
    {
        var trimmed = NormalizeToken(path);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var normalized = trimmed.Replace('\\', '/');
        normalized = normalized.TrimEnd('/');
        return normalized;
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
