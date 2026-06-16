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

    string? LastSelectedProjectId { get; set; }

    void AddProject(ProjectDefinition project);

    string? TryGetProjectRootPath(string projectId);
}

public sealed class NavigationProjectPreferencesAdapter : INavigationProjectPreferences
{
    private readonly AppPreferencesViewModel _preferences;
    private readonly ReadOnlyObservableCollection<ProjectDefinition> _projects;
    private readonly ReadOnlyObservableCollection<AgentRemoteDirectory> _agentRemoteDirectories;

    public NavigationProjectPreferencesAdapter(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _projects = new ReadOnlyObservableCollection<ProjectDefinition>(_preferences.Projects);
        _agentRemoteDirectories = new ReadOnlyObservableCollection<AgentRemoteDirectory>(_preferences.AgentRemoteDirectories);
    }

    public ReadOnlyObservableCollection<ProjectDefinition> Projects => _projects;

    public ReadOnlyObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories => _agentRemoteDirectories;

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

    public string? TryGetProjectRootPath(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var project = _preferences.Projects.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.Ordinal));
        if (project == null || string.IsNullOrWhiteSpace(project.RootPath))
        {
            return null;
        }

        return project.RootPath.Trim();
    }
}
