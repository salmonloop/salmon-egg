using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationCatalogDisplayPresenter : ObservableObject, IConversationCatalogDisplayReadModel, IDisposable
{
    private readonly IConversationCatalogReadModel _catalogPresenter;
    private readonly IConversationAttentionStore _attentionStore;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDisposable? _attentionSubscription;
    private readonly IState<ConversationAttentionState> _attentionStateFeed;
    private ConversationAttentionState _attentionState;
    private IReadOnlyList<ConversationCatalogDisplayItem> _snapshot = Array.Empty<ConversationCatalogDisplayItem>();
    private bool _isConversationListLoading = true;
    private int _conversationListVersion;
    private bool _disposed;

    public ConversationCatalogDisplayPresenter(
        IConversationCatalogReadModel catalogPresenter,
        IConversationAttentionStore attentionStore,
        IUiDispatcher uiDispatcher)
    {
        _catalogPresenter = catalogPresenter ?? throw new ArgumentNullException(nameof(catalogPresenter));
        _attentionStore = attentionStore ?? throw new ArgumentNullException(nameof(attentionStore));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        _attentionState = ConversationAttentionState.Empty;
        _attentionStateFeed = State.FromFeed(this, _attentionStore.State);

        _catalogPresenter.PropertyChanged += OnCatalogPresenterPropertyChanged;
        RefreshProjection();

        _attentionStateFeed.ForEach((state, ct) =>
        {
            if (state is null || ct.IsCancellationRequested || _disposed)
            {
                return ValueTask.CompletedTask;
            }

            PublishAttentionState(state);
            return ValueTask.CompletedTask;
        }, out _attentionSubscription);
    }

    public bool IsConversationListLoading
    {
        get => _isConversationListLoading;
        private set => SetProperty(ref _isConversationListLoading, value);
    }

    public int ConversationListVersion
    {
        get => _conversationListVersion;
        private set => SetProperty(ref _conversationListVersion, value);
    }

    public IReadOnlyList<ConversationCatalogDisplayItem> Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalogPresenter.PropertyChanged -= OnCatalogPresenterPropertyChanged;
        _attentionSubscription?.Dispose();
    }

    private void OnCatalogPresenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || string.Equals(e.PropertyName, nameof(IConversationCatalogReadModel.IsConversationListLoading), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(IConversationCatalogReadModel.ConversationListVersion), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(IConversationCatalogReadModel.Snapshot), StringComparison.Ordinal))
        {
            RunOnUi(RefreshProjection);
        }
    }

    private void PublishAttentionState(ConversationAttentionState attentionState)
    {
        RunOnUi(() =>
        {
            if (Equals(_attentionState, attentionState))
            {
                return;
            }

            _attentionState = attentionState;
            RefreshProjection();
        });
    }

    private void RefreshProjection()
    {
        if (_disposed)
        {
            return;
        }

        var catalogSnapshot = _catalogPresenter.Snapshot;
        var projectedSnapshot = new List<ConversationCatalogDisplayItem>(catalogSnapshot.Count);

        foreach (var item in catalogSnapshot)
        {
            var hasUnreadAttention = _attentionState.TryGetConversation(item.ConversationId, out var attention)
                && attention is { HasUnread: true };

            projectedSnapshot.Add(new ConversationCatalogDisplayItem(
                item.ConversationId,
                item.DisplayName,
                item.Cwd,
                item.CreatedAt,
                item.LastUpdatedAt,
                item.LastAccessedAt,
                hasUnreadAttention,
                item.RemoteSessionId,
                item.BoundProfileId,
                item.ProjectAffinityOverrideProjectId));
        }

        IsConversationListLoading = _catalogPresenter.IsConversationListLoading;
        ConversationListVersion = _catalogPresenter.ConversationListVersion;
        Snapshot = projectedSnapshot;
    }

    private void RunOnUi(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _uiDispatcher.Enqueue(action);
    }
}
