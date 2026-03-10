using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public partial class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly ChatViewModel _chatViewModel;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SidebarViewModel> _logger;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ProjectNavItemViewModel> _projects = new();

    [ObservableProperty]
    private ProjectNavItemViewModel? _selectedProject;

    public SidebarViewModel(ChatViewModel chatViewModel, ISessionManager sessionManager, ILogger<SidebarViewModel> logger)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SeedDefaultProjects();
        SyncSessionsFromChat();

        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
    }

    public async Task TryActivateSessionAsync(SessionNavItemViewModel session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        try
        {
            await _chatViewModel.TrySwitchToSessionAsync(session.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TryActivateSessionAsync failed");
        }
    }

    private void SeedDefaultProjects()
    {
        if (Projects.Count > 0)
        {
            return;
        }

        Projects.Add(new ProjectNavItemViewModel
        {
            ProjectId = "default",
            Name = "默认项目",
            Subtitle = "ACP Client UI",
            IsExpanded = true
        });

        SelectedProject = Projects.FirstOrDefault();
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId) ||
            e.PropertyName == nameof(ChatViewModel.IsSessionActive) ||
            e.PropertyName == nameof(ChatViewModel.CurrentSessionDisplayName))
        {
            SyncSessionsFromChat();
        }
    }

    private void SyncSessionsFromChat()
    {
        try
        {
            var sessionId = _chatViewModel.CurrentSessionId;
            var isActive = _chatViewModel.IsSessionActive && !string.IsNullOrWhiteSpace(sessionId);

            if (!isActive)
            {
                foreach (var project in Projects)
                {
                    project.SelectedSession = null;
                }
                return;
            }

            var targetProject = SelectedProject ?? Projects.FirstOrDefault();
            if (targetProject == null)
            {
                return;
            }

            var existing = targetProject.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (existing == null)
            {
                existing = new SessionNavItemViewModel
                {
                    SessionId = sessionId!,
                    Title = ResolveSessionTitle(sessionId!)
                };
                targetProject.Sessions.Insert(0, existing);
            }
            else
            {
                // If the display name changed (inline rename in chat header), keep nav label in sync.
                var desired = ResolveSessionTitle(sessionId!);
                if (!string.Equals(existing.Title, desired, StringComparison.Ordinal))
                {
                    existing.Title = desired;
                }
            }

            SelectedProject = targetProject;
            targetProject.IsExpanded = true;
            targetProject.SelectedSession = existing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SyncSessionsFromChat failed");
        }
    }

    private string ResolveSessionTitle(string sessionId)
    {
        var session = _sessionManager.GetSession(sessionId);
        var name = session?.DisplayName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return SessionNamePolicy.CreateDefault(sessionId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
    }
}

public partial class ProjectNavItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _projectId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    [NotifyPropertyChangedFor(nameof(SubtitleVisibility))]
    private string? _subtitle;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<SessionNavItemViewModel> _sessions = new();

    [ObservableProperty]
    private SessionNavItemViewModel? _selectedSession;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public Visibility SubtitleVisibility => HasSubtitle ? Visibility.Visible : Visibility.Collapsed;
}

public partial class SessionNavItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;
}
