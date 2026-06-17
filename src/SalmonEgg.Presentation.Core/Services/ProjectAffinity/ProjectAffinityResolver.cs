using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

public sealed class ProjectAffinityResolver : IProjectAffinityResolver
{
    private const string ReasonOverride = "Override";
    private const string ReasonRemoteDirectory = "RemoteDirectory";
    private const string ReasonDirectMatch = "DirectMatch";
    private const string ReasonNeedsMapping = "NeedsMapping";
    private const string ReasonUnclassified = "Unclassified";
    private const string ReasonMissingCwd = "MissingCwd";

    public ProjectAffinityResolution Resolve(ProjectAffinityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unclassifiedProjectId = GetUnclassifiedProjectId(request);
        var projects = request.Projects ?? Array.Empty<ProjectDefinition>();
        var remoteDirectories = request.RemoteDirectories ?? Array.Empty<AgentRemoteDirectory>();
        var overrideProjectId = NormalizeToken(request.OverrideProjectId);

        var overrideResolution = ResolveOverride(request.RemoteCwd, overrideProjectId, unclassifiedProjectId, projects);
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

        var directResolution = ResolveDirect(
            normalizedRemoteCwd,
            overrideProjectId,
            request.RemoteCwd,
            projects);
        if (directResolution != null)
        {
            return directResolution;
        }

        var remoteDirectoryResolution = ResolveRemoteDirectory(
            normalizedRemoteCwd,
            normalizedProfileId,
            overrideProjectId,
            request.RemoteCwd,
            unclassifiedProjectId,
            remoteDirectories);
        if (remoteDirectoryResolution != null)
        {
            return remoteDirectoryResolution;
        }

        return ResolveFallback(
            unclassifiedProjectId,
            normalizedProfileId,
            request.RemoteSessionId,
            overrideProjectId,
            request.RemoteCwd);
    }

    private static string GetUnclassifiedProjectId(ProjectAffinityRequest request)
        => string.IsNullOrWhiteSpace(request.UnclassifiedProjectId)
            ? NavigationProjectIds.Unclassified
            : request.UnclassifiedProjectId;

    private static ProjectAffinityResolution? ResolveOverride(
        string? remoteCwd,
        string? overrideProjectId,
        string unclassifiedProjectId,
        IReadOnlyList<ProjectDefinition> projects)
    {
        if (string.Equals(overrideProjectId, unclassifiedProjectId, StringComparison.Ordinal))
        {
            return CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.Override,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: remoteCwd,
                LocalResolvedPath: null,
                NeedsUserAttention: false,
                Reason: ReasonOverride);
        }

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

    private static ProjectAffinityResolution? ResolveRemoteDirectory(
        string normalizedRemoteCwd,
        string? normalizedProfileId,
        string? overrideProjectId,
        string? remoteCwd,
        string unclassifiedProjectId,
        IReadOnlyList<AgentRemoteDirectory> remoteDirectories)
    {
        // A configured remote directory can only be matched when we have both a bound profile id and a remote cwd;
        // profile-less or cwd-less requests fall through to the NeedsMapping/Unclassified fallback.
        if (string.IsNullOrWhiteSpace(normalizedProfileId)
            || string.IsNullOrWhiteSpace(normalizedRemoteCwd))
        {
            return null;
        }

        AgentRemoteDirectory? matchedDirectory = null;
        foreach (var directory in remoteDirectories)
        {
            if (directory == null
                || string.IsNullOrWhiteSpace(directory.ProfileId)
                || string.IsNullOrWhiteSpace(directory.RemotePath))
            {
                continue;
            }

            if (!string.Equals(directory.ProfileId.Trim(), normalizedProfileId, StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedDirectoryPath = NormalizePath(directory.RemotePath);
            if (string.IsNullOrWhiteSpace(normalizedDirectoryPath))
            {
                continue;
            }

            if (!PathsEqual(normalizedDirectoryPath, normalizedRemoteCwd))
            {
                continue;
            }

            matchedDirectory = directory;
            break;
        }

        if (matchedDirectory == null)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(matchedDirectory.DisplayName)
            ? matchedDirectory.RemotePath
            : matchedDirectory.DisplayName;

        return CreateResolution(
            EffectiveProjectId: unclassifiedProjectId,
            Source: ProjectAffinitySource.RemoteDirectory,
            MatchedProjectId: null,
            OverrideProjectId: overrideProjectId,
            RemoteCwd: remoteCwd,
            LocalResolvedPath: null,
            NeedsUserAttention: false,
            Reason: ReasonRemoteDirectory,
            RemoteDirectoryDisplayName: displayName);
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
        string? remoteCwd)
        => IsRemoteBound(normalizedProfileId, remoteSessionId)
            ? CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.NeedsMapping,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: remoteCwd,
                LocalResolvedPath: null,
                NeedsUserAttention: true,
                Reason: ReasonNeedsMapping)
            : CreateResolution(
                EffectiveProjectId: unclassifiedProjectId,
                Source: ProjectAffinitySource.Unclassified,
                MatchedProjectId: null,
                OverrideProjectId: overrideProjectId,
                RemoteCwd: remoteCwd,
                LocalResolvedPath: null,
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
        string Reason,
        string? RemoteDirectoryDisplayName = null)
        => new(
            EffectiveProjectId,
            Source,
            MatchedProjectId,
            OverrideProjectId,
            RemoteCwd,
            LocalResolvedPath,
            NeedsUserAttention,
            Reason,
            RemoteDirectoryDisplayName);

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

    private static bool PathsEqual(string left, string right)
    {
        var comparison = UsesCaseInsensitivePathSemantics(left) || UsesCaseInsensitivePathSemantics(right)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static bool UsesCaseInsensitivePathSemantics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ProtocolPathRules.IsAbsolutePath(path)
            && (path.StartsWith(@"\\", StringComparison.Ordinal)
                || (path.Length >= 3
                    && char.IsLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '/'));
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
