using System;
using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

/// <summary>
/// Presentation-owned projections between Domain conversation snapshots (opaque JSON /
/// open wire strings) and ACP tool-call DTOs used by chat ViewModels.
/// </summary>
internal static class ToolCallContentSnapshots
{
    public static JsonElement? ToDomainContent(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(content, AcpJsonContext.Default.ListToolCallContent);
    }

    public static List<ToolCallContent>? FromDomainContent(JsonElement? content)
    {
        if (content is null || content.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize(content.Value, AcpJsonContext.Default.ListToolCallContent);
    }

    public static JsonElement? ToDomainLocations(IReadOnlyList<ToolCallLocation>? locations)
    {
        if (locations is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(locations, AcpJsonContext.Default.ListToolCallLocation);
    }

    public static List<ToolCallLocation>? FromDomainLocations(JsonElement? locations)
    {
        if (locations is null || locations.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize(locations.Value, AcpJsonContext.Default.ListToolCallLocation);
    }

    public static JsonElement? CloneDomainPayload(JsonElement? payload)
    {
        if (payload is null || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return payload.Value.Clone();
    }

    public static bool DomainPayloadEquals(JsonElement? left, JsonElement? right)
    {
        if (left is null || left.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return right is null || right.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        }

        if (right is null || right.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return string.Equals(left.Value.GetRawText(), right.Value.GetRawText(), StringComparison.Ordinal);
    }

    public static List<ToolCallContent>? CloneList(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is null)
        {
            return null;
        }

        var cloned = new List<ToolCallContent>(content.Count);
        foreach (var item in content)
        {
            cloned.Add(Clone(item));
        }

        return cloned;
    }

    public static ToolCallContent Clone(ToolCallContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var json = JsonSerializer.Serialize(content, AcpJsonContext.Default.ToolCallContent);
        return JsonSerializer.Deserialize(json, AcpJsonContext.Default.ToolCallContent)
            ?? throw new InvalidOperationException("Failed to clone tool call content.");
    }

    public static bool SequenceEquals(
        IReadOnlyList<ToolCallContent>? left,
        IReadOnlyList<ToolCallContent>? right)
        => DomainPayloadEquals(ToDomainContent(left), ToDomainContent(right));

    public static List<ToolCallLocation>? CloneLocations(IReadOnlyList<ToolCallLocation>? locations)
    {
        if (locations is null)
        {
            return null;
        }

        var cloned = new List<ToolCallLocation>(locations.Count);
        foreach (var location in locations)
        {
            cloned.Add(new ToolCallLocation(location.Path, location.Line));
        }

        return cloned;
    }

    public static bool LocationsSequenceEquals(
        IReadOnlyList<ToolCallLocation>? left,
        IReadOnlyList<ToolCallLocation>? right)
        => DomainPayloadEquals(ToDomainLocations(left), ToDomainLocations(right));

    public static string? SerializePayload(IReadOnlyList<ToolCallContent>? content)
        => content is { Count: > 0 }
            ? JsonSerializer.Serialize(content, AcpJsonContext.Default.IReadOnlyListToolCallContent)
            : null;

    public static ToolCallKind? ParseKind(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ToolCallKind(value);

    public static ToolCallStatus? ParseStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ToolCallStatus(value);

    public static string? FormatKind(ToolCallKind? kind) => kind?.ToString();

    public static string? FormatStatus(ToolCallStatus? status) => status?.ToString();
}
