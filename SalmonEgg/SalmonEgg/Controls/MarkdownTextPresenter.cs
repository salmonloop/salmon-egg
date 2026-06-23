using System.Windows.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using SalmonEgg.Presentation.ViewModels.Chat;

#if WINDOWS
using CommunityToolkit.WinUI.Controls;
using MarkdownTextBlockControl = CommunityToolkit.WinUI.Controls.MarkdownTextBlock;
#else
using MarkdownTextBlockControl = CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock;
#endif

namespace SalmonEgg.Controls;

public sealed partial class MarkdownTextPresenter : Grid
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
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty LinkForegroundProperty = DependencyProperty.Register(
        nameof(LinkForeground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty CodeBlockBackgroundProperty = DependencyProperty.Register(
        nameof(CodeBlockBackground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty CodeBlockForegroundProperty = DependencyProperty.Register(
        nameof(CodeBlockForeground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty CodeBlockBorderBrushProperty = DependencyProperty.Register(
        nameof(CodeBlockBorderBrush),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty InlineCodeBackgroundProperty = DependencyProperty.Register(
        nameof(InlineCodeBackground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty InlineCodeForegroundProperty = DependencyProperty.Register(
        nameof(InlineCodeForeground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty InlineCodeBorderBrushProperty = DependencyProperty.Register(
        nameof(InlineCodeBorderBrush),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty QuoteBackgroundProperty = DependencyProperty.Register(
        nameof(QuoteBackground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty QuoteForegroundProperty = DependencyProperty.Register(
        nameof(QuoteForeground),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty QuoteBorderBrushProperty = DependencyProperty.Register(
        nameof(QuoteBorderBrush),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty TableBorderBrushProperty = DependencyProperty.Register(
        nameof(TableBorderBrush),
        typeof(Brush),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null, OnMarkdownTypographyChanged));

    public static readonly DependencyProperty ShouldRenderMarkdownProperty = DependencyProperty.Register(
        nameof(ShouldRenderMarkdown),
        typeof(bool),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(true, OnShouldRenderMarkdownChanged));

    public static readonly DependencyProperty RenderFailureSinkProperty = DependencyProperty.Register(
        nameof(RenderFailureSink),
        typeof(IRenderFailureSink),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null));

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand),
        typeof(ICommand),
        typeof(MarkdownTextPresenter),
        new PropertyMetadata(null));

    public MarkdownTextPresenter()
    {
        _requestedIsTextSelectionEnabled = false;
#if WINDOWS
        _selectableMarkdown = CreateMarkdownBlock(isTextSelectionEnabled: true);
        _nonSelectableMarkdown = CreateMarkdownBlock(isTextSelectionEnabled: false);
        _activeMarkdown = _nonSelectableMarkdown;
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

    // On Android, View.Foreground exists → new is required (CS0114 without it).
    // On desktop/wasm, View.Foreground is absent → new triggers CS0109.
    // Suppress CS0109 to keep all TFMs warning-free.
#pragma warning disable CS0109
    public new Brush? Foreground
#pragma warning restore CS0109
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? LinkForeground
    {
        get => (Brush?)GetValue(LinkForegroundProperty);
        set => SetValue(LinkForegroundProperty, value);
    }

    public Brush? CodeBlockBackground
    {
        get => (Brush?)GetValue(CodeBlockBackgroundProperty);
        set => SetValue(CodeBlockBackgroundProperty, value);
    }

    public Brush? CodeBlockForeground
    {
        get => (Brush?)GetValue(CodeBlockForegroundProperty);
        set => SetValue(CodeBlockForegroundProperty, value);
    }

    public Brush? CodeBlockBorderBrush
    {
        get => (Brush?)GetValue(CodeBlockBorderBrushProperty);
        set => SetValue(CodeBlockBorderBrushProperty, value);
    }

    public Brush? InlineCodeBackground
    {
        get => (Brush?)GetValue(InlineCodeBackgroundProperty);
        set => SetValue(InlineCodeBackgroundProperty, value);
    }

    public Brush? InlineCodeForeground
    {
        get => (Brush?)GetValue(InlineCodeForegroundProperty);
        set => SetValue(InlineCodeForegroundProperty, value);
    }

    public Brush? InlineCodeBorderBrush
    {
        get => (Brush?)GetValue(InlineCodeBorderBrushProperty);
        set => SetValue(InlineCodeBorderBrushProperty, value);
    }

    public Brush? QuoteBackground
    {
        get => (Brush?)GetValue(QuoteBackgroundProperty);
        set => SetValue(QuoteBackgroundProperty, value);
    }

    public Brush? QuoteForeground
    {
        get => (Brush?)GetValue(QuoteForegroundProperty);
        set => SetValue(QuoteForegroundProperty, value);
    }

    public Brush? QuoteBorderBrush
    {
        get => (Brush?)GetValue(QuoteBorderBrushProperty);
        set => SetValue(QuoteBorderBrushProperty, value);
    }

    public Brush? TableBorderBrush
    {
        get => (Brush?)GetValue(TableBorderBrushProperty);
        set => SetValue(TableBorderBrushProperty, value);
    }

    public bool ShouldRenderMarkdown
    {
        get => (bool)GetValue(ShouldRenderMarkdownProperty);
        set => SetValue(ShouldRenderMarkdownProperty, value);
    }

    public IRenderFailureSink? RenderFailureSink
    {
        get => (IRenderFailureSink?)GetValue(RenderFailureSinkProperty);
        set => SetValue(RenderFailureSinkProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
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

    private static void OnMarkdownTypographyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter.ApplyMarkdownTypography();
        }
    }

    private static void OnShouldRenderMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter.ApplyMarkdownText(presenter.Text);
        }
    }

    private void ApplyMarkdownText(string? value)
    {
        if (!ShouldRenderMarkdown)
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
            RenderFailureSink?.MarkRenderFailed();
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
            UseListExtras = true,
            UseTaskLists = true,
            UseEmphasisExtras = true,
            UseAutoLinks = !isTextSelectionEnabled,
            DisableLinks = isTextSelectionEnabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSelectionEnabled = isTextSelectionEnabled
        };
        markdown.OnLinkClicked += OnWindowsLinkClicked;
        ApplyMarkdownTypography(markdown);
        return markdown;
    }

    private MarkdownTextBlockControl ResolveMarkdownTarget(string? text)
    {
        var target = ShouldUseSelectableMarkdown()
            ? _selectableMarkdown
            : _nonSelectableMarkdown;

        if (!ReferenceEquals(_activeMarkdown, target))
        {
            DetachMarkdown(_activeMarkdown);
            AttachMarkdown(target);
            _activeMarkdown = target;
        }
        else if (Children.Count == 0)
        {
            AttachMarkdown(target);
        }

        return target;
    }

    private void ClearInactiveMarkdownText(MarkdownTextBlockControl active)
    {
        var inactive = ReferenceEquals(active, _selectableMarkdown)
            ? _nonSelectableMarkdown
            : _selectableMarkdown;
        inactive.Text = string.Empty;
        DetachMarkdown(inactive);
        AttachMarkdown(active);
    }

    private bool ShouldUseSelectableMarkdown()
    {
        return _requestedIsTextSelectionEnabled;
    }

    private void AttachMarkdown(MarkdownTextBlockControl markdown)
    {
        if (!Children.Contains(markdown))
        {
            Children.Add(markdown);
        }

        markdown.Visibility = Visibility.Visible;
    }

    private void DetachMarkdown(MarkdownTextBlockControl markdown)
    {
        markdown.Visibility = Visibility.Collapsed;
        _ = Children.Remove(markdown);
    }
#else
    private MarkdownTextBlockControl CreateMarkdownBlock()
    {
        var markdown = new MarkdownTextBlockControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSelectionEnabled = _requestedIsTextSelectionEnabled
        };
        markdown.LinkClicked += OnUnoLinkClicked;
        ApplyMarkdownTypography(markdown);
        return markdown;
    }
#endif

    private void ApplyMarkdownTypography()
    {
#if WINDOWS
        ApplyMarkdownTypography(_selectableMarkdown);
        ApplyMarkdownTypography(_nonSelectableMarkdown);
#else
        ApplyMarkdownTypography(_markdown);
#endif
    }

    private void ApplyMarkdownTypography(MarkdownTextBlockControl markdown)
    {
#if WINDOWS
        var foreground = Foreground ?? new SolidColorBrush(Colors.Transparent);
        var secondaryForeground = QuoteForeground ?? foreground;
        var linkForeground = LinkForeground ?? foreground;
        var codeBlockBackground = CodeBlockBackground ?? new SolidColorBrush(Colors.Transparent);
        var codeBlockBorderBrush = CodeBlockBorderBrush ?? new SolidColorBrush(Colors.Transparent);
        var inlineCodeBackground = InlineCodeBackground ?? codeBlockBackground;
        var inlineCodeForeground = InlineCodeForeground ?? foreground;
        var inlineCodeBorderBrush = InlineCodeBorderBrush ?? codeBlockBorderBrush;
        var codeBlockForeground = CodeBlockForeground ?? foreground;
        var quoteBackground = QuoteBackground ?? new SolidColorBrush(Colors.Transparent);
        var quoteBorderBrush = QuoteBorderBrush ?? codeBlockBorderBrush;
        var tableBorderBrush = TableBorderBrush ?? codeBlockBorderBrush;

        markdown.Foreground = foreground;
        markdown.Config = new MarkdownConfig
        {
            Themes = new MarkdownThemes
            {
                Padding = new Thickness(0),
                InternalMargin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                ParagraphMargin = new Thickness(0, 0, 0, 8),
                ParagraphLineHeight = 22,
                H1FontSize = 20,
                H2FontSize = 18,
                H3FontSize = 16,
                H4FontSize = 15,
                H5FontSize = 14,
                H6FontSize = 14,
                H1FontWeight = FontWeights.SemiBold,
                H2FontWeight = FontWeights.SemiBold,
                H3FontWeight = FontWeights.SemiBold,
                H4FontWeight = FontWeights.SemiBold,
                H5FontWeight = FontWeights.SemiBold,
                H6FontWeight = FontWeights.SemiBold,
                H1Foreground = foreground,
                H2Foreground = foreground,
                H3Foreground = foreground,
                H4Foreground = foreground,
                H5Foreground = foreground,
                H6Foreground = foreground,
                H1Margin = new Thickness(0, 0, 0, 10),
                H2Margin = new Thickness(0, 8, 0, 8),
                H3Margin = new Thickness(0, 8, 0, 6),
                H4Margin = new Thickness(0, 6, 0, 6),
                H5Margin = new Thickness(0, 6, 0, 4),
                H6Margin = new Thickness(0, 6, 0, 4),
                LinkForeground = linkForeground,
                BorderBrush = codeBlockBorderBrush,
                InlineCodeBackground = inlineCodeBackground,
                InlineCodeForeground = inlineCodeForeground,
                InlineCodeBorderBrush = inlineCodeBorderBrush,
                InlineCodeBorderThickness = new Thickness(1),
                InlineCodeCornerRadius = new CornerRadius(4),
                InlineCodePadding = new Thickness(3, 0, 3, 1),
                InlineCodeFontSize = 13,
                InlineCodeFontWeight = FontWeights.Normal,
                CodeBlockBackground = codeBlockBackground,
                CodeBlockForeground = codeBlockForeground,
                CodeBlockBorderBrush = codeBlockBorderBrush,
                CodeBlockBorderThickness = new Thickness(1),
                CodeBlockCornerRadius = new CornerRadius(6),
                CodeBlockPadding = new Thickness(10, 8, 10, 8),
                CodeBlockMargin = new Thickness(0, 6, 0, 10),
                QuoteBackground = quoteBackground,
                QuoteForeground = secondaryForeground,
                QuoteBorderBrush = quoteBorderBrush,
                QuoteBorderThickness = new Thickness(3, 0, 0, 0),
                QuoteCornerRadius = new CornerRadius(4),
                QuotePadding = new Thickness(12, 6, 10, 6),
                QuoteMargin = new Thickness(0, 6, 0, 10),
                HorizontalRuleBrush = tableBorderBrush,
                HorizontalRuleThickness = 1,
                HorizontalRuleMargin = new Thickness(0, 12, 0, 12),
                TableBorderBrush = tableBorderBrush,
                TableBorderThickness = 1,
                TableCellPadding = new Thickness(8, 5, 8, 5),
                TableMargin = new Thickness(0, 6, 0, 10),
                TableHeadingBackground = inlineCodeBackground
            }
        };
#else
        markdown.Foreground = Foreground;
        markdown.LinkForeground = LinkForeground;
        markdown.CodeBackground = CodeBlockBackground;
        markdown.CodeForeground = CodeBlockForeground ?? Foreground;
        markdown.CodeBorderBrush = CodeBlockBorderBrush;
        markdown.CodeBorderThickness = new Thickness(1);
        markdown.CodePadding = new Thickness(10, 8, 10, 8);
        markdown.CodeMargin = new Thickness(0, 6, 0, 10);
        markdown.InlineCodeBackground = InlineCodeBackground;
        markdown.InlineCodeForeground = InlineCodeForeground ?? Foreground;
        markdown.InlineCodeBorderBrush = InlineCodeBorderBrush ?? CodeBlockBorderBrush;
        markdown.InlineCodeBorderThickness = new Thickness(1);
        markdown.InlineCodePadding = new Thickness(3, 0, 3, 1);
        markdown.Header1FontSize = 20;
        markdown.Header2FontSize = 18;
        markdown.Header3FontSize = 16;
        markdown.Header4FontSize = 15;
        markdown.Header5FontSize = 14;
        markdown.Header6FontSize = 14;
        markdown.Header1FontWeight = FontWeights.SemiBold;
        markdown.Header2FontWeight = FontWeights.SemiBold;
        markdown.Header3FontWeight = FontWeights.SemiBold;
        markdown.Header4FontWeight = FontWeights.SemiBold;
        markdown.Header5FontWeight = FontWeights.SemiBold;
        markdown.Header6FontWeight = FontWeights.SemiBold;
        markdown.Header1Foreground = Foreground;
        markdown.Header2Foreground = Foreground;
        markdown.Header3Foreground = Foreground;
        markdown.Header4Foreground = Foreground;
        markdown.Header5Foreground = Foreground;
        markdown.Header6Foreground = Foreground;
        markdown.Header1Margin = new Thickness(0, 0, 0, 10);
        markdown.Header2Margin = new Thickness(0, 8, 0, 8);
        markdown.Header3Margin = new Thickness(0, 8, 0, 6);
        markdown.Header4Margin = new Thickness(0, 6, 0, 6);
        markdown.Header5Margin = new Thickness(0, 6, 0, 4);
        markdown.Header6Margin = new Thickness(0, 6, 0, 4);
        markdown.QuoteBackground = QuoteBackground;
        markdown.QuoteForeground = QuoteForeground ?? Foreground;
        markdown.QuoteBorderBrush = QuoteBorderBrush;
        markdown.QuoteBorderThickness = new Thickness(3, 0, 0, 0);
        markdown.QuotePadding = new Thickness(12, 6, 10, 6);
        markdown.QuoteMargin = new Thickness(0, 6, 0, 10);
        markdown.TableBorderBrush = TableBorderBrush ?? CodeBlockBorderBrush;
        markdown.TableBorderThickness = 1;
        markdown.TableCellPadding = new Thickness(8, 5, 8, 5);
        markdown.TableMargin = new Thickness(0, 6, 0, 10);
        markdown.HorizontalRuleBrush = TableBorderBrush ?? CodeBlockBorderBrush;
#endif
    }

#if WINDOWS
    private void OnWindowsLinkClicked(object? sender, CommunityToolkit.WinUI.Controls.LinkClickedEventArgs e)
    {
        if (!TryExecuteLinkCommand(e.Uri?.AbsoluteUri))
        {
            return;
        }

        e.Handled = true;
    }
#else
    private void OnUnoLinkClicked(object? sender, CommunityToolkit.WinUI.UI.Controls.LinkClickedEventArgs e)
    {
        _ = TryExecuteLinkCommand(e.Link);
    }
#endif

    private bool TryExecuteLinkCommand(string? rawLink)
    {
        var command = LinkCommand;
        if (command?.CanExecute(rawLink) != true)
        {
            return false;
        }

        command.Execute(rawLink);
        return true;
    }
}
