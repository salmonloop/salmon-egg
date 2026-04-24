using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
namespace SalmonEgg.Controls;

public sealed partial class XtermTerminalView : UserControl
{
    private const string TerminalHostName = "salmon-terminal";

    private bool _isReady;
    private bool _isInitialized;
    private string _pendingContent = string.Empty;

    public XtermTerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string ContentText
    {
        get => (string)GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    public static readonly DependencyProperty ContentTextProperty =
        DependencyProperty.Register(
            nameof(ContentText),
            typeof(string),
            typeof(XtermTerminalView),
            new PropertyMetadata(string.Empty, OnContentTextChanged));

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeTerminalAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
    }

    private static void OnContentTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not XtermTerminalView view)
        {
            return;
        }

        view._pendingContent = e.NewValue as string ?? string.Empty;
        _ = view.SetTerminalContentAsync(view._pendingContent);
    }

    private async Task InitializeTerminalAsync()
    {
        if (_isInitialized)
        {
            await SetTerminalContentAsync(_pendingContent).ConfigureAwait(true);
            return;
        }

        try
        {
            await TerminalWebView.EnsureCoreWebView2Async();
            if (TerminalWebView.CoreWebView2 == null)
            {
                ShowFallback();
                return;
            }

            var terminalAssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal");
            TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                TerminalHostName,
                terminalAssetsPath,
                CoreWebView2HostResourceAccessKind.Allow);
            TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
            TerminalWebView.WebMessageReceived += OnWebMessageReceived;
            TerminalWebView.Source = new Uri($"https://{TerminalHostName}/xterm-host.html");
            _isInitialized = true;
        }
        catch
        {
            ShowFallback();
        }
    }

    private void OnWebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            if (document.RootElement.TryGetProperty("kind", out var kind)
                && string.Equals(kind.GetString(), "ready", StringComparison.Ordinal))
            {
                _isReady = true;
                _ = SetTerminalContentAsync(_pendingContent);
            }
        }
        catch
        {
            ShowFallback();
        }
    }

    private async Task SetTerminalContentAsync(string text)
    {
        if (!_isReady || TerminalWebView.CoreWebView2 == null)
        {
            return;
        }

        var payload = "\"" + JavaScriptEncoder.Default.Encode(text ?? string.Empty) + "\"";
        await TerminalWebView.ExecuteScriptAsync(
            $"window.salmonTerminal && window.salmonTerminal.setContent({payload});");
    }

    private void ShowFallback()
    {
        _isReady = false;
        TerminalWebView.Visibility = Visibility.Collapsed;
        FallbackText.Visibility = Visibility.Visible;
    }
}
