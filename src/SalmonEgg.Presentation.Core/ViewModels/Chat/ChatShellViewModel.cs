using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed partial class ChatShellViewModel : ObservableObject
{
    private const int MiniWindowCompactDisplayNameMaxLength = 24;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly IConversationCatalogDisplayReadModel _conversationCatalog;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ILogger<ChatShellViewModel> _logger;
    private bool _suppressMiniWindowSelectionSync;
    private readonly ObservableCollection<MiniWindowConversationItemViewModel> _miniWindowSessions = [];

    public ChatShellViewModel(
        ChatViewModel chat,
        ShellLayoutViewModel shellLayout,
        INavigationCoordinator navigationCoordinator,
        IConversationCatalogDisplayReadModel conversationCatalog,
        IProjectAffinityResolver projectAffinityResolver,
        AppPreferencesViewModel preferences,
        ILogger<ChatShellViewModel> logger)
    {
        Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        ShellLayout = shellLayout ?? throw new ArgumentNullException(nameof(shellLayout));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _conversationCatalog = conversationCatalog ?? throw new ArgumentNullException(nameof(conversationCatalog));
        _projectAffinityResolver = projectAffinityResolver ?? throw new ArgumentNullException(nameof(projectAffinityResolver));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Chat.PropertyChanged += OnChatPropertyChanged;
        _conversationCatalog.PropertyChanged += OnConversationCatalogPropertyChanged;
        RefreshMiniWindowSessions();
        SyncSelectedMiniWindowSession();
    }

    public ChatViewModel Chat { get; }

    public ShellLayoutViewModel ShellLayout { get; }

    public ObservableCollection<MiniWindowConversationItemViewModel> MiniWindowSessions => _miniWindowSessions;

    [ObservableProperty]
    private MiniWindowConversationItemViewModel? _selectedMiniWindowSession;

    partial void OnSelectedMiniWindowSessionChanged(MiniWindowConversationItemViewModel? value)
    {
        if (_suppressMiniWindowSelectionSync
            || value is null
            || string.IsNullOrWhiteSpace(value.ConversationId)
            || string.Equals(Chat.CurrentSessionId, value.ConversationId, StringComparison.Ordinal))
        {
            return;
        }

        ActivateMiniWindowSessionAsync(value.ConversationId);
    }

    private async void ActivateMiniWindowSessionAsync(string conversationId)
    {
        try
        {
            await _navigationCoordinator
                .ActivateSessionAsync(conversationId, GetActivationProjectId(FindConversation(conversationId)))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to activate mini window conversation {ConversationId}", conversationId);
        }
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ChatViewModel.CurrentSessionId), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ChatViewModel.CurrentSessionDisplayName), StringComparison.Ordinal))
        {
            SyncSelectedMiniWindowSession();
        }
    }

    private void OnConversationCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || string.Equals(e.PropertyName, nameof(IConversationCatalogDisplayReadModel.Snapshot), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(IConversationCatalogDisplayReadModel.ConversationListVersion), StringComparison.Ordinal))
        {
            RefreshMiniWindowSessions();
        }
    }

    private void RefreshMiniWindowSessions()
    {
        _miniWindowSessions.Clear();
        foreach (var item in _conversationCatalog.Snapshot)
        {
            _miniWindowSessions.Add(new MiniWindowConversationItemViewModel(
                item.ConversationId,
                item.DisplayName,
                CreateMiniWindowCompactDisplayName(item.DisplayName),
                item.HasUnreadAttention));
        }

        OnPropertyChanged(nameof(MiniWindowSessions));
        SyncSelectedMiniWindowSession();
    }

    private void SyncSelectedMiniWindowSession()
    {
        if (_suppressMiniWindowSelectionSync)
        {
            return;
        }

        try
        {
            _suppressMiniWindowSelectionSync = true;
            if (string.IsNullOrWhiteSpace(Chat.CurrentSessionId))
            {
                SelectedMiniWindowSession = null;
                return;
            }

            var match = MiniWindowSessions.FirstOrDefault(item =>
                string.Equals(item.ConversationId, Chat.CurrentSessionId, StringComparison.Ordinal));
            if (!ReferenceEquals(SelectedMiniWindowSession, match))
            {
                SelectedMiniWindowSession = match;
            }
        }
        finally
        {
            _suppressMiniWindowSelectionSync = false;
        }
    }

    private ConversationCatalogDisplayItem? FindConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        return _conversationCatalog.Snapshot
            .FirstOrDefault(item => string.Equals(item.ConversationId, conversationId, StringComparison.Ordinal));
    }

    private string? GetActivationProjectId(ConversationCatalogDisplayItem? conversation)
    {
        if (conversation is null)
        {
            return null;
        }

        return _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: conversation.Cwd,
            BoundProfileId: conversation.BoundProfileId,
            RemoteSessionId: conversation.RemoteSessionId,
            OverrideProjectId: conversation.ProjectAffinityOverrideProjectId,
            Projects: _preferences.Projects,
            RemoteDirectories: _preferences.AgentRemoteDirectories,
            UnclassifiedProjectId: NavigationProjectIds.Unclassified)).EffectiveProjectId;
    }

    private static string CreateMiniWindowCompactDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var normalized = NormalizeMiniWindowDisplayName(displayName);
        var normalizedInfo = new StringInfo(normalized);
        if (normalizedInfo.LengthInTextElements <= MiniWindowCompactDisplayNameMaxLength)
        {
            return normalized;
        }

        return normalizedInfo.SubstringByTextElements(0, MiniWindowCompactDisplayNameMaxLength - 3) + "...";
    }

    private static string NormalizeMiniWindowDisplayName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousWasWhitespace = false;
        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }
}
