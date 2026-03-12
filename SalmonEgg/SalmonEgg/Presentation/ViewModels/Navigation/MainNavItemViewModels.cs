using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public abstract partial class MainNavItemViewModel : ObservableObject
{
    public ObservableCollection<MainNavItemViewModel> Children { get; } = new();
}

public sealed partial class StartNavItemViewModel : MainNavItemViewModel
{
    public string Title { get; } = "开始";
}

public sealed partial class SessionsHeaderNavItemViewModel : MainNavItemViewModel
{
    public string Title { get; } = "会话";

    public IAsyncRelayCommand AddProjectCommand { get; }

    private bool _isPaneOpen = true;

    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set
        {
            if (SetProperty(ref _isPaneOpen, value))
            {
                OnPropertyChanged(nameof(IsPaneClosed));
            }
        }
    }

    public bool IsPaneClosed => !IsPaneOpen;

    public SessionsHeaderNavItemViewModel(IAsyncRelayCommand addProjectCommand)
    {
        AddProjectCommand = addProjectCommand;
    }
}

public sealed partial class ProjectNavItemViewModel : MainNavItemViewModel
{
    public string ProjectId { get; }
    public string Title { get; }
    public string RootPath { get; }
    public bool IsSystemProject { get; }

    public IAsyncRelayCommand CreateSessionCommand { get; }

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ProjectNavItemViewModel(ProjectDefinition project, bool isSystemProject, Func<string, Task> createSessionAsync)
    {
        ProjectId = project.ProjectId;
        Title = project.Name;
        RootPath = project.RootPath;
        IsSystemProject = isSystemProject;
        CreateSessionCommand = new AsyncRelayCommand(() => createSessionAsync(ProjectId));
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

    public SessionNavItemViewModel(
        string sessionId,
        string projectId,
        string title,
        string relativeTimeText,
        IUiInteractionService ui,
        ChatViewModel chatViewModel,
        bool isPlaceholder = false)
    {
        SessionId = sessionId;
        ProjectId = projectId;
        Title = title;
        RelativeTimeText = relativeTimeText;
        _ui = ui;
        _chatViewModel = chatViewModel;
        IsPlaceholder = isPlaceholder;

        RenameCommand = new AsyncRelayCommand(RenameAsync, CanRename);
    }

    private bool CanRename()
        => !IsPlaceholder && !string.IsNullOrWhiteSpace(SessionId);

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
    public string Title { get; }

    public IAsyncRelayCommand ShowMoreCommand { get; }

    public MoreSessionsNavItemViewModel(string projectId, int remainingCount, IAsyncRelayCommand showMoreCommand)
    {
        ProjectId = projectId;
        Title = remainingCount > 0 ? $"展开显示（+{remainingCount}）" : "展开显示";
        ShowMoreCommand = showMoreCommand;
    }
}
