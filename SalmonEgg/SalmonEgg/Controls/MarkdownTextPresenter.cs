using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.System;

namespace SalmonEgg.Controls;

public sealed class MarkdownTextPresenter : Grid
{
#if WINDOWS
    private readonly CommunityToolkit.WinUI.Controls.MarkdownTextBlock _markdown = new();
#else
    private readonly CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock _markdown = new();
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
#if WINDOWS
        _markdown.UsePipeTables = true;
        _markdown.UseTaskLists = true;
        _markdown.UseEmphasisExtras = true;
        _markdown.UseAutoLinks = true;
        _markdown.DisableLinks = false;
        _markdown.OnLinkClicked += OnWindowsLinkClicked;
#else
        _markdown.LinkClicked += OnUnoLinkClicked;
#endif
        _markdown.HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(_markdown);
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
            presenter.ApplyTextSelectionMode(text);
        }
    }

    private static void OnIsTextSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._requestedIsTextSelectionEnabled = (bool)e.NewValue;
            presenter.ApplyTextSelectionMode(presenter.Text);
        }
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._markdown.Foreground = e.NewValue as Brush;
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
            _markdown.Text = string.Empty;
            return;
        }

        try
        {
            _markdown.Text = value ?? string.Empty;
        }
        catch
        {
            MessageViewModel?.MarkMarkdownRenderFailed();
            _markdown.Text = string.Empty;
        }
    }

    private void ApplyTextSelectionMode(string? text)
    {
#if WINDOWS
        // CommunityToolkit.Labs MarkdownTextBlock can crash on WinUI when fenced code blocks
        // receive word-selection gestures (double click). Keep selection enabled for plain markdown.
        var shouldDisableForCodeFence = ContainsClosedCodeFence(text);
        _markdown.IsTextSelectionEnabled = _requestedIsTextSelectionEnabled && !shouldDisableForCodeFence;
#else
        _markdown.IsTextSelectionEnabled = _requestedIsTextSelectionEnabled;
#endif
    }

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
