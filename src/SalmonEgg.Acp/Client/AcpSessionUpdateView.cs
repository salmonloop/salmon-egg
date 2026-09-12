using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Client;

/// <summary>
/// A version-independent application view of a session update. The wire update remains available
/// separately on its event; this value describes a complete entity after the SDK has applied it.
/// </summary>
public sealed class AcpSessionUpdateView
{
    private readonly JsonElement? _toolCall;
    private readonly ImmutableArray<JsonElement>? _configOptions;
    private readonly int _configProtocolVersion;
    private readonly ImmutableArray<JsonElement>? _planEntries;

    internal AcpSessionUpdateView(
        AcpMessageView? message = null,
        JsonElement? toolCall = null,
        AcpTerminalView? terminal = null,
        string? workState = null,
        StopReason? stopReason = null,
        ImmutableArray<JsonElement>? configOptions = null,
        ImmutableArray<JsonElement>? planEntries = null,
        int configProtocolVersion = AcpProtocolVersion.V2)
    {
        Message = message;
        _toolCall = toolCall;
        Terminal = terminal;
        WorkState = workState;
        StopReason = stopReason;
        _configOptions = configOptions;
        _configProtocolVersion = configProtocolVersion;
        _planEntries = planEntries;
    }

    /// <summary>A complete message after replacement or append, or null when this update is unrelated.</summary>
    public AcpMessageView? Message { get; }

    /// <summary>A detached complete tool call, or null when this update is unrelated.</summary>
    public ToolCallStatusUpdate? ToolCall => _toolCall?.Deserialize(AcpWireFormat.For(AcpProtocolVersion.V1).TypeInfo<ToolCallStatusUpdate>());

    /// <summary>A complete Agent-owned terminal description. This never requests a local process.</summary>
    public AcpTerminalView? Terminal { get; }

    /// <summary>The peer's foreground state, separate from message and background activity.</summary>
    public string? WorkState { get; }

    /// <summary>The reported ending reason; null preserves an unspecified reason.</summary>
    public StopReason? StopReason { get; }

    /// <summary>True when this update replaces the configuration list, including an empty list.</summary>
    public bool HasConfigOptions => _configOptions.HasValue;

    /// <summary>Detached configuration options in Agent priority order.</summary>
    public ImmutableArray<ConfigOption> ConfigOptions => _configOptions is { } options
        ? options.Select(option => option.Deserialize(AcpWireFormat.For(_configProtocolVersion).TypeInfo<ConfigOption>())!)
            .ToImmutableArray() : [];

    /// <summary>True when the complete plan list is supplied.</summary>
    public bool HasPlanEntries => _planEntries.HasValue;

    /// <summary>Detached plan entries, shared by stable and draft wire surfaces.</summary>
    public ImmutableArray<PlanEntry> PlanEntries => _planEntries is { } entries
        ? AcpSessionProjectionJson.ReadArray<PlanEntry>(entries) : [];
}

/// <summary>An immutable message value for application projection, independent of wire version.</summary>
public sealed class AcpMessageView
{
    private readonly ImmutableArray<JsonElement> _content;

    internal AcpMessageView(string messageId, string role, ImmutableArray<JsonElement> content)
    {
        MessageId = messageId;
        Role = role;
        _content = content;
    }

    /// <summary>Authoritative Agent-supplied identity.</summary>
    public string MessageId { get; }

    /// <summary>Application role: user, agent, or thought.</summary>
    public string Role { get; }

    /// <summary>Complete detached content after any replacement or append.</summary>
    public ImmutableArray<ContentBlock> Content => AcpSessionProjectionJson.ReadArray<ContentBlock>(_content);
}

/// <summary>A display-only snapshot of terminal output; it never runs a local command.</summary>
public sealed class AcpTerminalView
{
    internal AcpTerminalView(string terminalId, string? command, string? cwd,
        ImmutableArray<byte> output, bool hasExited, uint? exitCode, string? signal)
    {
        TerminalId = terminalId;
        Command = command;
        Cwd = cwd;
        Output = output;
        HasExited = hasExited;
        ExitCode = exitCode;
        Signal = signal;
    }

    /// <summary>The Agent-owned terminal identity.</summary>
    public string TerminalId { get; }
    /// <summary>The reported command, if any.</summary>
    public string? Command { get; }
    /// <summary>The reported working directory, if any.</summary>
    public string? Cwd { get; }
    /// <summary>Complete decoded output bytes, preserving partial UTF-8 across chunks.</summary>
    public ImmutableArray<byte> Output { get; }
    /// <summary>The complete UTF-8 display value.</summary>
    public string OutputText => Encoding.UTF8.GetString(Output.AsSpan());
    /// <summary>Whether the Agent reported an exit-status object, even if its values are unknown.</summary>
    public bool HasExited { get; }
    /// <summary>The reported exit code.</summary>
    public uint? ExitCode { get; }
    /// <summary>The reported exit signal.</summary>
    public string? Signal { get; }
}
