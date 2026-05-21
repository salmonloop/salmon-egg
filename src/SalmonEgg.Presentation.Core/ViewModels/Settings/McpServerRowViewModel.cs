using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models.Mcp;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class McpServerRowViewModel : ObservableObject
{
    public IRelayCommand RemoveCommand { get; set; } = new RelayCommand(() => { });

    [ObservableProperty]
    private string _name = string.Empty;

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

    public bool IsStdio => Transport == McpServerTransport.Stdio;

    public bool IsHttpLike => Transport is McpServerTransport.Http or McpServerTransport.Sse;

    public McpServerTransport[] TransportOptions { get; } =
    [
        McpServerTransport.Stdio,
        McpServerTransport.Http,
        McpServerTransport.Sse
    ];

    public static McpServerRowViewModel FromServer(McpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server switch
        {
            StdioMcpServer stdio => new McpServerRowViewModel
            {
                Name = stdio.Name,
                Transport = McpServerTransport.Stdio,
                Command = stdio.Command,
                ArgumentsText = JoinCommandLine(stdio.Args),
                EnvironmentText = JoinNameValueLines(stdio.Env)
            },
            HttpMcpServer http => new McpServerRowViewModel
            {
                Name = http.Name,
                Transport = McpServerTransport.Http,
                Url = http.Url,
                HeadersText = JoinNameValueLines(http.Headers)
            },
            SseMcpServer sse => new McpServerRowViewModel
            {
                Name = sse.Name,
                Transport = McpServerTransport.Sse,
                Url = sse.Url,
                HeadersText = JoinNameValueLines(sse.Headers)
            },
            _ => throw new NotSupportedException($"Unsupported MCP server type '{server.GetType().Name}'.")
        };
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
                ParseNameValueLines(EnvironmentText, (key, value) => new McpEnvVariable(key, value))),
            McpServerTransport.Http => new HttpMcpServer(
                name,
                Url.Trim(),
                ParseNameValueLines(HeadersText, (key, value) => new McpHttpHeader(key, value))),
            McpServerTransport.Sse => new SseMcpServer(
                name,
                Url.Trim(),
                ParseNameValueLines(HeadersText, (key, value) => new McpHttpHeader(key, value))),
            _ => throw new NotSupportedException($"Unsupported MCP transport '{Transport}'.")
        };
    }

    partial void OnTransportChanged(McpServerTransport value)
    {
        OnPropertyChanged(nameof(IsStdio));
        OnPropertyChanged(nameof(IsHttpLike));
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
