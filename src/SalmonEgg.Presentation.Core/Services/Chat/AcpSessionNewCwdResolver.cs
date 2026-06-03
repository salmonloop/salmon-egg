using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpSessionNewCwdResolution(
    bool IsSuccess,
    string? Cwd,
    string? ErrorMessage);

public static class AcpSessionNewCwdResolver
{
    public const string MissingRemoteCwdMessage =
        "Select a project or configure a remote path mapping before creating a remote session.";

    public static AcpSessionNewCwdResolution Resolve(
        string? requestedCwd,
        ServerConfiguration? profile,
        IReadOnlyList<ProjectPathMapping>? pathMappings)
    {
        var trimmedCwd = TrimOrNull(requestedCwd);
        if (profile is null || profile.Transport == TransportType.Stdio)
        {
            if (!string.IsNullOrWhiteSpace(trimmedCwd))
            {
                return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
            }

            try
            {
                var currentDirectory = Environment.CurrentDirectory?.Trim();
                return new AcpSessionNewCwdResolution(
                    !string.IsNullOrWhiteSpace(currentDirectory),
                    currentDirectory,
                    string.IsNullOrWhiteSpace(currentDirectory)
                        ? MissingRemoteCwdMessage
                        : null);
            }
            catch
            {
                return new AcpSessionNewCwdResolution(false, null, MissingRemoteCwdMessage);
            }
        }

        if (string.IsNullOrWhiteSpace(trimmedCwd))
        {
            return new AcpSessionNewCwdResolution(false, null, MissingRemoteCwdMessage);
        }

        if (TryMapLocalPathToRemote(trimmedCwd, profile.Id, pathMappings, out var remoteCwd))
        {
            return new AcpSessionNewCwdResolution(true, remoteCwd, null);
        }

        return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
    }

    private static bool TryMapLocalPathToRemote(
        string localCwd,
        string? profileId,
        IReadOnlyList<ProjectPathMapping>? pathMappings,
        out string? remoteCwd)
    {
        remoteCwd = null;
        if (string.IsNullOrWhiteSpace(localCwd)
            || string.IsNullOrWhiteSpace(profileId)
            || pathMappings is not { Count: > 0 })
        {
            return false;
        }

        var normalizedLocalCwd = NormalizePath(localCwd);
        if (string.IsNullOrWhiteSpace(normalizedLocalCwd))
        {
            return false;
        }

        ProjectPathMapping? bestMapping = null;
        var bestLength = -1;
        foreach (var mapping in pathMappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.ProfileId))
            {
                continue;
            }

            if (!string.Equals(mapping.ProfileId.Trim(), profileId.Trim(), StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedLocalRoot = NormalizePath(mapping.LocalRootPath);
            var normalizedRemoteRoot = NormalizePath(mapping.RemoteRootPath);
            if (string.IsNullOrWhiteSpace(normalizedLocalRoot)
                || string.IsNullOrWhiteSpace(normalizedRemoteRoot)
                || !IsPathPrefix(normalizedLocalRoot, normalizedLocalCwd))
            {
                continue;
            }

            if (normalizedLocalRoot.Length > bestLength)
            {
                bestMapping = mapping;
                bestLength = normalizedLocalRoot.Length;
            }
        }

        if (bestMapping is null)
        {
            return false;
        }

        var localRoot = NormalizePath(bestMapping.LocalRootPath);
        var remoteRoot = NormalizePath(bestMapping.RemoteRootPath);
        var suffix = normalizedLocalCwd.Length > localRoot.Length
            ? normalizedLocalCwd.Substring(localRoot.Length).TrimStart('/')
            : string.Empty;
        remoteCwd = string.IsNullOrEmpty(suffix)
            ? remoteRoot
            : $"{remoteRoot}/{suffix}";
        return true;
    }

    private static bool IsPathPrefix(string root, string path)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == root.Length || path[root.Length] == '/';
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/').TrimEnd('/');
}
