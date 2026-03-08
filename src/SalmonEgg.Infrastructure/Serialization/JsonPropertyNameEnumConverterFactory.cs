using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Infrastructure.Serialization;

/// <summary>
/// A JsonConverterFactory that enables enum (and nullable enum) values to be read/written using
/// <see cref="JsonPropertyNameAttribute"/> values applied to enum members.
///
/// This project historically annotated enum members with JsonPropertyName (e.g. "in_progress").
/// System.Text.Json does not honor JsonPropertyName on enum values by default, which can break ACP
/// payload deserialization (notably session/update plan entries).
/// </summary>
public sealed class JsonPropertyNameEnumConverterFactory : JsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, JsonConverter> Cache = new();

    public override bool CanConvert(Type typeToConvert)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return t.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return Cache.GetOrAdd(typeToConvert, static t =>
        {
            var underlying = Nullable.GetUnderlyingType(t);
            var enumType = underlying ?? t;

            var converterType = typeof(JsonPropertyNameEnumConverter<>).MakeGenericType(enumType);
            var converter = (JsonConverter)Activator.CreateInstance(converterType)!;

            if (underlying is null)
            {
                return converter;
            }

            var nullableWrapperType = typeof(NullableEnumConverter<>).MakeGenericType(enumType);
            return (JsonConverter)Activator.CreateInstance(nullableWrapperType, converter)!;
        });
    }

    private sealed class NullableEnumConverter<TEnum> : JsonConverter<TEnum?>
        where TEnum : struct, Enum
    {
        private readonly JsonConverter<TEnum> _inner;

        public NullableEnumConverter(JsonConverter inner)
        {
            _inner = (JsonConverter<TEnum>)inner;
        }

        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            return _inner.Read(ref reader, typeof(TEnum), options);
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            _inner.Write(writer, value.Value, options);
        }
    }

    private sealed class JsonPropertyNameEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ReadMap = BuildReadMap();
        private static readonly Dictionary<TEnum, string> WriteMap = BuildWriteMap();

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return reader.TokenType switch
                {
                    JsonTokenType.String => ReadFromString(reader.GetString()),
                    JsonTokenType.Number when reader.TryGetInt64(out var i) => ReadFromNumber(i),
                    _ => default
                };
            }
            catch
            {
                // Never let enum parsing crash ACP message handling.
                return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (WriteMap.TryGetValue(value, out var s))
            {
                writer.WriteStringValue(s);
                return;
            }

            writer.WriteStringValue(value.ToString());
        }

        private static TEnum ReadFromString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return default;
            }

            if (ReadMap.TryGetValue(value, out var e))
            {
                return e;
            }

            // Common fallbacks
            var normalized = Normalize(value);
            if (ReadMap.TryGetValue(normalized, out e))
            {
                return e;
            }

            return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : default;
        }

        private static TEnum ReadFromNumber(long value)
        {
            try
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), value);
            }
            catch
            {
                return default;
            }
        }

        private static Dictionary<string, TEnum> BuildReadMap()
        {
            var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            var fields = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                var enumValue = (TEnum)field.GetValue(null)!;

                // 1) Enum name
                map[field.Name] = enumValue;
                map[Normalize(field.Name)] = enumValue;

                // 2) JsonPropertyName on member (used by this repo historically)
                var jsonName = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                if (!string.IsNullOrWhiteSpace(jsonName))
                {
                    map[jsonName] = enumValue;
                    map[Normalize(jsonName)] = enumValue;
                }
            }

            return map;
        }

        private static Dictionary<TEnum, string> BuildWriteMap()
        {
            var map = new Dictionary<TEnum, string>();
            var fields = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                var enumValue = (TEnum)field.GetValue(null)!;
                var jsonName = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;

                map[enumValue] = !string.IsNullOrWhiteSpace(jsonName) ? jsonName : ToSnakeCase(field.Name);
            }

            return map;
        }

        private static string Normalize(string s) =>
            s.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal);

        private static string ToSnakeCase(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            var chars = new List<char>(s.Length + 8);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && s[i - 1] != '_' && !char.IsUpper(s[i - 1]))
                    {
                        chars.Add('_');
                    }

                    chars.Add(char.ToLower(c, CultureInfo.InvariantCulture));
                }
                else
                {
                    chars.Add(c);
                }
            }

            return new string(chars.ToArray());
        }
    }
}

