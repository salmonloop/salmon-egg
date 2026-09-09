using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Serialization;

// The v2 schema explicitly grants recovery to patch fields, not to the ids or streamed items.
// Keep this on the negotiated resolver so v1 and unversioned serializers retain their contracts.
internal static class SessionProjectionWireContract
{
    internal static void Apply(JsonTypeInfo info)
    {
        if (typeof(WholeMessageUpdate).IsAssignableFrom(info.Type))
        {
            Property(info, "content").CustomConverter = new DefaultableListJsonConverter<ContentBlock>();
            DefaultMetadata(info);
        }
        else if (typeof(ContentChunkUpdate).IsAssignableFrom(info.Type))
        {
            Property(info, "content").IsRequired = true;
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(ToolCallStatusUpdate))
        {
            RequireId(info, "toolCallId", static value => ((ToolCallStatusUpdate)value).ToolCallId);
            Property(info, "title").CustomConverter = new DefaultableStringJsonConverter();
            Property(info, "kind").CustomConverter = new DefaultableNullableJsonConverter<ToolCallKind>();
            Property(info, "status").CustomConverter = new DefaultableNullableJsonConverter<ToolCallStatus>();
            Property(info, "content").CustomConverter = new DefaultableListJsonConverter<ToolCallContent>();
            Property(info, "locations").CustomConverter = new DefaultableListJsonConverter<ToolCallLocation>();
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(ToolCallContentChunkUpdate))
        {
            RequireId(info, "toolCallId", static value => ((ToolCallContentChunkUpdate)value).ToolCallId);
            Property(info, "content").IsRequired = true;
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(TerminalSessionUpdate))
        {
            ApplyTerminal(info);
        }
        else if (info.Type == typeof(TerminalOutputChunkSessionUpdate))
        {
            RequireId(info, "terminalId", static value => ((TerminalOutputChunkSessionUpdate)value).TerminalId);
            RequireId(info, "data", static value => ((TerminalOutputChunkSessionUpdate)value).Data);
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(TerminalOutput))
        {
            RequireId(info, "data", static value => ((TerminalOutput)value).Data);
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(TerminalExitStatus))
        {
            Property(info, "exitCode").CustomConverter = new DefaultableNullableJsonConverter<uint>();
            Property(info, "signal").CustomConverter = new DefaultableStringJsonConverter();
            DefaultMetadata(info);
        }
        else if (info.Type == typeof(ToolCallLocation))
        {
            RequireId(info, "path", static value => ((ToolCallLocation)value).Path);
            Property(info, "line").CustomConverter = new DefaultableNullableJsonConverter<uint>();
            DefaultMetadata(info);
        }
    }

    private static void ApplyTerminal(JsonTypeInfo info)
    {
        RequireId(info, "terminalId", static value => ((TerminalSessionUpdate)value).TerminalId);
        Property(info, "command").CustomConverter = new DefaultableStringJsonConverter();
        Property(info, "cwd").CustomConverter = new DefaultableStringJsonConverter();
        Property(info, "output").CustomConverter = new DefaultableObjectJsonConverter<TerminalOutput>();
        Property(info, "exitStatus").CustomConverter = new DefaultableObjectJsonConverter<TerminalExitStatus>();
        DefaultMetadata(info);
    }

    private static void RequireId(JsonTypeInfo info, string name, Func<object, string?> read)
    {
        Property(info, name).IsRequired = true;
        var previous = info.OnDeserialized;
        info.OnDeserialized = value =>
        {
            previous?.Invoke(value);
            if (read(value) is null)
            {
                throw new JsonException($"ACP v2 update requires string '{name}'.");
            }
        };
    }

    private static void DefaultMetadata(JsonTypeInfo info)
        => Property(info, "_meta").CustomConverter = new DefaultableObjectJsonConverter<Dictionary<string, object?>>();

    private static JsonPropertyInfo Property(JsonTypeInfo info, string name)
    {
        foreach (var property in info.Properties)
        {
            if (property.Name == name)
            {
                return property;
            }
        }

        throw new InvalidOperationException($"Missing generated property {info.Type.Name}.{name}.");
    }
}
