using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Tool
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
        Pending,

        /// <summary>
        /// 工具调用正在执行中。
        /// </summary>
        InProgress,

        /// <summary>
        /// 工具调用已成功完成。
        /// </summary>
        Completed,

        /// <summary>
        /// 工具调用失败或出错。
        /// </summary>
        Failed,

        /// <summary>
        /// 工具调用已被取消。
        /// </summary>
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
        Read,

        /// <summary>
        /// 文件编辑操作。
        /// </summary>
        Edit,

        /// <summary>
        /// 文件删除操作。
        /// </summary>
        Delete,

        /// <summary>
        /// 文件移动或重命名操作。
        /// </summary>
        Move,

        /// <summary>
        /// 搜索操作。
        /// </summary>
        Search,

        /// <summary>
        /// 终端命令执行操作。
        /// </summary>
        Execute,

        /// <summary>
        /// 会话模式切换操作。
        /// </summary>
        SwitchMode,

        /// <summary>
        /// 思考或推理操作（不执行实际动作）。
        /// </summary>
        Think,

        /// <summary>
        /// 网络请求或数据获取操作。
        /// </summary>
        Fetch,

        /// <summary>
        /// 其他未分类的工具调用。
        /// </summary>
        Other
    }

    public sealed class ToolCallStatusJsonConverter : JsonConverter<ToolCallStatus>
    {
        public override ToolCallStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Tool call status must be a string.");
            }

            return reader.GetString() switch
            {
                "pending" => ToolCallStatus.Pending,
                "in_progress" => ToolCallStatus.InProgress,
                "completed" => ToolCallStatus.Completed,
                "failed" => ToolCallStatus.Failed,
                "cancelled" => ToolCallStatus.Cancelled,
                var value => throw new JsonException($"Unsupported tool call status '{value}'.")
            };
        }

        public override void Write(Utf8JsonWriter writer, ToolCallStatus value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ToolCallStatus.Pending => "pending",
                ToolCallStatus.InProgress => "in_progress",
                ToolCallStatus.Completed => "completed",
                ToolCallStatus.Failed => "failed",
                ToolCallStatus.Cancelled => "cancelled",
                _ => throw new JsonException($"Unsupported tool call status '{value}'.")
            });
        }
    }

    public sealed class ToolCallKindJsonConverter : JsonConverter<ToolCallKind>
    {
        public override ToolCallKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Tool call kind must be a string.");
            }

            return reader.GetString() switch
            {
                "read" => ToolCallKind.Read,
                "edit" => ToolCallKind.Edit,
                "delete" => ToolCallKind.Delete,
                "move" => ToolCallKind.Move,
                "search" => ToolCallKind.Search,
                "execute" => ToolCallKind.Execute,
                "switch_mode" => ToolCallKind.SwitchMode,
                "think" => ToolCallKind.Think,
                "fetch" => ToolCallKind.Fetch,
                "other" => ToolCallKind.Other,
                var value => throw new JsonException($"Unsupported tool call kind '{value}'.")
            };
        }

        public override void Write(Utf8JsonWriter writer, ToolCallKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ToolCallKind.Read => "read",
                ToolCallKind.Edit => "edit",
                ToolCallKind.Delete => "delete",
                ToolCallKind.Move => "move",
                ToolCallKind.Search => "search",
                ToolCallKind.Execute => "execute",
                ToolCallKind.SwitchMode => "switch_mode",
                ToolCallKind.Think => "think",
                ToolCallKind.Fetch => "fetch",
                ToolCallKind.Other => "other",
                _ => throw new JsonException($"Unsupported tool call kind '{value}'.")
            });
        }
    }
}
