using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Domain.Models.Conversation;

/// <summary>
/// Persistence helpers that project public ACP wire values through <see cref="AcpJsonContext"/>
/// without requiring Infrastructure access to internal ACP converters.
/// On-disk JSON keeps the original property shapes (strings / arrays), while in-memory
/// Domain/Presentation code continues to use the public ACP value types.
/// </summary>
internal static class ConversationAcpWireProjection
{
    public static JsonElement? SerializeToolCallContent(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(content, AcpJsonContext.Default.ListToolCallContent);
    }

    public static List<ToolCallContent>? DeserializeToolCallContent(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize(element.Value, AcpJsonContext.Default.ListToolCallContent);
    }

    public static JsonElement? SerializeToolCallLocations(IReadOnlyList<ToolCallLocation>? locations)
    {
        if (locations is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(locations, AcpJsonContext.Default.ListToolCallLocation);
    }

    public static List<ToolCallLocation>? DeserializeToolCallLocations(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize(element.Value, AcpJsonContext.Default.ListToolCallLocation);
    }

    public static ToolCallKind? ParseToolCallKind(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ToolCallKind(value);

    public static ToolCallStatus? ParseToolCallStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ToolCallStatus(value);

    public static PlanEntryStatus ParsePlanEntryStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? PlanEntryStatus.Pending : new PlanEntryStatus(value);

    public static PlanEntryPriority ParsePlanEntryPriority(string? value)
        => string.IsNullOrWhiteSpace(value) ? PlanEntryPriority.Low : new PlanEntryPriority(value);
}
