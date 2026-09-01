using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Authoritative snapshot of a terminal's output.
    /// </summary>
    /// <remarks>
    /// Carries no terminal id of its own: it is always read in the context of the
    /// <see cref="TerminalSessionUpdate"/> that contains it.
    /// </remarks>
    public sealed record TerminalOutput : AcpProtocolObject
    {
        /// <summary>
        /// The complete output so far, base64-encoded. Required by the protocol.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;
    }

    /// <summary>
    /// V2 <c>terminal_update</c>: the Agent reports the state of a terminal it owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how terminals work in v2. There are no <c>terminal/*</c> methods at all - the Client no
    /// longer creates, waits on, kills, or releases terminals over ACP. The Agent owns them and reports
    /// their state, so this notification and <see cref="TerminalOutputChunkSessionUpdate"/> replace the
    /// entire v1 client-implemented terminal surface.
    /// </para>
    /// <para>
    /// Every field except the id is patch semantics: absent leaves the current value alone, <c>null</c>
    /// clears it, and a value replaces it. The presence flags exist because a plain nullable field
    /// cannot tell "leave unchanged" from "clear".
    /// </para>
    /// </remarks>
    public sealed record TerminalSessionUpdate : SessionUpdate
    {
        /// <summary>
        /// The terminal this update addresses. Required by the protocol.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;

        /// <summary>
        /// The command being run, or <c>null</c> to clear it. See <see cref="HasCommand"/>.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; init; }

        /// <summary>
        /// Whether <c>command</c> was present on the wire.
        /// </summary>
        [JsonIgnore]
        public bool HasCommand { get; init; }

        /// <summary>
        /// The working directory, or <c>null</c> to clear it. See <see cref="HasCwd"/>.
        /// </summary>
        [JsonPropertyName("cwd")]
        public string? Cwd { get; init; }

        /// <summary>
        /// Whether <c>cwd</c> was present on the wire.
        /// </summary>
        [JsonIgnore]
        public bool HasCwd { get; init; }

        /// <summary>
        /// A replacement output snapshot, or <c>null</c> to clear it. See <see cref="HasOutput"/>.
        /// </summary>
        [JsonPropertyName("output")]
        public TerminalOutput? Output { get; init; }

        /// <summary>
        /// Whether <c>output</c> was present on the wire.
        /// </summary>
        [JsonIgnore]
        public bool HasOutput { get; init; }

        /// <summary>
        /// The exit status, or <c>null</c> to clear it. Presence of the object marks the terminal as
        /// exited even when both of its fields are absent. See <see cref="HasExitStatus"/>.
        /// </summary>
        [JsonPropertyName("exitStatus")]
        public TerminalExitStatus? ExitStatus { get; init; }

        /// <summary>
        /// Whether <c>exitStatus</c> was present on the wire.
        /// </summary>
        [JsonIgnore]
        public bool HasExitStatus { get; init; }
    }

    /// <summary>
    /// V2 <c>terminal_output_chunk</c>: output appended to a terminal the Agent owns.
    /// </summary>
    /// <remarks>
    /// Each chunk is independently base64-encoded, so consumers must decode per chunk and concatenate
    /// the resulting bytes. Concatenating the base64 text first would corrupt the stream at any chunk
    /// boundary whose payload length is not a multiple of three.
    /// </remarks>
    public sealed record TerminalOutputChunkSessionUpdate : SessionUpdate
    {
        /// <summary>
        /// The terminal this chunk belongs to. Required by the protocol.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;

        /// <summary>
        /// The appended output fragment, base64-encoded. Required by the protocol.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;
    }
}
