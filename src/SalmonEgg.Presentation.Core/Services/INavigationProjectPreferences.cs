using System;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services;

public interface INavigationProjectPreferences
{
    ReadOnlyObservableCollection<ProjectDefinition> Projects { get; }

    ReadOnlyObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories { get; }

    // Reference-only navigation membership: which configured remote directories the user
    // added to the project list. Stores directory IDs only; DisplayName/RemotePath stay owned
    // by AgentRemoteDirectories so renames and path edits never desynchronize navigation.
    ReadOnlyObservableCollection<string> NavigationRemoteDirectoryIds { get; }

    string? LastSelectedProjectId { get; set; }

    void AddProject(ProjectDefinition project);

    void AddRemoteDirectoryToNavigation(string directoryId);

    string? TryGetProjectCwd(string projectId);
}

public sealed class NavigationProjectPreferencesAdapter : INavigationProjectPreferences
{
    private readonly AppPreferencesViewModel _preferences;
    private readonly ReadOnlyObservableCollection<ProjectDefinition> _projects;
    private readonly ReadOnlyObservableCollection<AgentRemoteDirectory> _agentRemoteDirectories;
    private readonly ReadOnlyObservableCollection<string> _navigationRemoteDirectoryIds;

    public NavigationProjectPreferencesAdapter(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _projects = new ReadOnlyObservableCollection<ProjectDefinition>(_preferences.Projects);
        _agentRemoteDirectories = new ReadOnlyObservableCollection<AgentRemoteDirectory>(_preferences.AgentRemoteDirectories);
        _navigationRemoteDirectoryIds = new ReadOnlyObservableCollection<string>(_preferences.NavigationRemoteDirectoryIds);
    }

    public ReadOnlyObservableCollection<ProjectDefinition> Projects => _projects;

    public ReadOnlyObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories => _agentRemoteDirectories;

    public ReadOnlyObservableCollection<string> NavigationRemoteDirectoryIds => _navigationRemoteDirectoryIds;

    public string? LastSelectedProjectId
    {
        get => _preferences.LastSelectedProjectId;
        set => _preferences.LastSelectedProjectId = value;
    }

    public void AddProject(ProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _preferences.Projects.Add(project);
    }

    public void AddRemoteDirectoryToNavigation(string directoryId)
    {
        var normalized = directoryId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!_preferences.NavigationRemoteDirectoryIds.Contains(normalized, StringComparer.Ordinal))
        {
            _preferences.NavigationRemoteDirectoryIds.Add(normalized);
        }
    }

    public string? TryGetProjectCwd(string projectId)
        => ProjectSelectionCwdResolver.ResolveCwd(projectId, _preferences.Projects, _preferences.AgentRemoteDirectories);
}
