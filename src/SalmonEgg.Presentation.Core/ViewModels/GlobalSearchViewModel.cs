using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Search;
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
    private readonly IGlobalSearchPipeline _searchPipeline;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<GlobalSearchViewModel> _logger;

    private readonly List<SearchHistoryItem> _searchHistory = new();
    private readonly AsyncQueryCoordinator _searchCoordinator = new();
    private int _activeSearchRequestId;

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
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(HasAnyContent))]
    private GlobalSearchViewState _viewState = GlobalSearchViewState.Idle;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool IsSearching => ViewState == GlobalSearchViewState.Loading;
    public bool ShowSuggestions => !HasQuery && _searchHistory.Count > 0;
    public bool HasResults => ResultGroups.Count > 0 && ResultGroups.Any(g => g.Items.Count > 0);
    public bool IsEmpty => ViewState == GlobalSearchViewState.Empty;
    public bool IsError => ViewState == GlobalSearchViewState.Error;
    public bool HasAnyContent => HasResults || ShowSuggestions || IsSearching || IsEmpty || IsError;

    public IReadOnlyList<SearchSuggestionEntry> SuggestionEntries => BuildSuggestionEntries();

    public IReadOnlyList<SearchHistoryItem> RecentSearches => _searchHistory.AsReadOnly();

    public GlobalSearchViewModel(
        MainNavigationViewModel navViewModel,
        AppPreferencesViewModel preferences,
        INavigationCoordinator navigationCoordinator,
        IConversationCatalogReadModel conversationCatalog,
        IProjectAffinityResolver projectAffinityResolver,
        IGlobalSearchPipeline searchPipeline,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GlobalSearchViewModel> logger)
    {
        _navViewModel = navViewModel ?? throw new ArgumentNullException(nameof(navViewModel));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _conversationCatalog = conversationCatalog ?? throw new ArgumentNullException(nameof(conversationCatalog));
        _projectAffinityResolver = projectAffinityResolver ?? throw new ArgumentNullException(nameof(projectAffinityResolver));
        _searchPipeline = searchPipeline ?? throw new ArgumentNullException(nameof(searchPipeline));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resultGroups.CollectionChanged += OnResultGroupsCollectionChanged;
    }

    partial void OnQueryChanged(string value)
    {
        CancelPendingSearch();
        if (string.IsNullOrWhiteSpace(value))
        {
            ResultGroups.Clear();
            ViewState = GlobalSearchViewState.Idle;
            RaiseSuggestionEntriesChanged();
            return;
        }

        ResultGroups.Clear();
        ViewState = GlobalSearchViewState.Loading;
        RaiseSuggestionEntriesChanged();
        var ticket = _searchCoordinator.Begin();
        _ = Interlocked.Increment(ref _activeSearchRequestId);
        _ = SearchAsync(value, ticket);
    }

    private async Task SearchAsync(string query, AsyncQueryCoordinator.QueryTicket ticket)
    {
        var requestId = Volatile.Read(ref _activeSearchRequestId);
        try
        {
            await Task.Delay(150, ticket.Token);
            if (!_searchCoordinator.IsActive(ticket))
            {
                return;
            }

            var sourceSnapshot = BuildSearchSourceSnapshot();
            var result = await Task.Run(
                () => _searchPipeline.SearchAsync(query, sourceSnapshot, ticket.Token),
                ticket.Token).ConfigureAwait(true);

            if (!_searchCoordinator.IsActive(ticket)
                || !string.Equals(Query, query, StringComparison.Ordinal)
                || requestId != Volatile.Read(ref _activeSearchRequestId))
            {
                return;
            }

            ApplySearchSnapshot(result);
            ViewState = ResultGroups.Count > 0
                ? GlobalSearchViewState.Results
                : GlobalSearchViewState.Empty;
        }
        catch (OperationCanceledException) when (ticket.Token.IsCancellationRequested)
        {
            // Swallow cancellation; latest request owns the state.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search failed for query: {Query}", query);
            if (_searchCoordinator.IsActive(ticket)
                && string.Equals(Query, query, StringComparison.Ordinal)
                && requestId == Volatile.Read(ref _activeSearchRequestId))
            {
                ViewState = GlobalSearchViewState.Error;
            }
        }
    }

    private GlobalSearchSourceSnapshot BuildSearchSourceSnapshot()
    {
        var sessions = _conversationCatalog.Snapshot
            .Select(session => new GlobalSearchSessionSource(
                session.ConversationId,
                session.DisplayName ?? SessionNamePolicy.CreateDefault(session.ConversationId),
                session.Cwd))
            .ToImmutableArray();
        var projects = _preferences.Projects
            .Select(project => new GlobalSearchProjectSource(project.ProjectId, project.Name, project.RootPath))
            .ToImmutableArray();
        return new GlobalSearchSourceSnapshot(sessions, projects);
    }

    private void ApplySearchSnapshot(GlobalSearchSnapshot snapshot)
    {
        var topGroups = snapshot.Groups.Take(MaxSearchResults);
        ResultGroups.Clear();
        foreach (var groupSnapshot in topGroups)
        {
            var group = new SearchResultGroup
            {
                Name = groupSnapshot.Name,
                Title = groupSnapshot.Title,
                Priority = groupSnapshot.Priority
            };

            foreach (var item in groupSnapshot.Items)
            {
                group.Items.Add(new SearchResultItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Kind = item.Kind,
                    IconGlyph = item.IconGlyph,
                    Tag = item.Tag
                });
            }

            ResultGroups.Add(group);
        }
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

        ClearSearchState();
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
    private void ClearHistory()
    {
        _searchHistory.Clear();
        OnPropertyChanged(nameof(RecentSearches));
        OnPropertyChanged(nameof(ShowSuggestions));
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
            Query = query
        });

        // 限制历史记录数量
        while (_searchHistory.Count > MaxHistoryItems)
        {
            _searchHistory.RemoveAt(_searchHistory.Count - 1);
        }

        OnPropertyChanged(nameof(RecentSearches));
        OnPropertyChanged(nameof(ShowSuggestions));
        RaiseSuggestionEntriesChanged();
    }

    partial void OnViewStateChanged(GlobalSearchViewState value)
    {
        RaiseSuggestionEntriesChanged();
    }

    private void CancelPendingSearch()
    {
        _searchCoordinator.Cancel();
    }

    private void OnResultGroupsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(HasAnyContent));
        RaiseSuggestionEntriesChanged();
    }

    partial void OnResultGroupsChanged(ObservableCollection<SearchResultGroup>? oldValue, ObservableCollection<SearchResultGroup> newValue)
    {
        if (oldValue != null)
        {
            oldValue.CollectionChanged -= OnResultGroupsCollectionChanged;
        }

        newValue.CollectionChanged += OnResultGroupsCollectionChanged;
    }

    public void Dispose()
    {
        ResultGroups.CollectionChanged -= OnResultGroupsCollectionChanged;
        _searchCoordinator.Dispose();
    }

    public Task ActivateSuggestionAsync(SearchSuggestionEntry? entry)
    {
        if (entry == null || !entry.IsActionable)
        {
            return Task.CompletedTask;
        }

        return entry.Kind switch
        {
            SearchSuggestionEntryKind.Result when entry.ResultItem is not null => SelectResultAsync(entry.ResultItem),
            SearchSuggestionEntryKind.History when !string.IsNullOrWhiteSpace(entry.HistoryQuery) => ActivateHistorySuggestion(entry.HistoryQuery!),
            _ => Task.CompletedTask
        };
    }

    public Task SubmitQueryAsync(string? queryText)
    {
        var submittedText = queryText?.Trim();
        if (string.IsNullOrWhiteSpace(submittedText))
        {
            return Task.CompletedTask;
        }

        if (!string.Equals(Query, submittedText, StringComparison.Ordinal))
        {
            Query = submittedText;
        }

        var entry = SuggestionEntries.FirstOrDefault(candidate => candidate.IsActionable);
        return ActivateSuggestionAsync(entry);
    }

    private Task ActivateHistorySuggestion(string query)
    {
        Query = query;
        return Task.CompletedTask;
    }

    private void ClearSearchState()
    {
        Query = string.Empty;
        ResultGroups.Clear();
        ViewState = GlobalSearchViewState.Idle;
    }

    private void RaiseSuggestionEntriesChanged()
    {
        OnPropertyChanged(nameof(SuggestionEntries));
    }

    private IReadOnlyList<SearchSuggestionEntry> BuildSuggestionEntries()
    {
        if (IsSearching)
        {
            return
            [
                new SearchSuggestionEntry
                {
                    AutomationId = "SearchSuggestion.Status.Loading",
                    Title = _localizer["Search_Loading"],
                    Subtitle = _localizer["Search_LoadingSubtitle"],
                    IconGlyph = "\uE895",
                    Kind = SearchSuggestionEntryKind.Status
                }
            ];
        }

        if (HasResults)
        {
            return ResultGroups
                .SelectMany(
                    group => group.Items.Select(
                        item => new SearchSuggestionEntry
                        {
                            AutomationId = $"SearchSuggestion.Result.{item.Id}",
                            Title = item.Title,
                            Subtitle = item.Subtitle,
                            SectionTitle = group.Title,
                            IconGlyph = item.IconGlyph,
                            Kind = SearchSuggestionEntryKind.Result,
                            ResultItem = item
                        }))
                .ToArray();
        }

        if (ShowSuggestions)
        {
            return _searchHistory
                .Take(MaxSuggestions)
                .Select(
                    item => new SearchSuggestionEntry
                    {
                        AutomationId = $"SearchSuggestion.History.{SanitizeAutomationSegment(item.Query)}",
                        Title = item.Query,
                        Subtitle = _localizer["Search_History"],
                        IconGlyph = "\uE81C",
                        Kind = SearchSuggestionEntryKind.History,
                        HistoryQuery = item.Query
                    })
                .ToArray();
        }

        if (IsEmpty)
        {
            return
            [
                new SearchSuggestionEntry
                {
                    AutomationId = "SearchSuggestion.Status.Empty",
                    Title = _localizer["Search_Empty"],
                    Subtitle = _localizer["Search_EmptySubtitle"],
                    IconGlyph = "\uE783",
                    Kind = SearchSuggestionEntryKind.Status
                }
            ];
        }

        if (IsError)
        {
            return
            [
                new SearchSuggestionEntry
                {
                    AutomationId = "SearchSuggestion.Status.Error",
                    Title = _localizer["Search_Error"],
                    Subtitle = _localizer["Search_ErrorSubtitle"],
                    IconGlyph = "\uEA39",
                    Kind = SearchSuggestionEntryKind.Status
                }
            ];
        }

        return Array.Empty<SearchSuggestionEntry>();
    }

    private static string SanitizeAutomationSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Empty";
        }

        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }
}
