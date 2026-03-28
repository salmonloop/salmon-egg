using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Models.Search;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels;

public sealed partial class GlobalSearchViewModel : ObservableObject, IDisposable
{
    private const int MaxSearchResults = 50;
    private const int MaxHistoryItems = 10;
    private const int MaxSuggestions = 5;

    private readonly MainNavigationViewModel _navViewModel;
    private readonly AppPreferencesViewModel _preferences;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly IConversationCatalogReadModel _conversationCatalog;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly ILogger<GlobalSearchViewModel> _logger;

    private readonly List<SearchHistoryItem> _searchHistory = new();
    private readonly AsyncQueryCoordinator _searchCoordinator = new();
    private bool _suppressAutoOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(ShowSuggestions))]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasAnyContent))]
    private ObservableCollection<SearchResultGroup> _resultGroups = new();

    [ObservableProperty]
    private bool _isSearchPanelOpen;

    [ObservableProperty]
    private SearchResultItem? _selectedItem;

    [ObservableProperty]
    private bool _isSearchBoxFocused;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool IsSearching => HasQuery;
    public bool ShowSuggestions => !HasQuery && _searchHistory.Count > 0;
    public bool HasResults => ResultGroups.Count > 0 && ResultGroups.Any(g => g.Items.Count > 0);
    public bool IsEmpty => HasQuery && !HasResults;
    public bool HasAnyContent => HasResults || ShowSuggestions;

    public IReadOnlyList<SearchHistoryItem> RecentSearches => _searchHistory.AsReadOnly();

    public GlobalSearchViewModel(
        MainNavigationViewModel navViewModel,
        AppPreferencesViewModel preferences,
        INavigationCoordinator navigationCoordinator,
        IConversationCatalogReadModel conversationCatalog,
        IProjectAffinityResolver projectAffinityResolver,
        ILogger<GlobalSearchViewModel> logger)
    {
        _navViewModel = navViewModel ?? throw new ArgumentNullException(nameof(navViewModel));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _conversationCatalog = conversationCatalog ?? throw new ArgumentNullException(nameof(conversationCatalog));
        _projectAffinityResolver = projectAffinityResolver ?? throw new ArgumentNullException(nameof(projectAffinityResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    partial void OnQueryChanged(string value)
    {
        CancelPendingSearch();
        if (!_suppressAutoOpen || !string.IsNullOrWhiteSpace(value))
        {
            _suppressAutoOpen = false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            ResultGroups.Clear();
            UpdateSearchPanelState();
            return;
        }

        var ticket = _searchCoordinator.Begin();
        _ = SearchAsync(value, ticket);
    }

    private async Task SearchAsync(string query, AsyncQueryCoordinator.QueryTicket ticket)
    {
        try
        {
            await Task.Delay(150, ticket.Token);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }

            var normalizedQuery = query.Trim().ToLowerInvariant();
            var groups = new List<SearchResultGroup>();

            // 1. 搜索会话（当前页面相关性最高）
            var sessionGroup = await SearchSessionsAsync(normalizedQuery);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }
            if (sessionGroup.Items.Count > 0)
            {
                groups.Add(sessionGroup);
            }

            // 2. 搜索项目
            var projectGroup = SearchProjects(normalizedQuery);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }
            if (projectGroup.Items.Count > 0)
            {
                groups.Add(projectGroup);
            }

            // 3. 搜索设置
            var settingsGroup = SearchSettings(normalizedQuery);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }
            if (settingsGroup.Items.Count > 0)
            {
                groups.Add(settingsGroup);
            }

            // 4. 搜索命令
            var commandsGroup = SearchCommands(normalizedQuery);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }
            if (commandsGroup.Items.Count > 0)
            {
                groups.Add(commandsGroup);
            }

            if (!_searchCoordinator.IsActive(ticket) || !string.Equals(Query, query, StringComparison.Ordinal))
            {
                return;
            }

            ResultGroups = new ObservableCollection<SearchResultGroup>(
                groups.OrderByDescending(g => g.Priority).Take(MaxSearchResults));
            UpdateSearchPanelState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search failed for query: {Query}", query);
        }
    }

    private Task<SearchResultGroup> SearchSessionsAsync(string normalizedQuery)
    {
        var group = new SearchResultGroup
        {
            Title = "会话",
            Name = "sessions",
            Priority = 100
        };

        var matches = _conversationCatalog.Snapshot
            .Where(session => MatchScore(session.DisplayName ?? string.Empty, normalizedQuery) > 0
                || MatchScore(session.ConversationId, normalizedQuery) > 0)
            .OrderByDescending(session => MatchScore(session.DisplayName ?? string.Empty, normalizedQuery))
            .Take(10);

        foreach (var session in matches)
        {
            group.Items.Add(new SearchResultItem
            {
                Id = session.ConversationId,
                Title = session.DisplayName ?? SessionNamePolicy.CreateDefault(session.ConversationId),
                Subtitle = session.Cwd,
                Kind = SearchResultKind.Session,
                IconGlyph = "\uE8BD", // Chat icon
                ActivateCommand = SelectResultCommand
            });
        }

        return Task.FromResult(group);
    }

    private SearchResultGroup SearchProjects(string normalizedQuery)
    {
        var group = new SearchResultGroup
        {
            Title = "项目",
            Name = "projects",
            Priority = 90
        };

        // 未归类项目
        if (MatchScore("未归类", normalizedQuery) > 0)
        {
                group.Items.Add(new SearchResultItem
                {
                    Id = MainNavigationViewModel.UnclassifiedProjectId,
                    Title = "未归类",
                    Subtitle = null,
                    Kind = SearchResultKind.Project,
                    IconGlyph = "\uE8F1", // Folder
                    ActivateCommand = SelectResultCommand
                });
        }

        // 用户项目
        foreach (var project in _preferences.Projects)
        {
            if (MatchScore(project.Name, normalizedQuery) > 0
                || MatchScore(project.RootPath, normalizedQuery) > 0)
            {
                group.Items.Add(new SearchResultItem
                {
                    Id = project.ProjectId,
                    Title = project.Name,
                    Subtitle = project.RootPath,
                    Kind = SearchResultKind.Project,
                    IconGlyph = "\uE8F1",
                    ActivateCommand = SelectResultCommand
                });
            }
        }

        return group;
    }

    private SearchResultGroup SearchSettings(string normalizedQuery)
    {
        var group = new SearchResultGroup
        {
            Title = "设置",
            Name = "settings",
            Priority = 80
        };

        var settingsItems = new (string Id, string Title, string Subtitle)[]
        {
            ("general", "通用设置", "主题、语言、启动选项"),
            ("shortcuts", "快捷键", "自定义键盘快捷键"),
            ("appearance", "外观", "主题和背景效果"),
            ("data", "数据与存储", "历史记录和缓存管理"),
            ("profiles", "ACP 配置", "服务器和连接配置"),
            ("diagnostics", "诊断", "日志和调试信息"),
            ("about", "关于", "版本和许可证信息")
        };

        foreach (var (id, title, subtitle) in settingsItems)
        {
            if (MatchScore(title, normalizedQuery) > 0
                || MatchScore(subtitle, normalizedQuery) > 0
                || MatchScore(id, normalizedQuery) > 0)
            {
                group.Items.Add(new SearchResultItem
                {
                    Id = id,
                    Title = title,
                    Subtitle = subtitle,
                    Kind = SearchResultKind.Setting,
                    IconGlyph = "\uE713", // Settings gear
                    ActivateCommand = SelectResultCommand
                });
            }
        }

        return group;
    }

    private SearchResultGroup SearchCommands(string normalizedQuery)
    {
        var group = new SearchResultGroup
        {
            Title = "命令",
            Name = "commands",
            Priority = 70
        };

        var commands = new (string Id, string Title, string Subtitle, string Tag)[]
        {
            ("new_session", "新建会话", "创建新的聊天会话", "new"),
            ("new_project", "新建项目", "添加新的项目文件夹", "project"),
            ("toggle_theme", "切换主题", "在亮色/暗色/系统主题间切换", "theme"),
            ("toggle_anim", "切换动画", "启用或禁用界面动画", "animation"),
            ("toggle_panel", "切换面板", "打开或关闭右侧面板", "panel"),
            ("open_shortcuts", "打开快捷键设置", "查看所有快捷键", "shortcuts"),
            ("open_diag", "打开诊断", "查看日志和调试信息", "diagnostics")
        };

        foreach (var (id, title, subtitle, tag) in commands)
        {
            if (MatchScore(title, normalizedQuery) > 0
                || MatchScore(subtitle, normalizedQuery) > 0
                || MatchScore(id, normalizedQuery) > 0)
            {
                group.Items.Add(new SearchResultItem
                {
                    Id = id,
                    Title = title,
                    Subtitle = subtitle,
                    Kind = SearchResultKind.Command,
                    IconGlyph = "\uE756", // Command
                    ActivateCommand = SelectResultCommand,
                    Tag = tag
                });
            }
        }

        return group;
    }

    private static int MatchScore(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var lower = text.ToLowerInvariant();

        // 精确匹配
        if (lower == query)
        {
            return 100;
        }

        // 开头匹配
        if (lower.StartsWith(query))
        {
            return 80;
        }

        // 包含匹配
        if (lower.Contains(query))
        {
            return 50;
        }

        // 单词边界匹配
        var words = lower.Split(new[] { ' ', '_', '-', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(query)))
        {
            return 60;
        }

        return 0;
    }

    [RelayCommand]
    private async Task SelectResultAsync(SearchResultItem item)
    {
        if (item == null)
        {
            return;
        }

        // 添加到历史记录
        AddToHistory(Query);

        // 根据类型处理
        switch (item.Kind)
        {
            case SearchResultKind.Session:
                var session = FindConversation(item.Id);
                await _navigationCoordinator.ActivateSessionAsync(item.Id, GetActivationProjectId(session));
                break;

            case SearchResultKind.Project:
                if (string.Equals(item.Id, MainNavigationViewModel.UnclassifiedProjectId, StringComparison.Ordinal))
                {
                    await _navViewModel.PrepareStartForProjectAsync(MainNavigationViewModel.UnclassifiedProjectId);
                }
                else
                {
                    await _navViewModel.PrepareStartForProjectAsync(item.Id);
                }
                break;

            case SearchResultKind.Setting:
                await _navigationCoordinator.ActivateSettingsAsync(item.Id);
                break;

            case SearchResultKind.Command:
                ExecuteCommand(item);
                break;
        }

        // 关闭搜索面板
        CloseSearchPanel();
    }

    private void ExecuteCommand(SearchResultItem item)
    {
        switch (item.Tag)
        {
            case "new":
                _ = _navViewModel.PrepareStartForProjectAsync(MainNavigationViewModel.UnclassifiedProjectId);
                break;

            case "project":
                _ = _navViewModel.AddProjectCommand.ExecuteAsync(null);
                break;

            case "theme":
                // 切换主题 - 通过偏好设置
                var currentTheme = _preferences.Theme;
                _preferences.Theme = currentTheme switch
                {
                    "Light" => "Dark",
                    "Dark" => "System",
                    _ => "Light"
                };
                break;

            case "animation":
                _preferences.IsAnimationEnabled = !_preferences.IsAnimationEnabled;
                break;
        }
    }

    private ConversationCatalogItem? FindConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        return _conversationCatalog.Snapshot
            .FirstOrDefault(item => string.Equals(item.ConversationId, conversationId, StringComparison.Ordinal));
    }

    private string? GetActivationProjectId(ConversationCatalogItem? conversation)
    {
        if (conversation == null)
        {
            return null;
        }

        return _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: conversation.Cwd,
            BoundProfileId: conversation.BoundProfileId,
            RemoteSessionId: conversation.RemoteSessionId,
            OverrideProjectId: conversation.ProjectAffinityOverrideProjectId,
            Projects: _preferences.Projects,
            PathMappings: _preferences.ProjectPathMappings,
            UnclassifiedProjectId: MainNavigationViewModel.UnclassifiedProjectId)).EffectiveProjectId;
    }

    [RelayCommand]
    private void OpenSearchPanel()
    {
        IsSearchPanelOpen = true;
    }

    [RelayCommand]
    private void CloseSearchPanel()
    {
        _suppressAutoOpen = true;
        IsSearchPanelOpen = false;
        Query = string.Empty;
        ResultGroups.Clear();
    }

    [RelayCommand]
    private void UseHistoryItem(string? query)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            Query = query;
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _searchHistory.Clear();
        OnPropertyChanged(nameof(RecentSearches));
        OnPropertyChanged(nameof(ShowSuggestions));
        UpdateSearchPanelState();
    }

    private void AddToHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        // 移除已存在的相同项
        var existingIndex = _searchHistory.FindIndex(item => string.Equals(item.Query, query, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _searchHistory.RemoveAt(existingIndex);
        }

        // 添加到开头
        _searchHistory.Insert(0, new SearchHistoryItem
        {
            Query = query,
            UseCommand = UseHistoryItemCommand
        });

        // 限制历史记录数量
        while (_searchHistory.Count > MaxHistoryItems)
        {
            _searchHistory.RemoveAt(_searchHistory.Count - 1);
        }

        OnPropertyChanged(nameof(RecentSearches));
        OnPropertyChanged(nameof(ShowSuggestions));
        UpdateSearchPanelState();
    }

    partial void OnIsSearchBoxFocusedChanged(bool value)
    {
        if (value)
        {
            _suppressAutoOpen = false;
        }

        UpdateSearchPanelState();
    }

    private void UpdateSearchPanelState()
    {
        if (!IsSearchBoxFocused || !HasAnyContent)
        {
            IsSearchPanelOpen = false;
            return;
        }

        if (_suppressAutoOpen)
        {
            return;
        }

        IsSearchPanelOpen = true;
    }

    public void FocusSearch()
    {
        IsSearchPanelOpen = true;
    }

    private void CancelPendingSearch()
    {
        _searchCoordinator.Cancel();
    }

    public void Dispose()
    {
        _searchCoordinator.Dispose();
    }
}
