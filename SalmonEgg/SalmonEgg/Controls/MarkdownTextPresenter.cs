using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SalmonEgg.Controls;

public sealed class MarkdownTextPresenter : Grid
{
#if WINDOWS
    private readonly CommunityToolkit.WinUI.Controls.MarkdownTextBlock _markdown = new();
#else
    private readonly CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock _markdown = new();
#endif

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

    public MarkdownTextPresenter()
    {
#if WINDOWS
        _markdown.UsePipeTables = true;
        _markdown.UseTaskLists = true;
        _markdown.UseEmphasisExtras = true;
        _markdown.UseAutoLinks = true;
        _markdown.DisableLinks = false;
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

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._markdown.Text = (string?)e.NewValue ?? string.Empty;
        }
    }

    private static void OnIsTextSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._markdown.IsTextSelectionEnabled = (bool)e.NewValue;
        }
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._markdown.Foreground = e.NewValue as Brush;
        }
    }
}
