using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// A single project source the user chose to add. This is a thin input DTO, not a
/// state owner: local selections carry a raw picked path, remote selections carry the
/// authoritative <see cref="AgentRemoteDirectory.DirectoryId"/> only. Persisted identity
/// remains <see cref="ProjectDefinition"/> (local) or the reference-only navigation
/// membership id (remote); this type never becomes a second configuration source.
/// </summary>
public abstract record ProjectSourceSelection
{
    private ProjectSourceSelection()
    {
    }

    public sealed record LocalFolder(string Path) : ProjectSourceSelection;

    public sealed record RemoteDirectory(string DirectoryId) : ProjectSourceSelection;
}

public enum AddProjectStatus
{
    /// <summary>The source was added to the project list.</summary>
    Added,

    /// <summary>The source was already present; the existing project is the effective target.</summary>
    AlreadyExists,

    /// <summary>A remote directory id could not be resolved against the settings source of truth.</summary>
    RejectedUnknownRemote,

    /// <summary>The selection was empty or otherwise unusable.</summary>
    Invalid,
}

/// <summary>
/// Result of a unified add-project request. <see cref="ProjectId"/> is the effective
/// navigation project id for <see cref="AddProjectStatus.Added"/> and
/// <see cref="AddProjectStatus.AlreadyExists"/>; null otherwise.
/// </summary>
public sealed record AddProjectOutcome(AddProjectStatus Status, string? ProjectId)
{
    public static AddProjectOutcome Added(string projectId) => new(AddProjectStatus.Added, projectId);

    public static AddProjectOutcome AlreadyExists(string projectId) => new(AddProjectStatus.AlreadyExists, projectId);

    public static readonly AddProjectOutcome RejectedUnknownRemote = new(AddProjectStatus.RejectedUnknownRemote, null);

    public static readonly AddProjectOutcome Invalid = new(AddProjectStatus.Invalid, null);
}

/// <summary>
/// The single authoritative entry point that turns a chosen project source into a
/// navigation project. Local and remote sources both flow through here so dedup,
/// identity and persistence stay in one place; the UI never mutates the project list
/// directly. See docs plan "添加项目统一入口".
/// </summary>
public interface IAddProjectCoordinator
{
    AddProjectOutcome AddProject(ProjectSourceSelection selection);
}

public sealed class AddProjectCoordinator : IAddProjectCoordinator
{
    private readonly INavigationProjectPreferences _preferences;
    private readonly ILogger<AddProjectCoordinator> _logger;

    public AddProjectCoordinator(
        INavigationProjectPreferences preferences,
        ILogger<AddProjectCoordinator> logger)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AddProjectOutcome AddProject(ProjectSourceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return selection switch
        {
            ProjectSourceSelection.LocalFolder local => AddLocalFolder(local.Path),
            ProjectSourceSelection.RemoteDirectory remote => AddRemoteDirectory(remote.DirectoryId),
            _ => AddProjectOutcome.Invalid,
        };
    }

    private AddProjectOutcome AddLocalFolder(string? path)
    {
        var normalized = NavTimeFormatter
            .NormalizePathForPrefixMatch(path)
            .TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AddProjectOutcome.Invalid;
        }

        var existing = _preferences.Projects.FirstOrDefault(project => LocalPathsEqual(project.RootPath, normalized));
        if (existing is not null)
        {
            return AddProjectOutcome.AlreadyExists(existing.ProjectId);
        }

        var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalized;
        }

        var projectId = Guid.NewGuid().ToString("N");
        _preferences.AddProject(new ProjectDefinition
        {
            ProjectId = projectId,
            Name = name,
            RootPath = normalized,
        });

        _logger.LogInformation("Added local project. ProjectId={ProjectId}", projectId);
        return AddProjectOutcome.Added(projectId);
    }

    private AddProjectOutcome AddRemoteDirectory(string? directoryId)
    {
        var normalizedId = directoryId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return AddProjectOutcome.Invalid;
        }

        // Resolve strictly against the settings source of truth. An unknown id is rejected
        // outright — it must never fall back to being treated as a local path.
        var directory = _preferences.AgentRemoteDirectories.FirstOrDefault(candidate =>
            candidate is not null
            && string.Equals(candidate.DirectoryId, normalizedId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(candidate.RemotePath));
        if (directory is null)
        {
            _logger.LogWarning("Rejected unknown remote directory id for add-project.");
            return AddProjectOutcome.RejectedUnknownRemote;
        }

        var projectId = ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId(directory.DirectoryId);
        if (_preferences.NavigationRemoteDirectoryIds.Any(id => string.Equals(id, directory.DirectoryId, StringComparison.Ordinal)))
        {
            return AddProjectOutcome.AlreadyExists(projectId);
        }

        _preferences.AddRemoteDirectoryToNavigation(directory.DirectoryId);
        _logger.LogInformation("Added remote project to navigation. ProjectId={ProjectId}", projectId);
        return AddProjectOutcome.Added(projectId);
    }

    private static bool LocalPathsEqual(string? rootPath, string normalizedCandidate)
    {
        var normalizedRoot = NavTimeFormatter
            .NormalizePathForPrefixMatch(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }
}
