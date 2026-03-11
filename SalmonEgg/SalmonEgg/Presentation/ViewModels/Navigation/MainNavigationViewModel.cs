using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class MainNavigationViewModel : ObservableObject, IDisposable
{
    public const string UnclassifiedProjectId = "__unclassified__";

    private readonly ChatViewModel _chatViewModel;
    private readonly ISessionManager _sessionManager;
    private readonly AppPreferencesViewModel _preferences;
    private readonly IUiInteractionService _ui;
    private readonly ILogger<MainNavigationViewModel> _logger;
    private readonly SynchronizationContext _syncContext;
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _projectsChangedHandler;

    private readonly Dictionary<string, SessionNavItemViewModel> _sessionIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectNavItemViewModel> _projectIndex = new(StringComparer.Ordinal);

    public ObservableCollection<MainNavItemViewModel> Items { get; } = new();

    public StartNavItemViewModel StartItem { get; }
    public SessionsHeaderNavItemViewModel SessionsHeaderItem { get; }

    private object? _selectedItem;

    public object? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public IAsyncRelayCommand AddProjectCommand { get; }

    public MainNavigationViewModel(
        ChatViewModel chatViewModel,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        IUiInteractionService ui,
        ILogger<MainNavigationViewModel> logger)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        AddProjectCommand = new AsyncRelayCommand(AddProjectAsync);

        StartItem = new StartNavItemViewModel();
        SessionsHeaderItem = new SessionsHeaderNavItemViewModel(AddProjectCommand);

        Items.Add(StartItem);
        Items.Add(SessionsHeaderItem);

        // Show a lightweight placeholder until conversations are restored.
        var placeholderProject = CreateUnclassifiedProject();
        placeholderProject.Children.Add(CreateLoadingPlaceholder());
        Items.Add(placeholderProject);

        SelectedItem = StartItem;

        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        _projectsChangedHandler = (_, _) => RebuildTree();
        _preferences.Projects.CollectionChanged += _projectsChangedHandler;
    }

    public void Dispose()
    {
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        _preferences.Projects.CollectionChanged -= _projectsChangedHandler;
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsConversationListLoading))
        {
            RebuildTree();
        }
    }

    public void SelectStart()
    {
        SelectedItem = StartItem;
    }

    public void SelectSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_sessionIndex.TryGetValue(sessionId, out var item))
        {
            SelectedItem = item;
            return;
        }

        RebuildTree();
        if (_sessionIndex.TryGetValue(sessionId, out item))
        {
            SelectedItem = item;
        }
    }

    public void ToggleProjectExpanded(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        if (_projectIndex.TryGetValue(projectId, out var project))
        {
            project.IsExpanded = !project.IsExpanded;
        }
    }

    public async Task ShowAllSessionsForProjectAsync(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        try
        {
            var all = BuildSessionItemsForProject(projectId, limit: null);
            await _ui.ShowSessionsListDialogAsync(
                title: "会话",
                sessions: all,
                onPickSession: id =>
                {
                    SelectSession(id);
                }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Show sessions list failed");
        }
    }

    private SessionNavItemViewModel CreateLoadingPlaceholder()
    {
        return new SessionNavItemViewModel(
            sessionId: "__loading__",
            projectId: UnclassifiedProjectId,
            title: "加载中…",
            relativeTimeText: string.Empty,
            ui: _ui,
            chatViewModel: _chatViewModel,
            isPlaceholder: true);
    }

    private async Task AddProjectAsync()
    {
        var pickedPath = await _ui.PickFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(pickedPath).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (_preferences.Projects.Any(p => string.Equals(NavTimeFormatter.NormalizePathForPrefixMatch(p.RootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.ShowInfoAsync("该项目路径已存在。").ConfigureAwait(true);
            return;
        }

        var name = System.IO.Path.GetFileName(normalized.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalized;
        }

        _preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            Name = name,
            RootPath = normalized
        });

        RebuildTree();
    }

    public void RebuildTree()
    {
        _syncContext.Post(_ =>
        {
            try
            {
                _sessionIndex.Clear();
                _projectIndex.Clear();

                // Keep first 2 items (Start + Header) stable to preserve SelectedItem references.
                while (Items.Count > 2)
                {
                    Items.RemoveAt(2);
                }

                foreach (var project in BuildProjects())
                {
                    Items.Add(project);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rebuild navigation tree");
            }
        }, null);
    }

    private IEnumerable<ProjectNavItemViewModel> BuildProjects()
    {
        var projects = new List<(ProjectDefinition Project, bool IsSystem)>
        {
            (new ProjectDefinition { ProjectId = UnclassifiedProjectId, Name = "未归类", RootPath = string.Empty }, true)
        };

        projects.AddRange(_preferences.Projects
            .Where(p => p != null
                        && !string.IsNullOrWhiteSpace(p.ProjectId)
                        && !string.IsNullOrWhiteSpace(p.Name)
                        && !string.IsNullOrWhiteSpace(p.RootPath))
            .Select(p => (p, false)));

        // Normalize roots once for classification.
        var normalizedRoots = projects.ToDictionary(
            p => p.Project.ProjectId,
            p => NavTimeFormatter.NormalizePathForPrefixMatch(p.Project.RootPath),
            StringComparer.Ordinal);

        var sessions = _chatViewModel.GetKnownConversationIds()
            .Select(id => _sessionManager.GetSession(id))
            .Where(s => s != null)
            .Cast<Session>()
            .ToList();

        var byProject = sessions
            .GroupBy(s => ClassifyProjectId(s, normalizedRoots), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).ToList(), StringComparer.Ordinal);

        foreach (var (project, isSystem) in projects)
        {
            var projectVm = new ProjectNavItemViewModel(project, isSystem)
            {
                IsExpanded = true
            };

            _projectIndex[projectVm.ProjectId] = projectVm;

            var list = byProject.TryGetValue(project.ProjectId, out var value) ? value : new List<Session>();
            var top = list.Take(20).ToList();
            var remaining = Math.Max(0, list.Count - top.Count);

            foreach (var session in top)
            {
                var title = string.IsNullOrWhiteSpace(session.DisplayName)
                    ? SessionNamePolicy.CreateDefault(session.SessionId)
                    : session.DisplayName.Trim();

                var relative = NavTimeFormatter.ToRelativeText(session.LastActivityAt == default ? session.CreatedAt : session.LastActivityAt);
                var vm = new SessionNavItemViewModel(
                    sessionId: session.SessionId,
                    projectId: projectVm.ProjectId,
                    title: title,
                    relativeTimeText: relative,
                    ui: _ui,
                    chatViewModel: _chatViewModel);

                _sessionIndex[session.SessionId] = vm;
                projectVm.Children.Add(vm);
            }

            if (remaining > 0)
            {
                var showMore = new AsyncRelayCommand(() => ShowAllSessionsForProjectAsync(projectVm.ProjectId));
                projectVm.Children.Add(new MoreSessionsNavItemViewModel(projectVm.ProjectId, remaining, showMore));
            }

            // If still loading and there are no sessions yet, show a placeholder so the pane doesn't look empty.
            if (_chatViewModel.IsConversationListLoading && projectVm.Children.Count == 0 && projectVm.ProjectId == UnclassifiedProjectId)
            {
                projectVm.Children.Add(CreateLoadingPlaceholder());
            }

            yield return projectVm;
        }
    }

    private ProjectNavItemViewModel CreateUnclassifiedProject()
    {
        var project = new ProjectDefinition
        {
            ProjectId = UnclassifiedProjectId,
            Name = "未归类",
            RootPath = string.Empty
        };
        var vm = new ProjectNavItemViewModel(project, isSystemProject: true) { IsExpanded = true };
        _projectIndex[vm.ProjectId] = vm;
        return vm;
    }

    private string ClassifyProjectId(Session session, IReadOnlyDictionary<string, string> normalizedRoots)
    {
        var cwdNorm = NavTimeFormatter.NormalizePathForPrefixMatch(session.Cwd);
        if (string.IsNullOrWhiteSpace(cwdNorm))
        {
            return UnclassifiedProjectId;
        }

        string? bestId = null;
        var bestLen = -1;

        foreach (var kvp in normalizedRoots)
        {
            var rootNorm = kvp.Value;
            if (string.IsNullOrWhiteSpace(rootNorm))
            {
                continue;
            }

            if (cwdNorm.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase) && rootNorm.Length > bestLen)
            {
                bestId = kvp.Key;
                bestLen = rootNorm.Length;
            }
        }

        return bestId ?? UnclassifiedProjectId;
    }

    private IReadOnlyList<SessionNavItemViewModel> BuildSessionItemsForProject(string projectId, int? limit)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Array.Empty<SessionNavItemViewModel>();
        }

        var projects = new List<ProjectDefinition>
        {
            new() { ProjectId = UnclassifiedProjectId, Name = "未归类", RootPath = string.Empty }
        };
        projects.AddRange(_preferences.Projects);

        var normalizedRoots = projects
            .Where(p => !string.IsNullOrWhiteSpace(p.ProjectId))
            .ToDictionary(p => p.ProjectId, p => NavTimeFormatter.NormalizePathForPrefixMatch(p.RootPath), StringComparer.Ordinal);

        var sessions = _chatViewModel.GetKnownConversationIds()
            .Select(id => _sessionManager.GetSession(id))
            .Where(s => s != null)
            .Cast<Session>()
            .Where(s => string.Equals(ClassifyProjectId(s, normalizedRoots), projectId, StringComparison.Ordinal))
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();

        if (limit.HasValue)
        {
            sessions = sessions.Take(limit.Value).ToList();
        }

        return sessions.Select(s =>
        {
            var title = string.IsNullOrWhiteSpace(s.DisplayName) ? SessionNamePolicy.CreateDefault(s.SessionId) : s.DisplayName.Trim();
            var relative = NavTimeFormatter.ToRelativeText(s.LastActivityAt == default ? s.CreatedAt : s.LastActivityAt);
            return new SessionNavItemViewModel(
                sessionId: s.SessionId,
                projectId: projectId,
                title: title,
                relativeTimeText: relative,
                ui: _ui,
                chatViewModel: _chatViewModel);
        }).ToList();
    }
}
