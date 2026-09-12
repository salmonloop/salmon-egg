using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Client;

/// <summary>The kind of an ACP v2 message, including agent reasoning kept separately from output.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public enum AcpMessageKind
{
    /// <summary>A user message reported by the Agent.</summary>
    User,
    /// <summary>A response from the Agent.</summary>
    Agent,
    /// <summary>Agent reasoning.</summary>
    Thought
}

/// <summary>An immutable point-in-time projection of ACP v2 session updates.</summary>
/// <remarks>
/// Messages, tool calls and terminals retain first-seen order. Protocol DTO getters return detached
/// values because their wire contracts contain mutable collections. Changing one cannot change this
/// snapshot, any later snapshot, or the client's state. This is a current-view projection, not a
/// lossless archive. Hosts retain original events when they require complete update history.
/// </remarks>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed class AcpSessionSnapshot
{
    /// <summary>The maximum number of recent unprojected updates retained per session.</summary>
    public const int MaxUnprojectedUpdates = 64;
    /// <summary>The maximum combined UTF-8 JSON size of retained unprojected updates.</summary>
    public const int MaxUnprojectedUtf8Bytes = 256 * 1024;

    private readonly JsonElement? _workState;
    private readonly ImmutableArray<JsonElement>? _configOptions;

    internal AcpSessionSnapshot(
        string sessionId,
        ImmutableArray<AcpMessageSnapshot> messages,
        ImmutableArray<AcpToolCallSnapshot> toolCalls,
        ImmutableArray<AcpTerminalSnapshot> terminals,
        ImmutableArray<JsonElement> unprojectedUpdates,
        long omittedUnprojectedUpdateCount,
        JsonElement? workState,
        ImmutableArray<JsonElement>? configOptions)
    {
        SessionId = sessionId;
        Messages = messages;
        ToolCalls = toolCalls;
        Terminals = terminals;
        UnprojectedUpdates = unprojectedUpdates;
        OmittedUnprojectedUpdateCount = omittedUnprojectedUpdateCount;
        _workState = workState;
        _configOptions = configOptions;
    }

    /// <summary>The session whose history was projected.</summary>
    public string SessionId { get; }

    /// <summary>Messages indexed by the Agent's message ids and retained in first-seen order.</summary>
    public ImmutableArray<AcpMessageSnapshot> Messages { get; }

    /// <summary>Tool calls retained in first-seen order.</summary>
    public ImmutableArray<AcpToolCallSnapshot> ToolCalls { get; }

    /// <summary>Agent-owned terminals retained in first-seen order.</summary>
    public ImmutableArray<AcpTerminalSnapshot> Terminals { get; }

    /// <summary>
    /// Recent updates outside the message/tool/terminal projection, including unknown future variants,
    /// retained verbatim in receive order within the count and byte limits. Oversized updates are
    /// omitted; fitting updates evict the oldest entries as needed. Work state is exposed separately.
    /// </summary>
    public ImmutableArray<JsonElement> UnprojectedUpdates { get; }

    /// <summary>Unprojected updates omitted or evicted since this projection began or was replayed.</summary>
    public long OmittedUnprojectedUpdateCount { get; }

    /// <summary>The latest reported work state, detached from the client's work controller.</summary>
    public SessionWorkState? WorkState => AcpSessionProjectionJson.Read<SessionWorkState>(_workState);

    /// <summary>Whether the Agent has supplied an authoritative configuration list, including an empty list.</summary>
    public bool HasConfigOptions => _configOptions.HasValue;

    /// <summary>
    /// The latest full configuration list in Agent priority order. Known and unknown option types
    /// are retained. Each access returns detached wire DTOs; hosts edit only types they understand.
    /// </summary>
    public ImmutableArray<ConfigOption> ConfigOptions => _configOptions is { } options
        ? AcpSessionProjectionJson.ReadArray<ConfigOption>(options)
        : [];
}

/// <summary>The current content of one Agent-identified message.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed class AcpMessageSnapshot
{
    private readonly ImmutableArray<JsonElement> _content;

    internal AcpMessageSnapshot(
        string messageId,
        AcpMessageKind kind,
        ImmutableArray<JsonElement> content,
        JsonElement? meta,
        ImmutableDictionary<string, JsonElement> extensionData)
    {
        MessageId = messageId;
        Kind = kind;
        _content = content;
        Meta = meta;
        ExtensionData = extensionData;
    }

    /// <summary>The message id supplied by the Agent.</summary>
    public string MessageId { get; }

    /// <summary>The message's role.</summary>
    public AcpMessageKind Kind { get; }

    /// <summary>Content after ordered upserts and chunks. Each access returns detached wire DTOs.</summary>
    public ImmutableArray<ContentBlock> Content => AcpSessionProjectionJson.ReadArray<ContentBlock>(_content);

    /// <summary>Message-scoped metadata. Chunk metadata never replaces it.</summary>
    public JsonElement? Meta { get; }

    /// <summary>Last-seen values of unknown message fields; chunk fields remain on their events.</summary>
    public ImmutableDictionary<string, JsonElement> ExtensionData { get; }
}

