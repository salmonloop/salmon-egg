using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed partial class ConversationCatalogPresenter : ObservableObject, IConversationCatalogReadModel
{
    [ObservableProperty]
    private bool _isConversationListLoading = true;

    [ObservableProperty]
    private int _conversationListVersion;

    private IReadOnlyList<ConversationCatalogItem> _snapshot = Array.Empty<ConversationCatalogItem>();

    public IReadOnlyList<ConversationCatalogItem> Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public void SetLoading(bool value)
    {
        if (IsConversationListLoading == value)
        {
            return;
        }

        IsConversationListLoading = value;
    }

    public void Refresh(IReadOnlyList<ConversationCatalogItem> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        ConversationListVersion++;
    }
}

/// <summary>
/// Read-only data for a single conversation in the navigation catalog.
/// </summary>
public sealed record ConversationCatalogItem(
    string ConversationId,
    string DisplayName,
    string? Cwd,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    DateTime LastAccessedAt,
    string? RemoteSessionId = null,
    string? BoundProfileId = null,
    string? ProjectAffinityOverrideProjectId = null);
