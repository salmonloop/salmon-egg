using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

/// <summary>
/// Shared conversation-catalog double for navigation tests.
/// </summary>
/// <remarks>
/// Implements <see cref="IChatSessionCatalog"/>, which is a marker over
/// <see cref="IConversationCatalog"/>, so it satisfies tests that depend on either. Kept in one place
/// because per-file copies drift: they had already grown different helpers and different mutation
/// results, so adding a catalog member meant editing several near-identical classes.
/// </remarks>
public sealed class FakeChatSessionCatalog : IChatSessionCatalog
{
    private readonly List<string> _conversationIds;

    public FakeChatSessionCatalog(params string[] conversationIds)
    {
        _conversationIds = [.. conversationIds];
    }

    /// <summary>
    /// Result returned by both archive and delete, so a test can drive the failure branches.
    /// </summary>
    public ConversationMutationResult MutationResult { get; set; } = new(true, false, null);

    public bool IsConversationListLoading { get; set; }

    public int ConversationListVersion { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string[] GetKnownConversationIds() => [.. _conversationIds];

    /// <summary>
    /// Projects the known ids as catalog items, for tests that need a snapshot rather than just ids.
    /// </summary>
    public IReadOnlyList<ConversationCatalogItem> CreateSnapshot()
    {
        var now = DateTime.UtcNow;
        return _conversationIds.ConvertAll(id => new ConversationCatalogItem(
            id,
            id,
            @"C:\repo\demo",
            now,
            now,
            now));
    }

    public void AddConversation(string conversationId)
    {
        _conversationIds.Add(conversationId);
        BumpConversationListVersion();
    }

    public void RemoveConversation(string conversationId)
    {
        if (_conversationIds.Remove(conversationId))
        {
            BumpConversationListVersion();
        }
    }

    public void BumpConversationListVersion()
    {
        ConversationListVersion++;
        RaisePropertyChanged(nameof(ConversationListVersion));
    }

    public void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task FlushPendingSaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ConversationMutationResult> ArchiveConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(MutationResult);

    public Task<ConversationMutationResult> DeleteConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(MutationResult);
}
