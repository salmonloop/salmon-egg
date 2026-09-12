using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Controls;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.Foundation;

namespace SalmonEgg.Presentation.Transcript;

/// <summary>Reports visible native content; reading state remains in the attention store.</summary>
internal sealed class TranscriptReadReceiptObserver : IDisposable
{
    private readonly ListViewBase _list;
    private readonly ChatViewModel _viewModel;
    private readonly AppActivationSignalSource _activation;
    private readonly Dictionary<ListViewItem, Observation> _observations = new();
    private readonly HashSet<Popup> _observedPopups = new();
    private Rect? _effectiveViewport;
    private bool _disposed;

    public TranscriptReadReceiptObserver(ListViewBase list, ChatViewModel viewModel, AppActivationSignalSource activation)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.EffectiveViewportChanged += OnEffectiveViewportChanged;
        _activation.ActivityChanged += OnActivityChanged;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.ReplyReadObservationRequested += ReportVisibleAsync;
        _list.Loaded += OnListLoaded;
        ObserveRealizedItems();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _list.ContainerContentChanging -= OnContainerContentChanging;
        _list.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        _activation.ActivityChanged -= OnActivityChanged;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.ReplyReadObservationRequested -= ReportVisibleAsync;
        _list.Loaded -= OnListLoaded;
        foreach (var observation in _observations.Values) observation.Dispose();
        _observations.Clear();
        foreach (var popup in _observedPopups) popup.Closed -= OnPopupClosed;
        _observedPopups.Clear();
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container) return;
        if (_observations.Remove(container, out var old)) old.Dispose();
        if (!args.InRecycleQueue && args.Item is ChatMessageViewModel message && !message.IsOutgoing)
        {
            var observation = new Observation(this, container, message);
            _observations.Add(container, observation);
            observation.Report();
        }
    }

    private void OnActivityChanged(object? sender, EventArgs args) => ReportVisible();
    private void OnListLoaded(object sender, RoutedEventArgs args) => ObserveRealizedItems();

    private void OnEffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        _effectiveViewport = args.EffectiveViewport;
        ReportVisible();
    }

    private void ObserveRealizedItems()
    {
        if (_list.ItemsPanelRoot is not Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is ListViewItem container && !_observations.ContainsKey(container)
                && container.Content is ChatMessageViewModel { IsOutgoing: false } message)
            {
                var observation = new Observation(this, container, message);
                _observations.Add(container, observation);
                observation.Report();
            }
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChatViewModel.IsActivationOverlayVisible) or nameof(ChatViewModel.CurrentSessionId)) ReportVisible();
    }

    internal void ReportVisible()
    {
        if (_disposed) return;
        ObserveRealizedItems();
        _ = ReportVisibleAsync();
    }

    private Task ReportVisibleAsync()
        => Task.WhenAll(_observations.Values.Select(observation => observation.ReportAsync()));

    private bool CanReport(FrameworkElement anchor)
        => !_disposed && _list.IsLoaded && anchor.IsLoaded && _list.XamlRoot is not null
            && ReferenceEquals(anchor.XamlRoot, _list.XamlRoot)
            && ReferenceEquals((_activation.ActiveWindow?.Content as FrameworkElement)?.XamlRoot, _list.XamlRoot)
            && _activation.IsActive && !_viewModel.IsActivationOverlayVisible && _viewModel.IsSessionActive
            && !HasOpenPopup(_list.XamlRoot);

    private bool HasOpenPopup(XamlRoot root)
    {
        var isOpen = false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (!popup.IsOpen) continue;
            isOpen = true;
            // Native popups own their visibility. Keep subscriptions only so a reply can
            // be observed again after a dialog/flyout closes without another content update.
            if (_observedPopups.Add(popup)) popup.Closed += OnPopupClosed;
        }
        return isOpen;
    }

    private void OnPopupClosed(object? sender, object args)
    {
        if (sender is Popup popup && _observedPopups.Remove(popup)) popup.Closed -= OnPopupClosed;
        // Let the native popup finish detaching, then sample visibility again.
        if (!_disposed) _list.DispatcherQueue.TryEnqueue(ReportVisible);
    }

    private sealed class Observation : IDisposable
    {
        private readonly TranscriptReadReceiptObserver _owner;
        private readonly ListViewItem _container;
        private readonly ChatMessageViewModel _message;
        private ConversationMessageSnapshot? _reported;
        private FrameworkElement? _content;
        private Task _pendingReport = Task.CompletedTask;
        private bool _awaitingContentLayout;
        private bool _layoutObservationAttached;
        private bool _disposed;

        internal Observation(TranscriptReadReceiptObserver owner, ListViewItem container, ChatMessageViewModel message)
        {
            _owner = owner;
            _container = container;
            _message = message;
            container.SizeChanged += OnSizeChanged;
            container.Loaded += OnLoaded;
            message.PropertyChanged += OnContentChanged;
        }

        public void Dispose()
        {
            _disposed = true;
            _container.SizeChanged -= OnSizeChanged;
            _container.Loaded -= OnLoaded;
            _message.PropertyChanged -= OnContentChanged;
            ObserveContent(null);
            DetachLayoutObservation();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            _awaitingContentLayout = false;
            DetachLayoutObservation();
            Report();
        }
        private void OnLoaded(object sender, RoutedEventArgs args) => Report();
        private void OnFormatted(object? sender, EventArgs args) => ObserveNextLayout();

        private void OnContentChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(ChatMessageViewModel.PresentedSnapshot)) return;
            ObserveNextLayout();
        }

        private void ObserveNextLayout()
        {
            if (_disposed) return;
            _awaitingContentLayout = true;
            if (_layoutObservationAttached) return;
            _layoutObservationAttached = true;
            _container.LayoutUpdated += OnContentLayoutUpdated;
        }

        private void OnContentLayoutUpdated(object? sender, object args)
        {
            // This event only invalidates the observation; it is not proof that content is shown.
            // ReportAsync additionally requires the native host's text/format result and geometry.
            _awaitingContentLayout = false;
            DetachLayoutObservation();
            Report();
        }

        private void DetachLayoutObservation()
        {
            if (!_layoutObservationAttached) return;
            _layoutObservationAttached = false;
            _container.LayoutUpdated -= OnContentLayoutUpdated;
        }

        internal void Report() => _ = ReportAsync();

        internal Task ReportAsync()
        {
            if (_disposed || _awaitingContentLayout || !_owner.CanReport(_container) || ResolveContentTemplateRoot(_container) is not { } root
                || _message.PresentedSnapshot is not { } snapshot || ReferenceEquals(snapshot, _reported)) return Task.CompletedTask;
            if (!_pendingReport.IsCompleted) return _pendingReport;
            FrameworkElement? content;
            if (_message.ShouldRenderMarkdown)
            {
                content = root.FindName("IncomingMarkdownPresenter") as MarkdownTextPresenter;
                if (content is not MarkdownTextPresenter markdown) return Task.CompletedTask;
                ObserveContent(markdown);
                if (!markdown.IsContentFormatted || markdown.Text != _message.DisplayBodyText) return Task.CompletedTask;
            }
            else
            {
                content = root.FindName("IncomingPlainTextBlock") as TextBlock;
                if (content is not TextBlock text) return Task.CompletedTask;
                ObserveContent(text);
                if (text.Text != _message.DisplayBodyText || text.ActualHeight <= 0) return Task.CompletedTask;
            }

            if (!IsContentEndVisible(snapshot)) return Task.CompletedTask;
            var conversationId = _owner._viewModel.CurrentSessionId;
            if (conversationId is null) return Task.CompletedTask;
            return _pendingReport = AcknowledgeAsync(conversationId, snapshot);
        }

        private void ObserveContent(FrameworkElement? content)
        {
            if (ReferenceEquals(_content, content)) return;
            if (_content is not null) _content.Loaded -= OnLoaded;
            if (_content is MarkdownTextPresenter previous) previous.ContentFormatted -= OnFormatted;
            _content = content;
            // x:Load can replace the text host while its ListViewItem stays loaded. Formatting
            // and layout may finish before the new host's Loaded event, so observe that host too.
            if (content is not null) content.Loaded += OnLoaded;
            if (content is MarkdownTextPresenter markdown) markdown.ContentFormatted += OnFormatted;
        }

        private static FrameworkElement? ResolveContentTemplateRoot(DependencyObject element)
        {
            if (element is ContentPresenter { ContentTemplateRoot: FrameworkElement root }) return root;
            if (element is ContentControl { ContentTemplateRoot: FrameworkElement controlRoot }) return controlRoot;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                // Only the native container template is inspected. Third-party Markdown content
                // is accessed through the single application-owned presenter in its namescope.
                if (ResolveContentTemplateRoot(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
            }
            return null;
        }

        private bool IsContentEndVisible(ConversationMessageSnapshot snapshot)
        {
            if (_disposed || _awaitingContentLayout || !_owner.CanReport(_container) || !ReferenceEquals(_message.PresentedSnapshot, snapshot)
                || _content is not { IsLoaded: true, Visibility: Visibility.Visible } content
                || content.ActualWidth <= 0 || content.ActualHeight <= 0
                || _owner._effectiveViewport is not { IsEmpty: false, Width: > 0, Height: > 0 } viewport) return false;
            // The stable list reports clipping from enclosing viewports. Native transforms
            // locate the content after scrolling even when its item container was recycled.
            var end = content.TransformToVisual(_owner._list).TransformPoint(new Point(0, content.ActualHeight));
            return end.Y >= Math.Max(viewport.Top, _owner._list.Padding.Top)
                && end.Y <= Math.Min(viewport.Bottom, _owner._list.ActualHeight - _owner._list.Padding.Bottom)
                && end.X < Math.Min(viewport.Right, _owner._list.ActualWidth)
                && end.X + content.ActualWidth > Math.Max(viewport.Left, 0);
        }

        private async Task AcknowledgeAsync(string conversationId, ConversationMessageSnapshot snapshot)
        {
            var acknowledged = await _owner._viewModel.AcknowledgeVisibleReplyAsync(conversationId, snapshot,
                () => IsContentEndVisible(snapshot)).ConfigureAwait(true);
            if (acknowledged && !_disposed) _reported = snapshot;
            if (!_disposed && !ReferenceEquals(_message.PresentedSnapshot, snapshot))
            {
                _container.DispatcherQueue.TryEnqueue(Report);
            }
        }
    }
}