/// <summary>The current patch-merged state of one tool call.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed class AcpToolCallSnapshot
{
    private readonly ImmutableArray<JsonElement> _content;
    private readonly ImmutableArray<JsonElement> _locations;

    internal AcpToolCallSnapshot(
        string toolCallId,
        string? title,
        ToolCallKind? kind,
        ToolCallStatus? status,
        ImmutableArray<JsonElement> content,
        ImmutableArray<JsonElement> locations,
        JsonElement? rawInput,
        JsonElement? rawOutput,
        JsonElement? meta,
        ImmutableDictionary<string, JsonElement> extensionData)
    {
        ToolCallId = toolCallId;
        Title = title;
        Kind = kind;
        Status = status;
        _content = content;
        _locations = locations;
        RawInput = rawInput;
        RawOutput = rawOutput;
        Meta = meta;
        ExtensionData = extensionData;
    }

    /// <summary>The tool call id supplied by the Agent.</summary>
    public string ToolCallId { get; }
    /// <summary>The latest title, or null when unknown or cleared.</summary>
    public string? Title { get; }
    /// <summary>The latest kind, preserving unknown protocol values.</summary>
    public ToolCallKind? Kind { get; }
    /// <summary>The latest status, preserving unknown protocol values.</summary>
    public ToolCallStatus? Status { get; }
    /// <summary>Content after replacement and append updates. Each access returns detached DTOs.</summary>
    public ImmutableArray<ToolCallContent> Content => AcpSessionProjectionJson.ReadArray<ToolCallContent>(_content);
    /// <summary>Latest affected locations. Each access returns detached DTOs.</summary>
    public ImmutableArray<ToolCallLocation> Locations => AcpSessionProjectionJson.ReadArray<ToolCallLocation>(_locations);
    /// <summary>The latest raw input value.</summary>
    public JsonElement? RawInput { get; }
    /// <summary>The latest raw output value.</summary>
    public JsonElement? RawOutput { get; }
    /// <summary>Tool-call-scoped metadata.</summary>
    public JsonElement? Meta { get; }
    /// <summary>Last-seen values of unknown tool-call fields; chunk fields remain on their events.</summary>
    public ImmutableDictionary<string, JsonElement> ExtensionData { get; }
}

/// <summary>The current state and decoded output of an Agent-owned terminal.</summary>
/// <remarks>This describes a terminal; it never creates or controls a local process.</remarks>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed class AcpTerminalSnapshot
{
    private readonly JsonElement? _exitStatus;

    internal AcpTerminalSnapshot(
        string terminalId,
        string? command,
        string? cwd,
        ImmutableArray<byte> output,
        JsonElement? outputMeta,
        JsonElement? exitStatus,
        JsonElement? meta,
        ImmutableDictionary<string, JsonElement> extensionData)
    {
        TerminalId = terminalId;
        Command = command;
        Cwd = cwd;
        Output = output;
        OutputMeta = outputMeta;
        _exitStatus = exitStatus;
        Meta = meta;
        ExtensionData = extensionData;
    }

    /// <summary>The terminal id supplied by the Agent.</summary>
    public string TerminalId { get; }
    /// <summary>The latest command, or null when unknown or cleared.</summary>
    public string? Command { get; }
    /// <summary>The latest working directory, or null when unknown or cleared.</summary>
    public string? Cwd { get; }
    /// <summary>Output bytes after replacement snapshots and independently decoded chunks.</summary>
    public ImmutableArray<byte> Output { get; }
    /// <summary>Metadata belonging to the latest output replacement snapshot.</summary>
    public JsonElement? OutputMeta { get; }
    /// <summary>Whether an exit-status object has been reported, even if its fields are unknown.</summary>
    public bool HasExited => _exitStatus is not null;
    /// <summary>The latest exit information. Each access returns a detached DTO.</summary>
    public TerminalExitStatus? ExitStatus => AcpSessionProjectionJson.Read<TerminalExitStatus>(_exitStatus);
    /// <summary>Terminal-scoped metadata.</summary>
    public JsonElement? Meta { get; }
    /// <summary>Last-seen values of unknown terminal fields; chunk fields remain on their events.</summary>
    public ImmutableDictionary<string, JsonElement> ExtensionData { get; }
}
