using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Tool
{
    /// <summary>
    /// 工具调用的状态枚举。
    /// 表示工具调用在生命周期中的当前状态。
    /// </summary>
    [JsonConverter(typeof(ToolCallStatusJsonConverter))]
    public enum ToolCallStatus
    {
        /// <summary>
        /// 工具调用已创建但尚未开始执行。
        /// </summary>
        [JsonPropertyName("pending")]
        Pending,

        /// <summary>
        /// 工具调用正在执行中。
        /// </summary>
        [JsonPropertyName("in_progress")]
        InProgress,

        /// <summary>
        /// 工具调用已成功完成。
        /// </summary>
        [JsonPropertyName("completed")]
        Completed,

        /// <summary>
        /// 工具调用失败或出错。
        /// </summary>
        [JsonPropertyName("failed")]
        Failed,

        /// <summary>
        /// 工具调用已被取消。
        /// </summary>
        [JsonPropertyName("cancelled")]
        Cancelled
    }

    /// <summary>
    /// 工具调用的类型枚举。
    /// 表示工具执行的具体操作类型。
    /// </summary>
    [JsonConverter(typeof(ToolCallKindJsonConverter))]
    public enum ToolCallKind
    {
        /// <summary>
        /// 文件读取操作。
        /// </summary>
        [JsonPropertyName("read")]
        Read,

        /// <summary>
        /// 文件编辑操作。
        /// </summary>
        [JsonPropertyName("edit")]
        Edit,

        /// <summary>
        /// 文件删除操作。
        /// </summary>
        [JsonPropertyName("delete")]
        Delete,

        /// <summary>
        /// 文件移动或重命名操作。
        /// </summary>
        [JsonPropertyName("move")]
        Move,

        /// <summary>
        /// 搜索操作。
        /// </summary>
        [JsonPropertyName("search")]
        Search,

        /// <summary>
        /// 终端命令执行操作。
        /// </summary>
        [JsonPropertyName("execute")]
        Execute,

        /// <summary>
        /// 会话模式切换操作。
        /// </summary>
        [JsonPropertyName("switch_mode")]
        SwitchMode,

        /// <summary>
        /// 思考或推理操作（不执行实际动作）。
        /// </summary>
        [JsonPropertyName("think")]
        Think,

        /// <summary>
        /// 网络请求或数据获取操作。
        /// </summary>
        [JsonPropertyName("fetch")]
        Fetch,

        /// <summary>
        /// 其他未分类的工具调用。
        /// </summary>
        [JsonPropertyName("other")]
        Other
    }

    public sealed class ToolCallStatusJsonConverter : JsonPropertyNameEnumJsonConverter<ToolCallStatus>
    {
    }

    public sealed class ToolCallKindJsonConverter : JsonPropertyNameEnumJsonConverter<ToolCallKind>
    {
    }

    public abstract class JsonPropertyNameEnumJsonConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ReadMap = BuildReadMap();
        private static readonly Dictionary<TEnum, string> WriteMap = BuildWriteMap();

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => ReadFromString(reader.GetString()),
                JsonTokenType.Number when reader.TryGetInt64(out var value) => (TEnum)Enum.ToObject(typeof(TEnum), value),
                _ => default
            };
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (WriteMap.TryGetValue(value, out var serialized))
            {
                writer.WriteStringValue(serialized);
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

            if (ReadMap.TryGetValue(value, out var match))
            {
                return match;
            }

            var normalized = value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal);
            return ReadMap.TryGetValue(normalized, out match)
                ? match
                : Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
                    ? parsed
                    : default;
        }

        private static Dictionary<string, TEnum> BuildReadMap()
        {
            var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var enumValue = (TEnum)field.GetValue(null)!;
                map[field.Name] = enumValue;

                var jsonName = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                if (!string.IsNullOrWhiteSpace(jsonName))
                {
                    map[jsonName] = enumValue;
                }
            }

            return map;
        }

        private static Dictionary<TEnum, string> BuildWriteMap()
        {
            var map = new Dictionary<TEnum, string>();
            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var enumValue = (TEnum)field.GetValue(null)!;
                var jsonName = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                map[enumValue] = !string.IsNullOrWhiteSpace(jsonName)
                    ? jsonName
                    : ToSnakeCase(field.Name);
            }

            return map;
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var chars = new List<char>(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsUpper(current))
                {
                    if (i > 0 && value[i - 1] != '_' && !char.IsUpper(value[i - 1]))
                    {
                        chars.Add('_');
                    }

                    chars.Add(char.ToLower(current, CultureInfo.InvariantCulture));
                }
                else
                {
                    chars.Add(current);
                }
            }

            return new string(chars.ToArray());
        }
    }
}
