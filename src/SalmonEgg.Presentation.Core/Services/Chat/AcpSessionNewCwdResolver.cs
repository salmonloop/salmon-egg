using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpSessionNewCwdResolution(
    bool IsSuccess,
    string? Cwd,
    string? ErrorMessage);

public static class AcpSessionNewCwdResolver
{
    public const string MissingRemoteCwdMessage =
        "Select a configured remote directory before creating a remote session.";

    public static AcpSessionNewCwdResolution Resolve(
        string? requestedCwd,
        ServerConfiguration? profile,
        IReadOnlyList<AgentRemoteDirectory>? remoteDirectories)
    {
        var trimmedCwd = TrimOrNull(requestedCwd);
        if (profile?.Transport == TransportType.Stdio)
        {
            if (!string.IsNullOrWhiteSpace(trimmedCwd))
            {
                return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
            }

            return new AcpSessionNewCwdResolution(true, GetDefaultStdioUserProfileDirectory(), null);
        }

        if (string.IsNullOrWhiteSpace(trimmedCwd)
            || string.IsNullOrWhiteSpace(profile?.Id)
            || !IsConfiguredRemoteDirectory(trimmedCwd, profile.Id, remoteDirectories))
        {
            return new AcpSessionNewCwdResolution(false, null, MissingRemoteCwdMessage);
        }

        return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
    }

    private static bool IsConfiguredRemoteDirectory(
        string requestedCwd,
        string profileId,
        IReadOnlyList<AgentRemoteDirectory>? remoteDirectories)
    {
        if (remoteDirectories is not { Count: > 0 })
        {
            return false;
        }

        foreach (var directory in remoteDirectories)
        {
            if (directory is null)
            {
                continue;
            }

            if (string.Equals(directory.ProfileId?.Trim(), profileId.Trim(), StringComparison.Ordinal)
                && string.Equals(directory.RemotePath?.Trim(), requestedCwd, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetDefaultStdioUserProfileDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
