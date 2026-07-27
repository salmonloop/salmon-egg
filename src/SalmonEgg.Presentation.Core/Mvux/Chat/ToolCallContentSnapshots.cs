using System.Text.Json;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

/// <summary>
/// Clones and compares ACP tool-call content via the SDK source-generated context.
/// Host code must not re-source-generate Acp polymorphic types; that would require
/// public converter types and leaks SDK implementation details.
/// </summary>
internal static class ToolCallContentSnapshots
{
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
        => JsonSequenceEquals(left, right);

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
        => JsonSequenceEquals(left, right);

    public static string? SerializePayload(IReadOnlyList<ToolCallContent>? content)
        => content is { Count: > 0 }
            ? JsonSerializer.Serialize(content, AcpJsonContext.Default.IReadOnlyListToolCallContent)
            : null;

    private static bool JsonSequenceEquals<T>(
        IReadOnlyList<T>? left,
        IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(SerializeValue(left[i]), SerializeValue(right[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string SerializeValue<T>(T value)
        => value switch
        {
            ToolCallContent toolCallContent => JsonSerializer.Serialize(
                toolCallContent,
                AcpJsonContext.Default.ToolCallContent),
            ToolCallLocation toolCallLocation => JsonSerializer.Serialize(
                toolCallLocation,
                AcpJsonContext.Default.ToolCallLocation),
            _ => throw new InvalidOperationException($"Unsupported tool call snapshot value type: {typeof(T).FullName}")
        };
}
