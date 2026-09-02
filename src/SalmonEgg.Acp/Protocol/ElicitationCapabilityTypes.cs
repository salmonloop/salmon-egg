using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Elicitation capabilities advertised by the client, per mode.
    /// </summary>
    /// <remarks>
    /// ACP deliberately diverges from the MCP 2026-07-28 release candidate here: an empty capability
    /// object does <em>not</em> retain MCP's form-only meaning. Each supported mode must be advertised
    /// explicitly through its own non-null field, so <c>{}</c> and <c>{"form":null,"url":null}</c> both
    /// advertise no supported modes. An omitted or <c>null</c> capability object means elicitation is
    /// unsupported altogether.
    /// </remarks>
    public sealed record ElicitationCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Form-mode support. <c>null</c> means the mode is not advertised.
        /// </summary>
        [JsonPropertyName("form")]
        public ElicitationFormCapabilities? Form { get; init; }

        /// <summary>
        /// URL-mode support. <c>null</c> means the mode is not advertised.
        /// </summary>
        [JsonPropertyName("url")]
        public ElicitationUrlCapabilities? Url { get; init; }

        /// <summary>
        /// Whether form-mode elicitation is advertised.
        /// </summary>
        [JsonIgnore]
        public bool SupportsForm => Form is not null;

        /// <summary>
        /// Whether URL-mode elicitation is advertised.
        /// </summary>
        [JsonIgnore]
        public bool SupportsUrl => Url is not null;
    }

    /// <summary>
    /// Form-based elicitation capability marker. Supplying an instance advertises form support.
    /// </summary>
    public sealed record ElicitationFormCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// URL-based elicitation capability marker. Supplying an instance advertises URL support.
    /// </summary>
    public sealed record ElicitationUrlCapabilities : AcpProtocolObject
    {
    }
}
