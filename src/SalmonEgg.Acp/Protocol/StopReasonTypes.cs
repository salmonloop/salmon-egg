using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// Reason an agent stopped generating a prompt response.
/// </summary>
/// <remarks>
/// Modeled as an extensible value type rather than a closed enum so that unknown
/// wire values are preserved and round-tripped losslessly, matching the authoritative
/// ACP schema (<c>StopReason</c> is <c>#[non_exhaustive]</c> with an untagged
/// <c>Other(String)</c> fallback). Per the ACP extensibility contract, unknown values
/// that do not begin with <c>_</c> are reserved for future ACP variants and MUST NOT
/// be rejected by a client.
/// </remarks>
[JsonConverter(typeof(StopReasonJsonConverter))]
public readonly struct StopReason : IEquatable<StopReason>
{
    private readonly string? _value;

    /// <summary>
    /// Creates a stop reason carrying the given wire value.
    /// </summary>
    /// <param name="value">The protocol string value.</param>
    public StopReason(string value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The agent completed the turn normally.
    /// </summary>
    public static StopReason EndTurn { get; } = new("end_turn");

    /// <summary>
    /// The agent reached its token limit.
    /// </summary>
    public static StopReason MaxTokens { get; } = new("max_tokens");

    /// <summary>
    /// The agent exceeded the maximum request count for the turn.
    /// </summary>
    public static StopReason MaxTurnRequests { get; } = new("max_turn_requests");

    /// <summary>
    /// The agent refused the request.
    /// </summary>
    public static StopReason Refusal { get; } = new("refusal");

    /// <summary>
    /// The turn was cancelled by the user or client.
    /// </summary>
    public static StopReason Cancelled { get; } = new("cancelled");

    /// <summary>
    /// The wire value carried by this stop reason.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(StopReason other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StopReason other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>
    /// Determines whether two stop reasons carry the same wire value.
    /// </summary>
    public static bool operator ==(StopReason left, StopReason right) => left.Equals(right);

    /// <summary>
    /// Determines whether two stop reasons carry different wire values.
    /// </summary>
    public static bool operator !=(StopReason left, StopReason right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Value;
}

public sealed class StopReasonJsonConverter : JsonConverter<StopReason>
{
    public override StopReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Stop reason must be a string.");
        }

        return new StopReason(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, StopReason value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
