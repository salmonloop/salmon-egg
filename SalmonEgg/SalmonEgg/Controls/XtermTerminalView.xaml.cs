using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Controls;

public sealed partial class XtermTerminalView : UserControl
{
    private const string TerminalHostName = "salmon-terminal";
    private const string GuiLocalTerminalSmokeCommandEnvVar = "SALMONEGG_GUI_LOCAL_TERMINAL_SMOKE_COMMAND";

    public static readonly DependencyProperty ContentTextProperty =
        DependencyProperty.Register(
            nameof(ContentText),
            typeof(string),
            typeof(XtermTerminalView),
            new PropertyMetadata(string.Empty, OnContentTextChanged));

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(ILocalTerminalSession),
            typeof(XtermTerminalView),
            new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty RenderedTextProperty =
        DependencyProperty.Register(
            nameof(RenderedText),
            typeof(string),
            typeof(XtermTerminalView),
            new PropertyMetadata(string.Empty));

    private readonly Queue<TerminalHostCommand> _pendingCommands = new();
    private bool _isReady;
    private bool _isInitialized;
    private bool _isViewActive;
    private bool _sessionHasLiveOutput;
    private int _lastViewportHeight;
    private int _lastViewportWidth;
    private int _sessionGeneration;
    private string _currentHostId = CreateHostId();
    private string _pendingContent = string.Empty;
    private ILocalTerminalSession? _attachedSession;
    private bool _guiLocalTerminalSmokeCommandSent;
    private string? _guiLocalTerminalSmokeCommand;

    public XtermTerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public string ContentText
    {
        get => (string)GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    public ILocalTerminalSession? Session
    {
        get => (ILocalTerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public string RenderedText
    {
        get => (string)GetValue(RenderedTextProperty);
        private set => SetValue(RenderedTextProperty, value);
    }

    private string EffectiveTransportMode =>
        Session?.TransportMode == LocalTerminalTransportMode.PseudoConsole
            ? "pseudoConsole"
            : "pipe";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isViewActive = true;

        try
        {
            if (_attachedSession is null && Session is not null)
            {
                AttachSession(Session);
            }

            await InitializeTerminalAsync();
            await FlushPendingCommandsAsync();
            await SyncSessionStateAsync();
        }
        catch
        {
            ShowFallback();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isViewActive = false;
        DetachWebViewEvents();
        DetachSession(_attachedSession);
    }

    private static void OnContentTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not XtermTerminalView view)
        {
            return;
        }

        view._pendingContent = e.NewValue as string ?? string.Empty;
        if (view.Session is not null && view._sessionHasLiveOutput)
        {
            return;
        }

        view.ReplaceRenderedText(view._pendingContent);
        _ = view.ReplaceTerminalContentAsync(view._pendingContent, view._sessionGeneration);
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not XtermTerminalView view)
        {
            return;
        }

        view._sessionGeneration++;
        view.DetachSession(e.OldValue as ILocalTerminalSession);
        view.AttachSession(e.NewValue as ILocalTerminalSession);
        view._sessionHasLiveOutput = false;
        view.ClearPendingCommands(static _ => true);
        view._guiLocalTerminalSmokeCommandSent = false;
        view._guiLocalTerminalSmokeCommand = Environment.GetEnvironmentVariable(GuiLocalTerminalSmokeCommandEnvVar);
        _ = view.ResetTerminalBridgeAsync();
    }

    private async Task InitializeTerminalAsync()
    {
        if (_isInitialized)
        {
            AttachWebViewEvents();
            await ReplaceTerminalContentAsync(_pendingContent, _sessionGeneration);
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
            TerminalWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            AttachWebViewEvents();
            NavigateToCurrentHost();
            _isInitialized = true;
        }
        catch
        {
            ShowFallback();
        }
    }

    private void AttachWebViewEvents()
    {
        TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
        TerminalWebView.WebMessageReceived += OnWebMessageReceived;
        TerminalWebView.NavigationCompleted -= OnNavigationCompleted;
        TerminalWebView.NavigationCompleted += OnNavigationCompleted;
    }

    private void DetachWebViewEvents()
    {
        TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
        TerminalWebView.NavigationCompleted -= OnNavigationCompleted;
    }

    private void AttachSession(ILocalTerminalSession? session)
    {
        if (session is null)
        {
            _attachedSession = null;
            return;
        }

        _attachedSession = session;
        session.OutputReceived += OnSessionOutputReceived;
        session.StateChanged += OnSessionStateChanged;
    }

