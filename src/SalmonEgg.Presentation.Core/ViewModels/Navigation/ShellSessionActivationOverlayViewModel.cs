using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed class ShellSessionActivationOverlayViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string[]> ChatPropertyMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [nameof(ChatViewModel.IsActivationOverlayVisible)] =
        [
            nameof(IsOverlayVisible)
        ],
        [nameof(ChatViewModel.ShouldShowBlockingLoadingMask)] =
        [
            nameof(ShowsBlockingMask)
        ],
        [nameof(ChatViewModel.ShouldShowLoadingOverlayStatusPill)] =
        [
            nameof(ShowsStatusPill)
        ],
        [nameof(ChatViewModel.ShouldShowLoadingOverlayPresenter)] =
        [
            nameof(ShowsPresenter)
        ],
        [nameof(ChatViewModel.OverlayStatusText)] =
        [
            nameof(StatusText)
        ]
    };

    private readonly ChatViewModel _chatViewModel;
    private readonly IShellNavigationRuntimeState _runtimeState;
    private readonly IUiDispatcher _uiDispatcher;

    public ShellSessionActivationOverlayViewModel(
        ChatViewModel chatViewModel,
        IShellNavigationRuntimeState runtimeState)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _uiDispatcher = _chatViewModel.Dispatcher;

        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        _runtimeState.PropertyChanged += OnRuntimeStatePropertyChanged;
    }

    public bool IsOverlayVisible => ShouldProjectChatOverlay && _chatViewModel.IsActivationOverlayVisible;

    public bool ShowsBlockingMask => ShouldProjectChatOverlay && _chatViewModel.ShouldShowBlockingLoadingMask;

    public bool ShowsStatusPill => ShouldProjectChatOverlay && _chatViewModel.ShouldShowLoadingOverlayStatusPill;

    public bool ShowsPresenter => ShouldProjectChatOverlay && _chatViewModel.ShouldShowLoadingOverlayPresenter;

    public string StatusText => ShouldProjectChatOverlay ? _chatViewModel.OverlayStatusText : string.Empty;

    private bool ShouldProjectChatOverlay
        => _runtimeState.CurrentShellContent == Models.Navigation.ShellNavigationContent.Chat
           || _runtimeState.IsSessionActivationInProgress;

    public void Dispose()
    {
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        _runtimeState.PropertyChanged -= OnRuntimeStatePropertyChanged;
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            RaiseProjectionChanged();
            return;
        }

        if (!ChatPropertyMap.TryGetValue(e.PropertyName, out var projectionProperties))
        {
            return;
        }

        RunOnUi(() => RaiseProjectionChanged(projectionProperties));
    }

    private void OnRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellNavigationRuntimeState.CurrentShellContent)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.DesiredSessionId)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.IsSessionActivationInProgress)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.ActiveSessionActivation))
        {
            RunOnUi(RaiseProjectionChanged);
        }
    }

    private void RaiseProjectionChanged()
    {
        OnPropertyChanged(nameof(IsOverlayVisible));
        OnPropertyChanged(nameof(ShowsBlockingMask));
        OnPropertyChanged(nameof(ShowsStatusPill));
        OnPropertyChanged(nameof(ShowsPresenter));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseProjectionChanged(IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
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
}
