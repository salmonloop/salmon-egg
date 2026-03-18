using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class StartNavItemViewModel : MainNavItemViewModel
{
    public string Title { get; } = "开始";

    public StartNavItemViewModel(INavigationPaneState navigationState) 
        : base(navigationState)
    {
    }
}

public sealed partial class ProjectNavItemViewModel : MainNavItemViewModel
{
    public string ProjectId { get; }
    private bool _isActiveDescendant;
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public string RootPath { get; }
    public bool IsSystemProject { get; }

    public IAsyncRelayCommand CreateSessionCommand { get; }

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsActiveDescendant
    {
        get => _isActiveDescendant;
        set
        {
            if (SetProperty(ref _isActiveDescendant, value))
            {
                OnPropertyChanged(nameof(HasActiveDescendantIndicator));
            }
        }
    }

    public bool HasActiveDescendantIndicator => IsActiveDescendant && IsPaneClosed;

    public ProjectNavItemViewModel(
        ProjectDefinition project, 
        bool isSystemProject, 
        Func<string, Task> createSessionAsync,
        INavigationPaneState navigationState)
        : base(navigationState)
    {
        ProjectId = project.ProjectId;
        _title = project.Name;
        RootPath = project.RootPath;
        IsSystemProject = isSystemProject;
        CreateSessionCommand = new AsyncRelayCommand(() => createSessionAsync(ProjectId));
    }

    protected override void OnPaneStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveDescendantIndicator));
    }
}

public sealed partial class SessionNavItemViewModel : MainNavItemViewModel
{
    private readonly IUiInteractionService _ui;
    private readonly ChatViewModel _chatViewModel;

    public string SessionId { get; }
    public string ProjectId { get; }

    private string _title = string.Empty;
    private string _relativeTimeText = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string RelativeTimeText
    {
        get => _relativeTimeText;
        set => SetProperty(ref _relativeTimeText, value);
    }

    public bool IsPlaceholder { get; }

    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand ArchiveCommand { get; }

    public SessionNavItemViewModel(
        string sessionId,
        string projectId,
        string title,
        string relativeTimeText,
        IUiInteractionService ui,
        ChatViewModel chatViewModel,
        INavigationPaneState navigationState,
        bool isPlaceholder = false)
        : base(navigationState)
    {
        SessionId = sessionId;
        ProjectId = projectId;
        Title = title;
        RelativeTimeText = relativeTimeText;
        _ui = ui;
        _chatViewModel = chatViewModel;
        IsPlaceholder = isPlaceholder;

        RenameCommand = new AsyncRelayCommand(RenameAsync, CanRename);
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, CanArchive);
    }

    private bool CanRename()
        => !IsPlaceholder && !string.IsNullOrWhiteSpace(SessionId);

    private bool CanArchive()
        => !IsPlaceholder && !string.IsNullOrWhiteSpace(SessionId);

    private async Task ArchiveAsync()
    {
        var confirmed = await _ui.ConfirmAsync(
            title: "归档会话",
            message: $"确定要归档会话 \"{Title}\" 吗？",
            primaryButtonText: "归档",
            closeButtonText: "取消").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        _chatViewModel.ArchiveConversation(SessionId);
    }

    private async Task RenameAsync()
    {
        var original = Title;
        var result = await _ui.PromptTextAsync(
            title: "重命名会话",
            primaryButtonText: "确定",
            closeButtonText: "取消",
            initialText: original).ConfigureAwait(true);

        if (result == null)
        {
            return;
        }

        var sanitized = SessionNamePolicy.Sanitize(result);
        var finalName = string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(SessionId)
            : sanitized;

        _chatViewModel.RenameConversation(SessionId, finalName);
        Title = finalName;
    }
}

public sealed partial class MoreSessionsNavItemViewModel : MainNavItemViewModel
{
    public string ProjectId { get; }
    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Title => Count > 0 ? $"展开显示（+{Count}）" : "展开显示";

    public IAsyncRelayCommand ShowMoreCommand { get; }

    public MoreSessionsNavItemViewModel(string projectId, int remainingCount, IAsyncRelayCommand showMoreCommand, INavigationPaneState navigationState)
        : base(navigationState)
    {
        ProjectId = projectId;
        _count = remainingCount;
        ShowMoreCommand = showMoreCommand;
    }
}
