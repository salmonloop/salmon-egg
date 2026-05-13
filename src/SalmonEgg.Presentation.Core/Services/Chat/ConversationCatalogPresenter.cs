using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed partial class ConversationCatalogPresenter : ObservableObject, IConversationCatalogReadModel
{
    private readonly IUiDispatcher _uiDispatcher;

    [ObservableProperty]
    private bool _isConversationListLoading = true;

    [ObservableProperty]
    private int _conversationListVersion;

    private IReadOnlyList<ConversationCatalogItem> _snapshot = Array.Empty<ConversationCatalogItem>();

    public ConversationCatalogPresenter()
        : this(new InlineUiDispatcher())
    {
    }

    public ConversationCatalogPresenter(IUiDispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public IReadOnlyList<ConversationCatalogItem> Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public void SetLoading(bool value)
    {
        RunOnUi(() =>
        {
            if (IsConversationListLoading == value)
            {
                return;
            }

            IsConversationListLoading = value;
        });
    }

    public void Refresh(IReadOnlyList<ConversationCatalogItem> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RunOnUi(() =>
        {
            Snapshot = snapshot;
            ConversationListVersion++;
        });
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _uiDispatcher.Enqueue(action);
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);
            return function();
        }
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
    DateTime CatalogUpdatedAt,
    DateTime LastAccessedAt,
    string? RemoteSessionId = null,
    string? BoundProfileId = null,
    string? ProjectAffinityOverrideProjectId = null);
