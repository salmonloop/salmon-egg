using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Tool
{
    /// <summary>
    /// Status of a tool call within its lifecycle.
    /// </summary>
    /// <remarks>
    /// Modeled as an extensible value type rather than a closed enum so that unknown
    /// wire values are preserved and round-tripped losslessly, matching the authoritative
    /// ACP schema (<c>ToolCallStatus</c> is <c>#[non_exhaustive]</c> with an untagged
    /// <c>Other(String)</c> fallback). Per the ACP extensibility contract, unknown values
    /// that do not begin with <c>_</c> are reserved for future ACP variants and MUST NOT
    /// be rejected by a client.
    /// </remarks>
    [JsonConverter(typeof(ToolCallStatusJsonConverter))]
    public readonly struct ToolCallStatus : IEquatable<ToolCallStatus>
    {
        private readonly string? _value;

        /// <summary>
        /// Creates a tool call status carrying the given wire value.
        /// </summary>
        /// <param name="value">The protocol string value.</param>
        public ToolCallStatus(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The tool call has been created but has not started executing.
        /// </summary>
        public static ToolCallStatus Pending { get; } = new("pending");

        /// <summary>
        /// The tool call is currently executing.
        /// </summary>
        public static ToolCallStatus InProgress { get; } = new("in_progress");

        /// <summary>
        /// The tool call completed successfully.
        /// </summary>
        public static ToolCallStatus Completed { get; } = new("completed");

        /// <summary>
        /// The tool call failed or errored.
        /// </summary>
        public static ToolCallStatus Failed { get; } = new("failed");

        /// <summary>
        /// The tool call was cancelled.
        /// </summary>
        public static ToolCallStatus Cancelled { get; } = new("cancelled");

        /// <summary>
        /// The wire value carried by this status.
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(ToolCallStatus other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is ToolCallStatus other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// Determines whether two statuses carry the same wire value.
        /// </summary>
        public static bool operator ==(ToolCallStatus left, ToolCallStatus right) => left.Equals(right);

        /// <summary>
        /// Determines whether two statuses carry different wire values.
        /// </summary>
        public static bool operator !=(ToolCallStatus left, ToolCallStatus right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    /// <summary>
    /// Category of operation performed by a tool call.
    /// </summary>
    /// <remarks>
    /// Modeled as an extensible value type rather than a closed enum so that unknown
    /// wire values are preserved and round-tripped losslessly, matching the authoritative
    /// ACP schema (<c>ToolKind</c> is <c>#[non_exhaustive]</c> with a well-known
    /// <c>Other</c> literal plus an untagged <c>Unknown(String)</c> fallback). Per the
    /// ACP extensibility contract, unknown values that do not begin with <c>_</c> are
    /// reserved for future ACP variants and MUST NOT be rejected by a client.
    /// </remarks>
    [JsonConverter(typeof(ToolCallKindJsonConverter))]
    public readonly struct ToolCallKind : IEquatable<ToolCallKind>
    {
        private readonly string? _value;

        /// <summary>
        /// Creates a tool call kind carrying the given wire value.
        /// </summary>
        /// <param name="value">The protocol string value.</param>
        public ToolCallKind(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// File read operation.
        /// </summary>
        public static ToolCallKind Read { get; } = new("read");

        /// <summary>
        /// File edit operation.
        /// </summary>
        public static ToolCallKind Edit { get; } = new("edit");

        /// <summary>
        /// File delete operation.
        /// </summary>
        public static ToolCallKind Delete { get; } = new("delete");

        /// <summary>
        /// File move or rename operation.
        /// </summary>
        public static ToolCallKind Move { get; } = new("move");

        /// <summary>
        /// Search operation.
        /// </summary>
        public static ToolCallKind Search { get; } = new("search");

        /// <summary>
        /// Terminal command execution operation.
        /// </summary>
        public static ToolCallKind Execute { get; } = new("execute");

        /// <summary>
        /// Session mode switch operation.
        /// </summary>
        public static ToolCallKind SwitchMode { get; } = new("switch_mode");

        /// <summary>
        /// Reasoning or planning operation (performs no external action).
        /// </summary>
        public static ToolCallKind Think { get; } = new("think");

        /// <summary>
        /// Network request or data retrieval operation.
        /// </summary>
        public static ToolCallKind Fetch { get; } = new("fetch");

        /// <summary>
        /// Well-known catch-all kind (the literal <c>"other"</c>). Distinct from an
        /// unrecognized future kind, which is preserved by carrying its own raw value.
        /// </summary>
        public static ToolCallKind Other { get; } = new("other");

        /// <summary>
        /// The wire value carried by this kind.
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(ToolCallKind other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is ToolCallKind other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// Determines whether two kinds carry the same wire value.
        /// </summary>
        public static bool operator ==(ToolCallKind left, ToolCallKind right) => left.Equals(right);

        /// <summary>
        /// Determines whether two kinds carry different wire values.
        /// </summary>
        public static bool operator !=(ToolCallKind left, ToolCallKind right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    public sealed class ToolCallStatusJsonConverter : JsonConverter<ToolCallStatus>
    {
        public override ToolCallStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Tool call status must be a string.");
            }

            return new ToolCallStatus(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, ToolCallStatus value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    public sealed class ToolCallKindJsonConverter : JsonConverter<ToolCallKind>
    {
        public override ToolCallKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Tool call kind must be a string.");
            }

            return new ToolCallKind(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, ToolCallKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
