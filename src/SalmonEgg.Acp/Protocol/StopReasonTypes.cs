using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// Reason an agent stopped generating a prompt response.
/// </summary>
[JsonConverter(typeof(StopReasonJsonConverter))]
public enum StopReason
{
    /// <summary>
    /// The agent completed the turn normally.
    /// </summary>
    EndTurn,

    /// <summary>
    /// The agent reached its token limit.
    /// </summary>
    MaxTokens,

    /// <summary>
    /// The agent exceeded the maximum request count for the turn.
    /// </summary>
    MaxTurnRequests,

    /// <summary>
    /// The agent refused the request.
    /// </summary>
    Refusal,

    /// <summary>
    /// The turn was cancelled by the user or client.
    /// </summary>
    Cancelled
}

public sealed class StopReasonJsonConverter : JsonConverter<StopReason>
{
    public override StopReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Stop reason must be a string.");
        }

        return reader.GetString() switch
        {
            "end_turn" => StopReason.EndTurn,
            "max_tokens" => StopReason.MaxTokens,
            "max_turn_requests" => StopReason.MaxTurnRequests,
            "refusal" => StopReason.Refusal,
            "cancelled" => StopReason.Cancelled,
            var value => throw new JsonException($"Unsupported stop reason '{value}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, StopReason value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            StopReason.EndTurn => "end_turn",
            StopReason.MaxTokens => "max_tokens",
            StopReason.MaxTurnRequests => "max_turn_requests",
            StopReason.Refusal => "refusal",
            StopReason.Cancelled => "cancelled",
            _ => throw new JsonException($"Unsupported stop reason '{value}'.")
        });
    }
}
