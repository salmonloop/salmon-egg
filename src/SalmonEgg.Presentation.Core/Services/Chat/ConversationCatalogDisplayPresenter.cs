using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationCatalogDisplayPresenter : ObservableObject, IConversationCatalogDisplayReadModel, IDisposable
{
    private readonly IConversationCatalogReadModel _catalogPresenter;
    private readonly IConversationAttentionStore _attentionStore;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDisposable? _attentionSubscription;
    private readonly IState<ConversationAttentionState> _attentionStateFeed;
    private readonly IDisposable? _chatSubscription;
    private readonly IState<ConversationStatusState>? _chatStateFeed;
    private readonly ChatConversationPanelStateCoordinator? _panels;
    private IImmutableDictionary<string, ActiveTurnState>? _turns;
    private IImmutableDictionary<string, ConversationOperationFailure>? _operationFailures;
    private ConversationAttentionState _attentionState;
    private IReadOnlyList<ConversationCatalogDisplayItem> _snapshot = Array.Empty<ConversationCatalogDisplayItem>();
    private bool _isConversationListLoading = true;
    private int _conversationListVersion;
    private bool _disposed;

    public ConversationCatalogDisplayPresenter(
        IConversationCatalogReadModel catalogPresenter,
        IConversationAttentionStore attentionStore,
        IUiDispatcher uiDispatcher,
        IChatStore? chatStore = null,
        ChatConversationPanelStateCoordinator? panels = null)
    {
        _catalogPresenter = catalogPresenter ?? throw new ArgumentNullException(nameof(catalogPresenter));
        _attentionStore = attentionStore ?? throw new ArgumentNullException(nameof(attentionStore));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _panels = panels;
        if (_panels is not null) _panels.Changed += OnInteractionChanged;

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

        if (chatStore is not null)
        {
            _chatStateFeed = State.FromFeed(this, chatStore.State.Select(state =>
                new ConversationStatusState(state?.Turns, state?.OperationFailures)));
            _chatStateFeed.ForEach((state, token) =>
            {
                if (state is not null && !token.IsCancellationRequested)
                {
                    RunOnUi(() =>
                    {
                        if (ReferenceEquals(_turns, state.Turns)
                            && ReferenceEquals(_operationFailures, state.OperationFailures)) return;
                        _turns = state.Turns;
                        _operationFailures = state.OperationFailures;
                        RefreshProjection();
                    });
                }

                return ValueTask.CompletedTask;
            }, out _chatSubscription);
        }
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
        _chatSubscription?.Dispose();
        if (_panels is not null) _panels.Changed -= OnInteractionChanged;
    }

    private void OnInteractionChanged(string conversationId) => RunOnUi(RefreshProjection);

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
            var status = ConversationStatusPolicy.Resolve(
                _turns?.GetValueOrDefault(item.ConversationId),
                _panels?.GetSummary(item.ConversationId) ?? default,
                attention,
                _operationFailures?.GetValueOrDefault(item.ConversationId));

            projectedSnapshot.Add(new ConversationCatalogDisplayItem(
                item.ConversationId,
                item.DisplayName,
                item.Cwd,
                item.CreatedAt,
                item.CatalogUpdatedAt,
                item.LastAccessedAt,
                hasUnreadAttention,
                item.RemoteSessionId,
                item.BoundProfileId,
                item.ProjectAffinityOverrideProjectId,
                status.Group,
                status.Icon,
                status.ActivityAt));
        }

        IsConversationListLoading = _catalogPresenter.IsConversationListLoading;
        ConversationListVersion = _catalogPresenter.ConversationListVersion;
        if (!_snapshot.SequenceEqual(projectedSnapshot)) Snapshot = projectedSnapshot;
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

    private sealed record ConversationStatusState(
        IImmutableDictionary<string, ActiveTurnState>? Turns,
        IImmutableDictionary<string, ConversationOperationFailure>? OperationFailures);
}
