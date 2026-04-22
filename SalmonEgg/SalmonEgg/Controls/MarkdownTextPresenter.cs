using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.System;

#if WINDOWS
using MarkdownTextBlockControl = CommunityToolkit.WinUI.Controls.MarkdownTextBlock;
#else
using MarkdownTextBlockControl = CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock;
#endif

namespace SalmonEgg.Controls;

public sealed class MarkdownTextPresenter : Grid
{
#if WINDOWS
    private readonly MarkdownTextBlockControl _selectableMarkdown;
    private readonly MarkdownTextBlockControl _nonSelectableMarkdown;
    private MarkdownTextBlockControl _activeMarkdown;
#else
    private readonly MarkdownTextBlockControl _markdown;
#endif
    private bool _requestedIsTextSelectionEnabled;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IsTextSelectionEnabledProperty = DependencyProperty.Register(
        nameof(IsTextSelectionEnabled),
        typeof(bool),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(false, OnIsTextSelectionEnabledChanged));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnForegroundChanged));

    public static readonly DependencyProperty MessageViewModelProperty = DependencyProperty.Register(
        nameof(MessageViewModel),
        typeof(ChatMessageViewModel),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMessageViewModelChanged));

    public MarkdownTextPresenter()
    {
        _requestedIsTextSelectionEnabled = false;
#if WINDOWS
        _selectableMarkdown = CreateMarkdownBlock(isTextSelectionEnabled: true);
        _nonSelectableMarkdown = CreateMarkdownBlock(isTextSelectionEnabled: false);
        _activeMarkdown = _nonSelectableMarkdown;
        _selectableMarkdown.Visibility = Visibility.Collapsed;
        _nonSelectableMarkdown.Visibility = Visibility.Visible;
        Children.Add(_nonSelectableMarkdown);
        Children.Add(_selectableMarkdown);
#else
        _markdown = CreateMarkdownBlock();
        Children.Add(_markdown);
#endif
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty);
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public ChatMessageViewModel? MessageViewModel
    {
        get => (ChatMessageViewModel?)GetValue(MessageViewModelProperty);
        set => SetValue(MessageViewModelProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            var text = (string?)e.NewValue;
            presenter.ApplyMarkdownText(text);
        }
    }

    private static void OnIsTextSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._requestedIsTextSelectionEnabled = (bool)e.NewValue;
#if WINDOWS
            presenter.ApplyMarkdownText(presenter.Text);
#else
            presenter.ApplyTextSelectionMode(presenter.Text);
#endif
        }
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
#if WINDOWS
            presenter._selectableMarkdown.Foreground = e.NewValue as Brush;
            presenter._nonSelectableMarkdown.Foreground = e.NewValue as Brush;
#else
            presenter._markdown.Foreground = e.NewValue as Brush;
#endif
        }
    }

    private static void OnMessageViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter.ApplyMarkdownText(presenter.Text);
        }
    }

    private void ApplyMarkdownText(string? value)
    {
        if (MessageViewModel is { ShouldRenderMarkdown: false })
        {
            ClearMarkdownText();
            return;
        }

        var text = value ?? string.Empty;

        try
        {
#if WINDOWS
            var target = ResolveMarkdownTarget(text);
            target.Text = text;
            ClearInactiveMarkdownText(target);
#else
            _markdown.Text = text;
#endif
        }
        catch
        {
            MessageViewModel?.MarkMarkdownRenderFailed();
            ClearMarkdownText();
        }
    }

    private void ApplyTextSelectionMode(string? text)
    {
#if WINDOWS
        _ = text;
#else
        _markdown.IsTextSelectionEnabled = _requestedIsTextSelectionEnabled;
#endif
    }

    private void ClearMarkdownText()
    {
#if WINDOWS
        _selectableMarkdown.Text = string.Empty;
        _nonSelectableMarkdown.Text = string.Empty;
#else
        _markdown.Text = string.Empty;
#endif
    }

#if WINDOWS
    private MarkdownTextBlockControl CreateMarkdownBlock(bool isTextSelectionEnabled)
    {
        var markdown = new MarkdownTextBlockControl
        {
            UsePipeTables = true,
            UseTaskLists = true,
            UseEmphasisExtras = true,
            UseAutoLinks = true,
            DisableLinks = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSelectionEnabled = isTextSelectionEnabled
        };
        markdown.OnLinkClicked += OnWindowsLinkClicked;
        return markdown;
    }

    private MarkdownTextBlockControl ResolveMarkdownTarget(string? text)
    {
        var target = ShouldUseSelectableMarkdown(text)
            ? _selectableMarkdown
            : _nonSelectableMarkdown;

        if (!ReferenceEquals(_activeMarkdown, target))
        {
            _activeMarkdown.Visibility = Visibility.Collapsed;
            target.Visibility = Visibility.Visible;
            _activeMarkdown = target;
        }

        return target;
    }

    private void ClearInactiveMarkdownText(MarkdownTextBlockControl active)
    {
        var inactive = ReferenceEquals(active, _selectableMarkdown)
            ? _nonSelectableMarkdown
            : _selectableMarkdown;
        inactive.Text = string.Empty;
        inactive.Visibility = Visibility.Collapsed;
        active.Visibility = Visibility.Visible;
    }

    private bool ShouldUseSelectableMarkdown(string? text)
    {
        // Keep native selection available for ordinary markdown, but avoid mutating
        // RichTextBlock selection mode on a live control after chat re-entry.
        return _requestedIsTextSelectionEnabled && !ContainsClosedCodeFence(text);
    }
#else
    private MarkdownTextBlockControl CreateMarkdownBlock()
    {
        var markdown = new MarkdownTextBlockControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        markdown.LinkClicked += OnUnoLinkClicked;
        return markdown;
    }
#endif

    private static bool ContainsClosedCodeFence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan();
        const string fence = "```";
        var fenceCount = 0;
        var index = 0;

        while (index <= span.Length - fence.Length)
        {
            if (span.Slice(index, fence.Length).SequenceEqual(fence.AsSpan()))
            {
                fenceCount++;
                index += fence.Length;
                continue;
            }

            index++;
        }

        return fenceCount >= 2;
    }

#if WINDOWS
    private async void OnWindowsLinkClicked(object? sender, CommunityToolkit.WinUI.Controls.LinkClickedEventArgs e)
    {
        if (e.Uri is null)
        {
            return;
        }

        e.Handled = true;
        await TryLaunchUriAsync(e.Uri);
    }
#else
    private async void OnUnoLinkClicked(object? sender, CommunityToolkit.WinUI.UI.Controls.LinkClickedEventArgs e)
    {
        if (!Uri.TryCreate(e.Link, UriKind.Absolute, out var uri) || uri is null)
        {
            return;
        }

        await TryLaunchUriAsync(uri);
    }
#endif

    private static async Task TryLaunchUriAsync(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // Keep chat rendering resilient when host platform cannot open the URI.
        }
    }
}
