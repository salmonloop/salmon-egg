using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Registers an imported remote session's working directory as a remote-directory project so the
/// conversation survives restart: project affinity, the navigation project list and recovery all
/// resolve against <see cref="AgentRemoteDirectories"/>, and an unregistered cwd falls out of the
/// project tree entirely once the runtime session is gone.
/// </summary>
public interface IRemoteDirectoryRegistrar
{
    /// <summary>
    /// Ensures <paramref name="remoteCwd"/> is backed by a configured remote directory and that the
    /// directory is a navigation project member. Existing entries win: a path that already has a
    /// directory keeps its id, display name and membership state. Returns the id of the directory
    /// backing <paramref name="remoteCwd"/>, or null when the path could not be registered.
    /// </summary>
    string? EnsureRegistered(string remoteCwd);
}

/// <inheritdoc />
public sealed class RemoteDirectoryRegistrar : IRemoteDirectoryRegistrar
{
    private readonly AppPreferencesViewModel _preferences;

    public RemoteDirectoryRegistrar(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public string? EnsureRegistered(string remoteCwd)
    {
        var normalizedCwd = RemotePathEquivalence.Normalize(remoteCwd);
        if (string.IsNullOrWhiteSpace(normalizedCwd) || !ProtocolPathRules.IsAbsolutePath(normalizedCwd))
        {
            return null;
        }

        var directories = _preferences.AgentRemoteDirectories;
        AgentRemoteDirectory? existing = null;
        for (var i = 0; i < directories.Count; i++)
        {
            if (PathsEqual(directories[i]?.RemotePath, normalizedCwd))
            {
                existing = directories[i];
                break;
            }
        }

        string directoryId;
        if (existing is null)
        {
            directoryId = Guid.NewGuid().ToString("N");
            _preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = directoryId,
                DisplayName = string.Empty,
                RemotePath = normalizedCwd
            });
        }
        else
        {
            directoryId = existing.DirectoryId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directoryId))
            {
                return null;
            }
        }

        if (!_preferences.NavigationRemoteDirectoryIds.Contains(directoryId, StringComparer.Ordinal))
        {
            _preferences.NavigationRemoteDirectoryIds.Add(directoryId);
        }

        return directoryId;
    }

    private static bool PathsEqual(string? left, string? right) => RemotePathEquivalence.Equals(left, right);
}
