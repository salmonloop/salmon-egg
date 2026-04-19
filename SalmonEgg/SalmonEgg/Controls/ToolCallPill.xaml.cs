using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Domain.Models.Tool;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Controls;

public sealed partial class ToolCallPill : UserControl, INotifyPropertyChanged
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public static readonly DependencyProperty ToolTitleProperty =
        DependencyProperty.Register(
            nameof(ToolTitle),
            typeof(string),
            typeof(ToolCallPill),
            new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty ToolKindProperty =
        DependencyProperty.Register(
            nameof(ToolKind),
            typeof(ToolCallKind?),
            typeof(ToolCallPill),
            new PropertyMetadata(null, OnDisplayInputChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(ToolCallStatus?), typeof(ToolCallPill), new PropertyMetadata(null, OnDisplayInputChanged));

    public static readonly DependencyProperty RawPayloadProperty =
        DependencyProperty.Register(nameof(RawPayload), typeof(string), typeof(ToolCallPill), new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty IsInProgressProperty =
        DependencyProperty.Register(nameof(IsInProgress), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false));

    public static readonly DependencyProperty IsCompletedProperty =
        DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFailedProperty =
        DependencyProperty.Register(nameof(IsFailed), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false));

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ToolTitle
    {
        get => (string)GetValue(ToolTitleProperty);
        set => SetValue(ToolTitleProperty, value);
    }

    public ToolCallKind? ToolKind
    {
        get => (ToolCallKind?)GetValue(ToolKindProperty);
        set => SetValue(ToolKindProperty, value);
    }

    public ToolCallStatus? Status
    {
        get => (ToolCallStatus?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string RawPayload
    {
        get => (string)GetValue(RawPayloadProperty);
        set => SetValue(RawPayloadProperty, value);
    }

    public bool IsInProgress
    {
        get => (bool)GetValue(IsInProgressProperty);
        set => SetValue(IsInProgressProperty, value);
    }

    public bool IsCompleted
    {
        get => (bool)GetValue(IsCompletedProperty);
        set => SetValue(IsCompletedProperty, value);
    }

    public bool IsFailed
    {
        get => (bool)GetValue(IsFailedProperty);
        set => SetValue(IsFailedProperty, value);
    }

    public string DisplayToolName => ResolveToolName();

    public string DisplaySummary => ResolveSummary();

    public string PayloadHeaderText => ResolveResourceString("ToolCallPillPayloadTitle", "Payload details");

    public string AutomationName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplaySummary))
            {
                return DisplayToolName;
            }

            return $"{DisplayToolName}, {DisplaySummary}";
        }
    }

    public ToolCallPill()
    {
        InitializeComponent();
    }

    private static void OnDisplayInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolCallPill pill)
        {
            pill.NotifyDisplayChanged();
        }
    }

    private void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(DisplayToolName));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(PayloadHeaderText));
        OnPropertyChanged(nameof(AutomationName));
    }

    private string ResolveToolName()
    {
        if (!string.IsNullOrWhiteSpace(ToolTitle))
        {
            return ToolTitle;
        }

        return ToolKind switch
        {
            ToolCallKind.Read => $"🔍 {ResolveResourceString("ToolCallPillKindRead", "Read file")}",
            ToolCallKind.Edit => $"📝 {ResolveResourceString("ToolCallPillKindEdit", "Edit file")}",
            ToolCallKind.Delete => $"🗑 {ResolveResourceString("ToolCallPillKindDelete", "Delete file")}",
            ToolCallKind.Move => $"📦 {ResolveResourceString("ToolCallPillKindMove", "Move file")}",
            ToolCallKind.Search => $"🔎 {ResolveResourceString("ToolCallPillKindSearch", "Search code")}",
            ToolCallKind.Execute => $"💻 {ResolveResourceString("ToolCallPillKindExecute", "Run command")}",
            ToolCallKind.SwitchMode => $"🔄 {ResolveResourceString("ToolCallPillKindSwitchMode", "Switch mode")}",
            ToolCallKind.Think => $"🧠 {ResolveResourceString("ToolCallPillKindThink", "Thinking")}",
            ToolCallKind.Fetch => $"🌐 {ResolveResourceString("ToolCallPillKindFetch", "Fetch data")}",
            _ => $"🛠 {ResolveResourceString("ToolCallPillKindDefault", "Tool call")}"
        };
    }

    private string ResolveSummary()
    {
        if (string.IsNullOrWhiteSpace(RawPayload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(RawPayload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return SummarizePlainText(RawPayload);
            }

            var parts = new List<string>();
            var path = TryGetString(root, "path", "Path", "SearchPath", "searchPath", "TargetFile", "targetFile");
            if (!string.IsNullOrWhiteSpace(path))
            {
                parts.Add($"{ResolveResourceString("ToolCallPillSummaryPathLabel", "Path")}: {path}");
            }

            var query = TryGetString(root, "query", "Query");
            if (!string.IsNullOrWhiteSpace(query))
            {
                parts.Add($"{ResolveResourceString("ToolCallPillSummaryQueryLabel", "Query")}: {query}");
            }

            var command = TryGetString(root, "CommandLine", "commandLine", "command", "Command", "cmd");
            var arguments = TryGetString(root, "Arguments", "arguments", "Args", "args");
            var commandSummary = BuildCommandSummary(command, arguments);
            if (!string.IsNullOrWhiteSpace(commandSummary))
            {
                parts.Add($"{ResolveResourceString("ToolCallPillSummaryCommandLabel", "Command")}: {commandSummary}");
            }

            if (parts.Count > 0)
            {
                return Truncate(string.Join(", ", parts));
            }
        }
        catch (JsonException)
        {
        }

        return SummarizePlainText(RawPayload);
    }

    private static string? TryGetString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.GetRawText();
        }

        return null;
    }

    private static string? BuildCommandSummary(string? command, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.IsNullOrWhiteSpace(arguments) ? null : arguments;
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return command;
        }

        return $"{command} {arguments}";
    }

    private static string SummarizePlainText(string text)
    {
        var normalized = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return Truncate(normalized);
    }

    private static string Truncate(string text)
        => text.Length > 64 ? $"{text[..61]}..." : text;

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