    private void DetachSession(ILocalTerminalSession? session)
    {
        if (session is null)
        {
            return;
        }

        session.OutputReceived -= OnSessionOutputReceived;
        session.StateChanged -= OnSessionStateChanged;
        if (ReferenceEquals(_attachedSession, session))
        {
            _attachedSession = null;
        }
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ShowFallback();
        }
    }

    private void OnWebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        _ = DispatchWebMessageAsync(args.WebMessageAsJson);
    }

    private async Task DispatchWebMessageAsync(string messageJson)
    {
        JsonDocument? document = null;

        try
        {
            document = TryParseMessageDocument(messageJson);
            if (document is null)
            {
                return;
            }

            var root = document.RootElement;
            if (!TryGetString(root, "kind", out var kind))
            {
                return;
            }

            if (!TryGetString(root, "hostId", out var hostId)
                || !string.Equals(hostId, _currentHostId, StringComparison.Ordinal))
            {
                return;
            }

            switch (kind)
            {
                case "ready":
                    await HandleReadyMessageAsync(root);
                    break;
                case "input":
                    await HandleInputMessageAsync(root);
                    break;
                case "resize":
                    await HandleResizeMessageAsync(root);
                    break;
                case "error":
                    HandleErrorMessage();
                    break;
            }
        }
        finally
        {
            document?.Dispose();
        }
    }

    private async Task HandleReadyMessageAsync(JsonElement root)
    {
        _isReady = true;
        ShowTerminal();
        await FlushPendingCommandsAsync();
        await SyncSessionStateAsync();
        await HandleResizeMessageAsync(root);
    }

    private void HandleErrorMessage()
    {
        ShowFallback();
    }

    private async Task HandleInputMessageAsync(JsonElement root)
    {
        if (Session is null || !Session.CanAcceptInput || !TryGetString(root, "data", out var input) || string.IsNullOrEmpty(input))
        {
            return;
        }

        try
        {
            await Session.WriteInputAsync(input);
        }
        catch
        {
        }
    }

    private async Task HandleResizeMessageAsync(JsonElement root)
    {
        if (Session is null
            || !TryGetInt32(root, "cols", out var columns)
            || !TryGetInt32(root, "rows", out var rows)
            || columns <= 0
            || rows <= 0)
        {
            return;
        }

        try
        {
            await Session.ResizeAsync(columns, rows);
        }
        catch
        {
        }
    }

    private void OnSessionOutputReceived(object? sender, string output)
    {
        if (sender is not ILocalTerminalSession session || string.IsNullOrEmpty(output))
        {
            return;
        }

        _ = RunOnUiThreadAsync(() => AppendSessionOutputAsync(session, output));
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (sender is not ILocalTerminalSession session)
        {
            return;
        }

        _ = RunOnUiThreadAsync(() => SyncSessionStateAsync(session));
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = QueueHostResizeAsync();
    }

    private Task AppendSessionOutputAsync(ILocalTerminalSession session, string output)
    {
        if (!ReferenceEquals(session, Session))
        {
            return Task.CompletedTask;
        }

        _sessionHasLiveOutput = true;
        AppendRenderedText(output);
        return QueueTerminalWriteAsync(output, TerminalOutputChannel.Stdout, _sessionGeneration);
    }

    private async Task ResetTerminalBridgeAsync()
    {
        ReplaceRenderedText(_pendingContent);

        if (Session is not null && !string.IsNullOrEmpty(_pendingContent))
        {
            await ReplaceTerminalContentAsync(_pendingContent, _sessionGeneration);
            await SyncSessionStateAsync();
            return;
        }

        if (Session is null)
        {
            await ReplaceTerminalContentAsync(_pendingContent, _sessionGeneration);
            await SyncSessionStateAsync();
            return;
        }

        ReplaceRenderedText(string.Empty);
        await ClearTerminalAsync(_sessionGeneration);
        await SyncSessionStateAsync();
    }

    private Task ReplaceTerminalContentAsync(string text, int sessionGeneration)
    {
        ClearPendingCommands(static command =>
            string.Equals(command.Message.Kind, "replace", StringComparison.Ordinal)
            || string.Equals(command.Message.Kind, "clear", StringComparison.Ordinal)
            || string.Equals(command.Message.Kind, "stdout", StringComparison.Ordinal)
            || string.Equals(command.Message.Kind, "stderr", StringComparison.Ordinal));

        return SendHostCommandAsync(
            CreateHostCommand(
                sessionGeneration,
                new TerminalHostMessage
                {
                    Kind = "replace",
                    HostId = _currentHostId,
                    TransportMode = EffectiveTransportMode,
                    Text = text ?? string.Empty
                }));
    }

    private Task QueueTerminalWriteAsync(string text, TerminalOutputChannel channel, int sessionGeneration)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        return SendHostCommandAsync(
            CreateHostCommand(
                sessionGeneration,
                new TerminalHostMessage
                {
                    Kind = channel == TerminalOutputChannel.Stdout ? "stdout" : "stderr",
                    HostId = _currentHostId,
                    TransportMode = EffectiveTransportMode,
                    Text = text
                }));
    }

    private Task ClearTerminalAsync(int sessionGeneration)
    {
        ClearPendingCommands(static command =>
            string.Equals(command.Message.Kind, "replace", StringComparison.Ordinal)
            || string.Equals(command.Message.Kind, "clear", StringComparison.Ordinal));

        return SendHostCommandAsync(
            CreateHostCommand(
                sessionGeneration,
                new TerminalHostMessage
                {
                    Kind = "clear",
                    HostId = _currentHostId,
                    TransportMode = EffectiveTransportMode
                }));
    }

    private Task SyncSessionStateAsync()
    {
        return SyncSessionStateAsync(Session);
    }

    private async Task SyncSessionStateAsync(ILocalTerminalSession? session)
    {
        if (session is not null && !ReferenceEquals(session, Session))
        {
            return;
        }

        ClearPendingCommands(static command =>
            string.Equals(command.Message.Kind, "setInputEnabled", StringComparison.Ordinal));

        await SendHostCommandAsync(
            CreateHostCommand(
                _sessionGeneration,
                new TerminalHostMessage
                {
                    Kind = "setInputEnabled",
                    HostId = _currentHostId,
                    TransportMode = EffectiveTransportMode,
                    Enabled = Session is not null && Session.CanAcceptInput
                }));
        await QueueHostResizeAsync();
        await TryInjectGuiSmokeCommandAsync(session);
    }

    private async Task TryInjectGuiSmokeCommandAsync(ILocalTerminalSession? session)
    {
        if (_guiLocalTerminalSmokeCommandSent
            || session is null
            || !ReferenceEquals(session, Session)
            || !session.CanAcceptInput
            || string.IsNullOrWhiteSpace(_guiLocalTerminalSmokeCommand))
        {
            return;
        }

        try
        {
            await session.WriteInputAsync(_guiLocalTerminalSmokeCommand);
            _guiLocalTerminalSmokeCommandSent = true;
        }
        catch
        {
        }
    }

    private Task QueueHostResizeAsync()
    {
        var width = Math.Max((int)Math.Round(ActualWidth), 0);
        var height = Math.Max((int)Math.Round(ActualHeight), 0);
        if (width <= 0 || height <= 0)
        {
            return Task.CompletedTask;
        }

        if (width == _lastViewportWidth && height == _lastViewportHeight)
        {
            return Task.CompletedTask;
        }

        _lastViewportWidth = width;
        _lastViewportHeight = height;

        ClearPendingCommands(static command =>
            string.Equals(command.Message.Kind, "hostSize", StringComparison.Ordinal));

        return SendHostCommandAsync(
            CreateHostCommand(
                _sessionGeneration,
                new TerminalHostMessage
                {
                    Kind = "hostSize",
                    HostId = _currentHostId,
                    TransportMode = EffectiveTransportMode,
                    Width = width,
                    Height = height
                }));
    }

    private TerminalHostCommand CreateHostCommand(int sessionGeneration, TerminalHostMessage message)
    {
        return new TerminalHostCommand(sessionGeneration, message);
    }

    private async Task SendHostCommandAsync(TerminalHostCommand command)
    {
        if (!IsCommandCurrent(command))
        {
            return;
        }

        if (!_isViewActive || !_isReady || TerminalWebView.CoreWebView2 == null)
        {
            _pendingCommands.Enqueue(command);
            return;
        }

        try
        {
            await TerminalWebView.ExecuteScriptAsync(
                $"window.salmonTerminal && window.salmonTerminal.dispatch({SerializeHostMessage(command.Message)});");
        }
        catch
        {
            _pendingCommands.Enqueue(command);
        }
    }

    private async Task FlushPendingCommandsAsync()
    {
        while (_isViewActive && _isReady && TerminalWebView.CoreWebView2 != null && _pendingCommands.Count > 0)
        {
            var command = _pendingCommands.Dequeue();
            if (!IsCommandCurrent(command))
            {
                continue;
            }

            try
            {
                await TerminalWebView.ExecuteScriptAsync(
                    $"window.salmonTerminal && window.salmonTerminal.dispatch({SerializeHostMessage(command.Message)});");
            }
            catch
            {
                _pendingCommands.Enqueue(command);
                return;
            }
        }
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (DispatcherQueue is null || DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completionSource = new TaskCompletionSource<object?>();
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completionSource.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }))
        {
            completionSource.TrySetResult(null);
        }

        return completionSource.Task;
    }

    private void ReplaceRenderedText(string text)
    {
        RenderedText = text ?? string.Empty;
    }

    private void AppendRenderedText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        RenderedText = string.Concat(RenderedText, text);
    }

    private static JsonDocument? TryParseMessageDocument(string messageJson)
    {
        try
        {
            var document = JsonDocument.Parse(messageJson);
            if (document.RootElement.ValueKind != JsonValueKind.String)
            {
                return document;
            }

            var nestedJson = document.RootElement.GetString();
            document.Dispose();
            if (string.IsNullOrWhiteSpace(nestedJson))
            {
                return null;
            }

            return JsonDocument.Parse(nestedJson);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out value);
    }

    private void NavigateToCurrentHost()
    {
        _isReady = false;
        _currentHostId = CreateHostId();
        TerminalWebView.Source = new Uri(
            $"https://{TerminalHostName}/xterm-host.html?hostId={Uri.EscapeDataString(_currentHostId)}");
    }

    private static string SerializeHostMessage(TerminalHostMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("kind", message.Kind);
        writer.WriteString("hostId", message.HostId);

        if (!string.IsNullOrWhiteSpace(message.TransportMode))
        {
            writer.WriteString("transportMode", message.TransportMode);
        }

        if (message.Text is not null)
        {
            writer.WriteString("text", message.Text);
        }

        if (message.Enabled.HasValue)
        {
            writer.WriteBoolean("enabled", message.Enabled.Value);
        }

        if (message.Width.HasValue)
        {
            writer.WriteNumber("width", message.Width.Value);
        }

        if (message.Height.HasValue)
        {
            writer.WriteNumber("height", message.Height.Value);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void ClearPendingCommands(Func<TerminalHostCommand, bool> predicate)
    {
        if (_pendingCommands.Count == 0)
        {
            return;
        }

        var retainedCommands = new Queue<TerminalHostCommand>(_pendingCommands.Count);
        while (_pendingCommands.Count > 0)
        {
            var command = _pendingCommands.Dequeue();
            if (!predicate(command))
            {
                retainedCommands.Enqueue(command);
            }
        }

        while (retainedCommands.Count > 0)
        {
            _pendingCommands.Enqueue(retainedCommands.Dequeue());
        }
    }

    private bool IsCommandCurrent(TerminalHostCommand command)
    {
        return string.Equals(command.Message.HostId, _currentHostId, StringComparison.Ordinal)
            && command.SessionGeneration == _sessionGeneration;
    }

    private static string CreateHostId()
    {
        return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    private void ShowTerminal()
    {
        TerminalWebView.Visibility = Visibility.Visible;
        FallbackText.Visibility = Visibility.Collapsed;
    }

    private void ShowFallback()
    {
        _isReady = false;
        TerminalWebView.Visibility = Visibility.Collapsed;
        FallbackText.Visibility = Visibility.Visible;
    }

    private enum TerminalOutputChannel
    {
        Stdout,
        Stderr
    }

    private sealed class TerminalHostCommand
    {
        public TerminalHostCommand(int sessionGeneration, TerminalHostMessage message)
        {
            SessionGeneration = sessionGeneration;
            Message = message;
        }

        public int SessionGeneration { get; }

        public TerminalHostMessage Message { get; }
    }

    private sealed class TerminalHostMessage
    {
        public string Kind { get; init; } = string.Empty;

        public string HostId { get; init; } = string.Empty;

        public string? TransportMode { get; init; }

        public string? Text { get; init; }

        public bool? Enabled { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }
    }
}
