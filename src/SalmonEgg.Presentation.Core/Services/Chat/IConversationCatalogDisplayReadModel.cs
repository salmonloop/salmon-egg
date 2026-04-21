using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationCatalogDisplayReadModel : INotifyPropertyChanged
{
    bool IsConversationListLoading { get; }

    int ConversationListVersion { get; }

    IReadOnlyList<ConversationCatalogDisplayItem> Snapshot { get; }
}

public sealed record ConversationCatalogDisplayItem(
    string ConversationId,
    string DisplayName,
    string? Cwd,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    DateTime LastAccessedAt,
    bool HasUnreadAttention,
    string? RemoteSessionId = null,
    string? BoundProfileId = null,
    string? ProjectAffinityOverrideProjectId = null);
