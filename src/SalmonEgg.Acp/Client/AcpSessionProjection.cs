using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Client;

// The work controller owns lifetime and locking. This owned value holds only projection state;
// offline replay uses the same receive-order rules without constructing a connection or process.
internal sealed class AcpSessionProjection
{
    private readonly Dictionary<string, MessageState> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolState> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TerminalState> _terminals = new(StringComparer.Ordinal);
    private readonly List<MessageState> _messageOrder = [];
    private readonly List<ToolState> _toolOrder = [];
    private readonly List<TerminalState> _terminalOrder = [];
    private readonly Queue<(JsonElement Payload, int ByteCount)> _unprojected = new();
    private int _unprojectedBytes;
    private long _omittedUnprojectedUpdateCount;
    private ImmutableArray<JsonElement>? _configOptions;

    internal void Apply(SessionUpdate update, JsonElement payload)
    {
        switch (update)
        {
            case WholeMessageUpdate message:
                ApplyMessage(message, payload);
                break;
            case AgentMessageUpdate chunk:
                AppendMessage(chunk.MessageId!, AcpMessageKind.Agent, chunk.Content, payload);
                break;
            case UserMessageUpdate chunk:
                AppendMessage(chunk.MessageId!, AcpMessageKind.User, chunk.Content, payload);
                break;
            case AgentThoughtUpdate chunk:
                AppendMessage(chunk.MessageId!, AcpMessageKind.Thought, chunk.Content, payload);
                break;
            case ToolCallStatusUpdate tool:
                ApplyTool(tool, payload);
                break;
            case ToolCallContentChunkUpdate chunk:
                if (chunk.Content is null) throw new JsonException("Tool content chunks require content.");
                GetTool(chunk.ToolCallId).Content.Add(payload.GetProperty("content").Clone());
                break;
            case TerminalSessionUpdate terminal:
                ApplyTerminal(terminal, payload);
                break;
            case TerminalOutputChunkSessionUpdate chunk:
                // Decode before changing state: a malformed chunk must not create a phantom terminal.
                var bytes = AcpSessionProjectionJson.DecodeOutput(chunk.Data
                    ?? throw new JsonException("Terminal chunks require data."));
                GetTerminal(chunk.TerminalId).Output.AddRange(bytes);
                break;
            case StateSessionUpdate:
                break;
            case ConfigOptionUpdate configuration:
                SetConfigOptions(configuration.ConfigOptions ?? []);
                break;
            default:
                RetainUnprojected(payload);
                break;
        }
    }

    internal AcpSessionSnapshot Snapshot(string sessionId, SessionWorkState? state)
        => new(
            sessionId,
            _messageOrder.Select(static value => value.Snapshot()).ToImmutableArray(),
            _toolOrder.Select(static value => value.Snapshot()).ToImmutableArray(),
            _terminalOrder.Select(static value => value.Snapshot()).ToImmutableArray(),
            _unprojected.Select(static entry => entry.Payload).ToImmutableArray(),
            _omittedUnprojectedUpdateCount,
            state is null ? null : AcpSessionProjectionJson.Store<SessionWorkState>(state),
            _configOptions);

    internal void SetConfigOptions(IReadOnlyList<ConfigOption> configOptions)
        => _configOptions = configOptions.Select(static option => AcpSessionProjectionJson.Store(option)).ToImmutableArray();

    private void RetainUnprojected(JsonElement payload)
    {
        // Unknown variants have no defined replacement key. Keep bounded recent evidence rather
        // than inventing merge semantics or duplicating the host's complete event history.
        var byteCount = Encoding.UTF8.GetByteCount(payload.GetRawText());
        if (byteCount > AcpSessionSnapshot.MaxUnprojectedUtf8Bytes)
        {
            _omittedUnprojectedUpdateCount++;
            return;
        }
        while (_unprojected.Count >= AcpSessionSnapshot.MaxUnprojectedUpdates
            || _unprojectedBytes + byteCount > AcpSessionSnapshot.MaxUnprojectedUtf8Bytes)
        {
            _unprojectedBytes -= _unprojected.Dequeue().ByteCount;
            _omittedUnprojectedUpdateCount++;
        }
        _unprojected.Enqueue((payload.Clone(), byteCount));
        _unprojectedBytes += byteCount;
    }

    private void ApplyMessage(WholeMessageUpdate update, JsonElement payload)
    {
        var kind = update switch
        {
            AgentWholeMessageUpdate => AcpMessageKind.Agent,
            UserWholeMessageUpdate => AcpMessageKind.User,
            AgentWholeThoughtUpdate => AcpMessageKind.Thought,
            _ => throw new JsonException("Unsupported whole-message variant.")
        };
        var message = GetMessage(update.MessageId, kind);
        if (payload.TryGetProperty("content", out _))
        {
            Replace(message.Content, AcpSessionProjectionJson.ReadPatchArray<ContentBlock>(payload, "content"));
        }
        if (payload.TryGetProperty("_meta", out _))
        {
            message.Meta = AcpSessionProjectionJson.ReadMetadata(payload);
        }
        MergeExtensions(message.ExtensionData, update);
    }

    private void AppendMessage(string messageId, AcpMessageKind kind, ContentBlock? content, JsonElement payload)
    {
        if (content is null) throw new JsonException("Message chunks require content.");
        var item = payload.GetProperty("content").Clone();
        GetMessage(messageId, kind).Content.Add(item);
    }

