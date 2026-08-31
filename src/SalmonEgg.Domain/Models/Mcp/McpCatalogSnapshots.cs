using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace SalmonEgg.Domain.Models.Mcp;

/// <summary>
/// Deep-copy helpers for app-local MCP catalog payloads.
/// </summary>
public static class McpCatalogSnapshots
{
    public static Dictionary<string, object?>? CloneMeta(Dictionary<string, object?>? meta)
    {
        if (meta is null)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(meta.Count);
        foreach (var item in meta)
        {
            result[item.Key] = CloneValue(item.Value);
        }

        return result;
    }

    public static List<string> CloneArgs(IEnumerable<string>? args)
        => args is null ? new List<string>() : new List<string>(args);

    public static List<McpCatalogNameValue> CloneNameValues(IEnumerable<McpCatalogNameValue>? values)
    {
        if (values is null)
        {
            return new List<McpCatalogNameValue>();
        }

        var result = new List<McpCatalogNameValue>();
        foreach (var value in values)
        {
            result.Add(value.Clone());
        }

        return result;
    }

    private static object? CloneValue(object? value)
        => value switch
        {
            null => null,
            JsonElement element => element.Clone(),
            JsonDocument document => document.RootElement.Clone(),
            string text => text,
            bool flag => flag,
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            float number => number,
            double number => number,
            decimal number => number,
            IReadOnlyDictionary<string, object?> readOnlyDictionary => CloneMeta(new Dictionary<string, object?>(readOnlyDictionary)),
            IDictionary dictionary => CloneDictionary(dictionary),
            IEnumerable values when value is not string => CloneArray(values),
            _ => value
        };

    private static List<object?> CloneArray(IEnumerable values)
    {
        var result = new List<object?>();
        foreach (var item in values)
        {
            result.Add(CloneValue(item));
        }

        return result;
    }

    private static Dictionary<string, object?> CloneDictionary(IDictionary dictionary)
    {
        var result = new Dictionary<string, object?>();
        foreach (DictionaryEntry item in dictionary)
        {
            if (item.Key is not string key)
            {
                continue;
            }

            result[key] = CloneValue(item.Value);
        }

        return result;
    }
}
