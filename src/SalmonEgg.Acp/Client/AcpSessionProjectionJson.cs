using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Client;

internal static class AcpSessionProjectionJson
{
    internal static JsonElement Store<T>(T value)
        => JsonSerializer.SerializeToElement(value, AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<T>());

    internal static T? Read<T>(JsonElement? value) where T : class
        => value?.Deserialize(AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<T>());

    internal static ImmutableArray<T> ReadArray<T>(ImmutableArray<JsonElement> values) where T : class
    {
        var result = ImmutableArray.CreateBuilder<T>(values.Length);
        foreach (var value in values)
        {
            result.Add(Read<T>(value)!);
        }
        return result.MoveToImmutable();
    }

    internal static ImmutableArray<JsonElement> ReadPatchArray<T>(JsonElement payload, string name) where T : class
    {
        if (!payload.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<JsonElement>();
        var typeInfo = AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<T>();
        foreach (var value in values.EnumerateArray())
        {
            try
            {
                if (value.Deserialize(typeInfo) is not null)
                {
                    result.Add(value.Clone());
                }
            }
            catch (JsonException)
            {
                // These patch arrays alone carry skip-invalid-items in the pinned v2 schema.
            }
        }
        return result.ToImmutable();
    }

    internal static JsonElement? ReadMetadata(JsonElement payload)
        => payload.TryGetProperty("_meta", out var value) && value.ValueKind == JsonValueKind.Object
            ? value.Clone()
            : null;

    internal static JsonElement? CloneValue(JsonElement? value)
        => value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } item ? item.Clone() : null;

    internal static byte[] DecodeOutput(string data)
    {
        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException error)
        {
            throw new JsonException("ACP terminal output must contain base64-encoded bytes.", error);
        }
    }
}
