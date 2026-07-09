using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

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
        var json = JsonSerializer.Serialize(content, ToolCallContentJsonContext.Default.ToolCallContent);
        return JsonSerializer.Deserialize(json, ToolCallContentJsonContext.Default.ToolCallContent)
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
            ? JsonSerializer.Serialize(content, ToolCallContentJsonContext.Default.IReadOnlyListToolCallContent)
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
                ToolCallContentJsonContext.Default.ToolCallContent),
            ToolCallLocation toolCallLocation => JsonSerializer.Serialize(
                toolCallLocation,
                ToolCallContentJsonContext.Default.ToolCallLocation),
            _ => throw new InvalidOperationException($"Unsupported tool call snapshot value type: {typeof(T).FullName}")
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolCallContent))]
[JsonSerializable(typeof(ContentToolCallContent))]
[JsonSerializable(typeof(DiffToolCallContent))]
[JsonSerializable(typeof(TerminalToolCallContent))]
[JsonSerializable(typeof(ToolCallLocation))]
[JsonSerializable(typeof(IReadOnlyList<ToolCallContent>))]
[JsonSerializable(typeof(List<ToolCallContent>))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextContentBlock))]
[JsonSerializable(typeof(ImageContentBlock))]
[JsonSerializable(typeof(AudioContentBlock))]
[JsonSerializable(typeof(ResourceContentBlock))]
[JsonSerializable(typeof(ResourceLinkContentBlock))]
[JsonSerializable(typeof(Annotations))]
[JsonSerializable(typeof(EmbeddedResource))]
internal partial class ToolCallContentJsonContext : JsonSerializerContext
{
}
