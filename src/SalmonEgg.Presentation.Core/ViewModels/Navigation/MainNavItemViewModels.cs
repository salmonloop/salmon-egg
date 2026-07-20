using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed partial class StartNavItemViewModel : MainNavItemViewModel
{
    private string _title = "Start";

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public StartNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher, string title = "Start")
        : base(navigationState, uiDispatcher)
    {
        Title = title;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }
}

public sealed partial class DiscoverSessionsNavItemViewModel : MainNavItemViewModel
{
    private string _title = "Discover sessions";

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public DiscoverSessionsNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher, string title = "Discover sessions")
        : base(navigationState, uiDispatcher)
    {
        Title = title;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }
}

public sealed partial class SettingsNavItemViewModel : MainNavItemViewModel
{
    private string _title = "Settings";

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public SettingsNavItemViewModel(string title, INavigationPaneState navigationState, IUiDispatcher uiDispatcher)
        : base(navigationState, uiDispatcher)
    {
        Title = title;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
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
        set => SetProperty(ref _isActiveDescendant, value);
    }

    public ProjectNavItemViewModel(
        ProjectDefinition project,
        bool isSystemProject,
        Func<string, Task> createSessionAsync,
        INavigationPaneState navigationState,
        IUiDispatcher uiDispatcher)
        : base(navigationState, uiDispatcher)
    {
        ProjectId = project.ProjectId;
        _title = project.Name;
        RootPath = project.RootPath;
        IsSystemProject = isSystemProject;
        CreateSessionCommand = new AsyncRelayCommand(() => createSessionAsync(ProjectId));
    }
}

public sealed partial class SessionNavItemViewModel : MainNavItemViewModel
{
    private readonly IStringLocalizer<CoreStrings> _localizer;

    private readonly IUiInteractionService _ui;
    private readonly IPlatformShellService _shell;
    private readonly IChatSessionCatalog _chatSessionCatalog;

    public string SessionId { get; }
    public string ProjectId { get; }

    private string _title = string.Empty;
    private string _relativeTimeText = string.Empty;
    private string? _remoteSessionId;
    private bool _hasUnreadAttention;

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

    public string? RemoteSessionId
    {
        get => _remoteSessionId;
        set
        {
            if (SetProperty(ref _remoteSessionId, value))
            {
                CopySessionIdCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasUnreadAttention
    {
        get => _hasUnreadAttention;
        set
        {
            if (SetProperty(ref _hasUnreadAttention, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string AutomationName
        => HasUnreadAttention
            ? $"{Title}, unread"
            : Title;

    public bool IsPlaceholder { get; }

    public IAsyncRelayCommand ArchiveCommand { get; }
    public IAsyncRelayCommand CopySessionIdCommand { get; }

    public SessionNavItemViewModel(
        string sessionId,
        string projectId,
        string title,
        string relativeTimeText,
        IUiInteractionService ui,
        IChatSessionCatalog chatSessionCatalog,
        INavigationPaneState navigationState,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        bool isPlaceholder = false)
        : this(
            sessionId,
            remoteSessionId: null,
            projectId,
            title,
            relativeTimeText,
            ui,
            NoOpPlatformShellService.Instance,
            chatSessionCatalog,
            navigationState,
            uiDispatcher,
            localizer,
            isPlaceholder)
    {
    }

    public SessionNavItemViewModel(
        string sessionId,
        string? remoteSessionId,
        string projectId,
        string title,
        string relativeTimeText,
        IUiInteractionService ui,
        IPlatformShellService shell,
        IChatSessionCatalog chatSessionCatalog,
        INavigationPaneState navigationState,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        bool isPlaceholder = false)
        : base(navigationState, uiDispatcher)
    {
        SessionId = sessionId;
        _remoteSessionId = remoteSessionId;
        ProjectId = projectId;
        Title = title;
        RelativeTimeText = relativeTimeText;
        _ui = ui;
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _chatSessionCatalog = chatSessionCatalog ?? throw new ArgumentNullException(nameof(chatSessionCatalog));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        IsPlaceholder = isPlaceholder;

        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, CanArchive);
        CopySessionIdCommand = new AsyncRelayCommand(CopySessionIdAsync, CanCopySessionId);
    }

    private bool CanArchive()
        => !IsPlaceholder && !string.IsNullOrWhiteSpace(SessionId);

    private bool CanCopySessionId()
        => !IsPlaceholder && !string.IsNullOrWhiteSpace(RemoteSessionId);

    private async Task CopySessionIdAsync()
    {
        if (!CanCopySessionId())
        {
            return;
        }

        _ = await _shell.CopyToClipboardAsync(RemoteSessionId!).ConfigureAwait(true);
    }

    private async Task ArchiveAsync()
    {
        var confirmed = await _ui.ConfirmAsync(
            title: _localizer["Nav_ArchiveSessionTitle"],
            message: string.Format(
                CultureInfo.CurrentUICulture,
                _localizer["Nav_ArchiveSessionMessage"],
                Title),
            primaryButtonText: _localizer["Nav_ArchiveSessionPrimary"],
            closeButtonText: _localizer["Common_Cancel"]).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var result = await _chatSessionCatalog.ArchiveConversationAsync(SessionId).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            await _ui.ShowInfoAsync(_localizer["Nav_ArchiveSessionFailed"]).ConfigureAwait(true);
        }
    }

    private sealed class NullStringLocalizer : IStringLocalizer<CoreStrings>
    {
        public static readonly NullStringLocalizer Instance = new();

        public LocalizedString this[string name]
            => new(
                name,
                name switch
                {
                    "Nav_ArchiveSessionTitle" => "Archive session",
                    "Nav_ArchiveSessionMessage" => "Archive session \"{0}\"?",
                    "Nav_ArchiveSessionPrimary" => "Archive",
                    "Nav_ArchiveSessionFailed" => "Failed to archive the session. Please try again later.",
                    "Common_Cancel" => "Cancel",
                    _ => name
                });

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public IStringLocalizer WithCulture(CultureInfo culture) => this;
    }
}


public sealed partial class MoreSessionsNavItemViewModel : MainNavItemViewModel
{
    public string ProjectId { get; }
    private string _titleFormat;
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

    public string Title => Count > 0
        ? string.Format(CultureInfo.CurrentCulture, _titleFormat, Count)
        : string.Format(CultureInfo.CurrentCulture, _titleFormat, 0);

    public IAsyncRelayCommand ShowMoreCommand { get; }

    public MoreSessionsNavItemViewModel(
        string projectId,
        int remainingCount,
        IAsyncRelayCommand showMoreCommand,
        INavigationPaneState navigationState,
        IUiDispatcher uiDispatcher,
        string titleFormat = "Show more (+{0})")
        : base(navigationState, uiDispatcher)
    {
        ProjectId = projectId;
        _count = remainingCount;
        _titleFormat = titleFormat;
        ShowMoreCommand = showMoreCommand;
    }

    public void UpdateTitleFormat(string titleFormat)
    {
        if (string.Equals(_titleFormat, titleFormat, StringComparison.Ordinal))
        {
            return;
        }

        _titleFormat = titleFormat;
        OnPropertyChanged(nameof(Title));
    }
}
