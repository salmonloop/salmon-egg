using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models.Mcp;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class McpServerRowViewModel : ObservableObject
{
    private static readonly Action<McpServerRowViewModel> NoopRemove = static _ => { };
    private readonly Action<McpServerRowViewModel>? _remove;
    private Func<McpServerRowViewModel, Task>? _save;
    private Action<McpServerRowViewModel>? _edit;
    private Action<McpServerRowViewModel>? _edited;
    private Action<McpServerRowViewModel>? _enabledChanged;
    private bool _suppressEnabledChanged;
    private bool _suppressEdited;

    public McpServerRowViewModel()
    {
        RemoveCommand = new RelayCommand(Remove);
        EditCommand = new RelayCommand(Edit);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public McpServerRowViewModel(
        Action<McpServerRowViewModel> remove,
        Func<McpServerRowViewModel, Task>? save = null)
        : this()
    {
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _save = save;
    }

    public IRelayCommand RemoveCommand { get; }

    public IRelayCommand EditCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public string? PersistedName { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private McpServerTransport _transport;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _argumentsText = string.Empty;

    [ObservableProperty]
    private string _environmentText = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _headersText = string.Empty;

    [ObservableProperty]
    private bool _isDetailsExpanded;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool IsStdio => Transport == McpServerTransport.Stdio;

    public bool IsHttpLike => Transport is McpServerTransport.Http or McpServerTransport.Sse;

    public McpTransportOptionViewModel[] TransportOptions { get; } =
    [
        new(McpServerTransport.Stdio, "stdio"),
        new(McpServerTransport.Http, "Streamable HTTP"),
        new(McpServerTransport.Sse, "SSE")
    ];

    public static McpServerRowViewModel FromServer(
        McpServer server,
        Action<McpServerRowViewModel>? remove = null,
        Func<McpServerRowViewModel, Task>? save = null,
        Action<McpServerRowViewModel>? edit = null,
        Action<McpServerRowViewModel>? edited = null,
        Action<McpServerRowViewModel>? enabledChanged = null)
    {
        ArgumentNullException.ThrowIfNull(server);

        var row = server switch
        {
            StdioMcpServer stdio => new McpServerRowViewModel(remove ?? NoopRemove, save)
            {
                Name = stdio.Name,
                Enabled = stdio.Enabled,
                Transport = McpServerTransport.Stdio,
                Command = stdio.Command,
                ArgumentsText = JoinCommandLine(stdio.Args),
                EnvironmentText = JoinNameValueLines(stdio.Env)
            },
            HttpMcpServer http => new McpServerRowViewModel(remove ?? NoopRemove, save)
            {
                Name = http.Name,
                Enabled = http.Enabled,
                Transport = McpServerTransport.Http,
                Url = http.Url,
                HeadersText = JoinNameValueLines(http.Headers)
            },
            SseMcpServer sse => new McpServerRowViewModel(remove ?? NoopRemove, save)
            {
                Name = sse.Name,
                Enabled = sse.Enabled,
                Transport = McpServerTransport.Sse,
                Url = sse.Url,
                HeadersText = JoinNameValueLines(sse.Headers)
            },
            _ => throw new NotSupportedException($"Unsupported MCP server type '{server.GetType().Name}'.")
        };
        row.MarkPersisted(server.Name);
        row.SetEditCallback(edit);
        row.SetEditedCallback(edited);
        row.SetEnabledChangedCallback(enabledChanged);
        return row;
    }

    private void Remove()
    {
        _remove?.Invoke(this);
    }

    private void Edit()
    {
        _edit?.Invoke(this);
    }

    private Task SaveAsync()
        => _save?.Invoke(this) ?? Task.CompletedTask;

    public void MarkPersisted(string persistedName)
    {
        PersistedName = persistedName;
    }

    public void SetSaveCallback(Func<McpServerRowViewModel, Task>? save)
    {
        _save = save;
    }

    public void SetEditCallback(Action<McpServerRowViewModel>? edit)
    {
        _edit = edit;
    }

    public void SetEditedCallback(Action<McpServerRowViewModel>? edited)
    {
        _edited = edited;
    }

    public void SetEnabledChangedCallback(Action<McpServerRowViewModel>? enabledChanged)
    {
        _enabledChanged = enabledChanged;
    }

    public void SetEnabledFromStore(bool enabled)
    {
        _suppressEnabledChanged = true;
        try
        {
            Enabled = enabled;
        }
        finally
        {
            _suppressEnabledChanged = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_suppressEnabledChanged)
        {
            return;
        }

        _enabledChanged?.Invoke(this);
    }

    public void SetStatusMessage(string statusMessage)
    {
        StatusMessage = statusMessage;
    }

    public void MarkClean(string persistedName, string statusMessage)
    {
        _suppressEdited = true;
        try
        {
            PersistedName = persistedName;
            StatusMessage = statusMessage;
        }
        finally
        {
            _suppressEdited = false;
        }
    }

    public McpServerRowViewModel CreateEditorCopy(
        Action<McpServerRowViewModel> remove,
        Func<McpServerRowViewModel, Task> save,
        Action<McpServerRowViewModel> edited)
    {
        var copy = new McpServerRowViewModel(remove, save)
        {
            Name = Name,
            Enabled = Enabled,
            Transport = Transport,
            Command = Command,
            ArgumentsText = ArgumentsText,
            EnvironmentText = EnvironmentText,
            Url = Url,
            HeadersText = HeadersText,
            IsDetailsExpanded = true
        };
        if (PersistedName is not null)
        {
            copy.MarkPersisted(PersistedName);
        }

        copy.SetEditedCallback(edited);
        copy.SetStatusMessage(StatusMessage);
        return copy;
    }

    partial void OnNameChanged(string value) => NotifyEdited();

    partial void OnCommandChanged(string value) => NotifyEdited();

    partial void OnArgumentsTextChanged(string value) => NotifyEdited();

    partial void OnEnvironmentTextChanged(string value) => NotifyEdited();

    partial void OnUrlChanged(string value) => NotifyEdited();

    partial void OnHeadersTextChanged(string value) => NotifyEdited();

    private void NotifyEdited()
    {
        if (_suppressEdited)
        {
            return;
        }

        _edited?.Invoke(this);
    }

    public McpServer ToServer()
    {
        var name = Name.Trim();
        return Transport switch
        {
            McpServerTransport.Stdio => new StdioMcpServer(
                name,
                Command.Trim(),
                SplitCommandLine(ArgumentsText),
                ParseNameValueLines(EnvironmentText, (key, value) => new McpEnvVariable(key, value)))
            {
                Enabled = Enabled
            },
            McpServerTransport.Http => new HttpMcpServer(
                name,
                Url.Trim(),
                ParseNameValueLines(HeadersText, (key, value) => new McpHttpHeader(key, value)))
            {
                Enabled = Enabled
            },
            McpServerTransport.Sse => new SseMcpServer(
                name,
                Url.Trim(),
                ParseNameValueLines(HeadersText, (key, value) => new McpHttpHeader(key, value)))
            {
                Enabled = Enabled
            },
            _ => throw new NotSupportedException($"Unsupported MCP transport '{Transport}'.")
        };
    }

    partial void OnTransportChanged(McpServerTransport value)
    {
        OnPropertyChanged(nameof(IsStdio));
        OnPropertyChanged(nameof(IsHttpLike));
        NotifyEdited();
    }

    private static string JoinCommandLine(IReadOnlyList<string>? args)
        => args is null || args.Count == 0
            ? string.Empty
            : string.Join(" ", args);

    private static string JoinNameValueLines<T>(IReadOnlyList<T>? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(values.Count);
        foreach (var value in values)
        {
            switch (value)
            {
                case McpEnvVariable env:
                    lines.Add($"{env.Name}={env.Value}");
                    break;
                case McpHttpHeader header:
                    lines.Add($"{header.Name}: {header.Value}");
                    break;
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<string> SplitCommandLine(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                AddCurrent();
                continue;
            }

            current.Append(ch);
        }

        AddCurrent();
        return result;

        void AddCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            result.Add(current.ToString());
            current.Clear();
        }
    }

    private static List<T> ParseNameValueLines<T>(string? text, Func<string, string, T> factory)
    {
        var result = new List<T>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            var colon = line.IndexOf(':');
            if (separator < 0 || (colon >= 0 && colon < separator))
            {
                separator = colon;
            }

            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            result.Add(factory(key, value));
        }

        return result;
    }
}

public sealed class McpTransportOptionViewModel
{
    public McpTransportOptionViewModel(McpServerTransport transport, string name)
    {
        Transport = transport;
        Name = name;
    }

    public McpServerTransport Transport { get; }

    public string Name { get; }
}