    private void ApplyTool(ToolCallStatusUpdate update, JsonElement payload)
    {
        var tool = GetTool(update.ToolCallId!);
        if (payload.TryGetProperty("title", out _)) tool.Title = update.Title;
        if (payload.TryGetProperty("kind", out _)) tool.Kind = update.Kind;
        if (payload.TryGetProperty("status", out _)) tool.Status = update.Status;
        if (payload.TryGetProperty("content", out _))
        {
            Replace(tool.Content, AcpSessionProjectionJson.ReadPatchArray<ToolCallContent>(payload, "content"));
        }
        if (payload.TryGetProperty("locations", out _))
        {
            Replace(tool.Locations, AcpSessionProjectionJson.ReadPatchArray<ToolCallLocation>(payload, "locations"));
        }
        if (payload.TryGetProperty("rawInput", out _)) tool.RawInput = AcpSessionProjectionJson.CloneValue(update.RawInput);
        if (payload.TryGetProperty("rawOutput", out _)) tool.RawOutput = AcpSessionProjectionJson.CloneValue(update.RawOutput);
        if (payload.TryGetProperty("_meta", out _)) tool.Meta = AcpSessionProjectionJson.ReadMetadata(payload);
        MergeExtensions(tool.ExtensionData, update);
    }

    private void ApplyTerminal(TerminalSessionUpdate update, JsonElement payload)
    {
        // A replacement snapshot's containing field permits default-on-error. Invalid base64 is an
        // invalid snapshot, so it clears output like other malformed optional output values; a
        // standalone output chunk has no such recovery grant and is rejected before any mutation.
        byte[]? output = null;
        if (update.Output is { } snapshot)
        {
            try
            {
                output = AcpSessionProjectionJson.DecodeOutput(snapshot.Data);
            }
            catch (JsonException)
            {
                // schema/v2 TerminalUpdate.output explicitly permits default-on-error.
            }
        }

        var terminal = GetTerminal(update.TerminalId);
        if (payload.TryGetProperty("command", out _)) terminal.Command = update.Command;
        if (payload.TryGetProperty("cwd", out _)) terminal.Cwd = update.Cwd;
        if (payload.TryGetProperty("output", out _))
        {
            Replace(terminal.Output, output ?? []);
            terminal.OutputMeta = output is not null && payload.GetProperty("output") is { ValueKind: JsonValueKind.Object } raw
                ? AcpSessionProjectionJson.ReadMetadata(raw)
                : null;
        }
        if (payload.TryGetProperty("exitStatus", out _))
        {
            terminal.ExitStatus = update.ExitStatus is null ? null : payload.GetProperty("exitStatus").Clone();
        }
        if (payload.TryGetProperty("_meta", out _)) terminal.Meta = AcpSessionProjectionJson.ReadMetadata(payload);
        MergeExtensions(terminal.ExtensionData, update);
    }

    private MessageState GetMessage(string id, AcpMessageKind kind)
    {
        if (!_messages.TryGetValue(id, out var state))
        {
            state = new MessageState(id, kind);
            _messages.Add(id, state);
            _messageOrder.Add(state);
        }
        else
        {
            state.Kind = kind;
        }
        return state;
    }

    private ToolState GetTool(string id)
    {
        if (!_tools.TryGetValue(id, out var state))
        {
            state = new ToolState(id);
            _tools.Add(id, state);
            _toolOrder.Add(state);
        }
        return state;
    }

    private TerminalState GetTerminal(string id)
    {
        if (!_terminals.TryGetValue(id, out var state))
        {
            state = new TerminalState(id);
            _terminals.Add(id, state);
            _terminalOrder.Add(state);
        }
        return state;
    }

    private static void Replace<T>(List<T> current, IEnumerable<T> replacement)
    {
        current.Clear();
        current.AddRange(replacement);
    }

    private static void MergeExtensions(Dictionary<string, JsonElement> current, SessionUpdate update)
    {
        if (update.ExtensionData is null) return;
        foreach (var field in update.ExtensionData)
        {
            // The extension owns its semantics. Retain the last value, including explicit null,
            // without treating an unknown field as an application-defined delete instruction.
            current[field.Key] = field.Value.Clone();
        }
    }

    private sealed class MessageState(string id, AcpMessageKind kind)
    {
        internal AcpMessageKind Kind { get; set; } = kind;
        internal List<JsonElement> Content { get; } = [];
        internal JsonElement? Meta { get; set; }
        internal Dictionary<string, JsonElement> ExtensionData { get; } = new(StringComparer.Ordinal);
        internal AcpMessageSnapshot Snapshot()
            => new(id, Kind, Content.ToImmutableArray(), Meta, ExtensionData.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private sealed class ToolState(string id)
    {
        internal string? Title { get; set; }
        internal ToolCallKind? Kind { get; set; }
        internal ToolCallStatus? Status { get; set; }
        internal List<JsonElement> Content { get; } = [];
        internal List<JsonElement> Locations { get; } = [];
        internal JsonElement? RawInput { get; set; }
        internal JsonElement? RawOutput { get; set; }
        internal JsonElement? Meta { get; set; }
        internal Dictionary<string, JsonElement> ExtensionData { get; } = new(StringComparer.Ordinal);
        internal AcpToolCallSnapshot Snapshot()
            => new(id, Title, Kind, Status, Content.ToImmutableArray(), Locations.ToImmutableArray(), RawInput, RawOutput,
                Meta, ExtensionData.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private sealed class TerminalState(string id)
    {
        internal string? Command { get; set; }
        internal string? Cwd { get; set; }
        internal List<byte> Output { get; } = [];
        internal JsonElement? OutputMeta { get; set; }
        internal JsonElement? ExitStatus { get; set; }
        internal JsonElement? Meta { get; set; }
        internal Dictionary<string, JsonElement> ExtensionData { get; } = new(StringComparer.Ordinal);
        internal AcpTerminalSnapshot Snapshot()
            => new(id, Command, Cwd, Output.ToImmutableArray(), OutputMeta, ExitStatus, Meta,
                ExtensionData.ToImmutableDictionary(StringComparer.Ordinal));
    }
}
