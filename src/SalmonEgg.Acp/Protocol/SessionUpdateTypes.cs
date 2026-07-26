using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Session/Update 通知的参数。
    /// 用于 Agent 向客户端发送会话更新。
    /// </summary>
    [JsonConverter(typeof(SessionUpdateParamsJsonConverter))]
    public class SessionUpdateParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 更新内容（多态类型）。
        /// 可以是文本、工具调用、计划、模式切换等。
        /// </summary>
        [JsonPropertyName("update")]
        public SessionUpdate Update { get; set; } = null!;

        /// <summary>
        /// 创建新的 SessionUpdateParams 实例。
        /// </summary>
        public SessionUpdateParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionUpdateParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="update">更新内容</param>
        public SessionUpdateParams(string sessionId, SessionUpdate update)
        {
            SessionId = sessionId;
            Update = update;
        }
    }

    /// <summary>
    /// 会话更新的基类/多态类型。
    /// 使用 JsonPolymorphic 特性支持不同类型的更新。
    /// </summary>
    [JsonPolymorphic(
        TypeDiscriminatorPropertyName = "sessionUpdate",
        UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType,
        IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(AgentMessageUpdate), "agent_message_chunk")]
    [JsonDerivedType(typeof(UserMessageUpdate), "user_message_chunk")]
    [JsonDerivedType(typeof(AgentThoughtUpdate), "agent_thought_chunk")]
    [JsonDerivedType(typeof(ToolCallUpdate), "tool_call")]
    [JsonDerivedType(typeof(ToolCallStatusUpdate), "tool_call_update")]
    [JsonDerivedType(typeof(PlanUpdate), "plan")]
    [JsonDerivedType(typeof(CurrentModeUpdate), "current_mode_update")]
    [JsonDerivedType(typeof(AvailableCommandsUpdate), "available_commands_update")]
    [JsonDerivedType(typeof(ConfigOptionUpdate), "config_option_update")]
    [JsonDerivedType(typeof(SessionInfoUpdate), "session_info_update")]
    [JsonDerivedType(typeof(UsageUpdate), "usage_update")]
    public class SessionUpdate : AcpProtocolObject
    {
        /// <summary>
        /// 未绑定到已知契约的前向兼容字段(含未知 sessionUpdate 判别值的完整 payload)。
        /// 协议要求未知更新原样保留并可 round-trip,client 不解释也不丢弃,
        /// 语义由 Agent 决定(AGENTS.md「协议宽松度不得反向收紧」)。
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>
        /// 未知更新类型的原始判别值;仅当此实例是未识别判别值回落的基类实例时非空。
        /// </summary>
        [JsonIgnore]
        public string? UnknownUpdateKind =>
            ExtensionData is not null
                && ExtensionData.TryGetValue("sessionUpdate", out var kind)
                && kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;
    }

    /// <summary>
    /// session/update 参数的读写。已知更新完全委托多态契约;未知判别值回落基类时
    /// STJ 会把判别值当多态元数据丢弃,这里把它补回 <see cref="SessionUpdate.ExtensionData"/>,
    /// 保证未知更新原样 round-trip 而不是被 client 静默降级。
    /// </summary>
    public sealed class SessionUpdateParamsJsonConverter : JsonConverter<SessionUpdateParams>
    {
        public override SessionUpdateParams? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("session/update params must be a JSON object.");
            }

            var sessionId = root.TryGetProperty("sessionId", out var sessionIdElement)
                    && sessionIdElement.ValueKind == JsonValueKind.String
                ? sessionIdElement.GetString() ?? string.Empty
                : string.Empty;

            SessionUpdate? update = null;
            if (root.TryGetProperty("update", out var updateElement) && updateElement.ValueKind == JsonValueKind.Object)
            {
                update = updateElement.Deserialize<SessionUpdate>(options);
                if (update is not null
                    && update.GetType() == typeof(SessionUpdate)
                    && updateElement.TryGetProperty("sessionUpdate", out var kindElement))
                {
                    update.ExtensionData ??= new Dictionary<string, JsonElement>();
                    update.ExtensionData["sessionUpdate"] = kindElement.Clone();
                }
            }

            return new SessionUpdateParams
            {
                SessionId = sessionId,
                Update = update!,
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, SessionUpdateParams value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", value.SessionId);
            if (value.Update is not null)
            {
                writer.WritePropertyName("update");
                JsonSerializer.Serialize(writer, value.Update, options);
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }

    public abstract class ContentChunkUpdate : SessionUpdate
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }
    }

    /// <summary>
    /// Usage update extension.
    /// Represents resource usage or other telemetry sent by the agent.
    /// </summary>
    public class UsageUpdate : SessionUpdate
    {
        [JsonPropertyName("used")]
        public ulong Used { get; set; }

        [JsonPropertyName("size")]
        public ulong Size { get; set; }

        [JsonPropertyName("cost")]
        public UsageCost? Cost { get; set; }
    }

    public class UsageCost : AcpProtocolObject
    {
        [JsonPropertyName("amount")]
        public double Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;
    }

    /// <summary>
    /// Agent 消息片段更新。
    /// 用于流式传输 Agent 的文本响应。
    /// </summary>
    public class AgentMessageUpdate : ContentChunkUpdate
    {
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }

        /// <summary>
        /// 创建新的 AgentMessageUpdate 实例。
        /// </summary>
        public AgentMessageUpdate()
        {
        }

        /// <summary>
        /// 创建新的 AgentMessageUpdate 实例。
        /// </summary>
        /// <param name="content">内容块</param>
        public AgentMessageUpdate(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// 用户消息片段更新（用于 session/load 回放或多端同步）。
    /// </summary>
    public class UserMessageUpdate : ContentChunkUpdate
    {
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }

        public UserMessageUpdate()
        {
        }

        public UserMessageUpdate(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// Agent 思考片段更新（通常不直接展示给用户，但必须可解析/可跳过）。
    /// </summary>
    public class AgentThoughtUpdate : ContentChunkUpdate
    {
        /// <summary>
        /// 消息内容块。
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }
    }

    /// <summary>
    /// 工具调用更新。
    /// 用于通知客户端工具调用的状态变化。
    /// </summary>
    public class ToolCallUpdate : SessionUpdate
    {
        /// <summary>
        /// 工具调用 ID。
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; set; }

        /// <summary>
        /// 工具调用类型。
        /// </summary>
        [JsonPropertyName("kind")]
        public ToolCallKind? Kind { get; set; }

        /// <summary>
        /// 工具调用状态。
        /// </summary>
        [JsonPropertyName("status")]
        public ToolCallStatus? Status { get; set; }

        /// <summary>
        /// 标题（可选）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 工具调用产生的内容。
        /// </summary>
        [JsonPropertyName("content")]
        public List<ToolCallContent>? Content { get; set; }

        /// <summary>
        /// 文件位置列表，表示工具调用影响的文件。
        /// </summary>
        [JsonPropertyName("locations")]
        public List<ToolCallLocation>? Locations { get; set; }

        /// <summary>
        /// 原始输入参数。
        /// </summary>
        [JsonPropertyName("rawInput")]
        public JsonElement? RawInput { get; set; }

        /// <summary>
        /// 原始输出结果。
        /// </summary>
        [JsonPropertyName("rawOutput")]
        public JsonElement? RawOutput { get; set; }

        /// <summary>
        /// 创建新的 ToolCallUpdate 实例。
        /// </summary>
        public ToolCallUpdate()
        {
        }

        /// <summary>
        /// 创建新的 ToolCallUpdate 实例。
        /// </summary>
        /// <param name="toolCallId">工具调用 ID</param>
        /// <param name="kind">工具调用类型</param>
        /// <param name="status">工具调用状态</param>
        /// <param name="title">标题</param>
        /// <param name="content">工具调用产生的内容</param>
        /// <param name="locations">文件位置列表</param>
        /// <param name="rawInput">原始输入参数</param>
        /// <param name="rawOutput">原始输出结果</param>
        public ToolCallUpdate(
            string? toolCallId = null,
            ToolCallKind? kind = null,
            ToolCallStatus? status = null,
            string? title = null,
            List<ToolCallContent>? content = null,
            List<ToolCallLocation>? locations = null,
            JsonElement? rawInput = null,
            JsonElement? rawOutput = null)
        {
            ToolCallId = toolCallId;
            Kind = kind;
            Status = status;
            Title = title;
            Content = content;
            Locations = locations;
            RawInput = rawInput;
            RawOutput = rawOutput;
        }
    }

    /// <summary>
    /// 计划更新。
    /// 用于通知客户端 Agent 的行动计划变化。
    /// </summary>
    public class PlanUpdate : SessionUpdate
    {
        private List<PlanEntry> _entries = new();

        /// <summary>
        /// 计划条目列表（用于 plan 类型的更新）。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("entries")]
        public List<PlanEntry> Entries
        {
            get => _entries;
            set => _entries = ValidateEntries(value);
        }

        /// <summary>
        /// 创建新的 PlanUpdate 实例。
        /// </summary>
        public PlanUpdate()
        {
        }

        /// <summary>
        /// 创建新的 PlanUpdate 实例。
        /// </summary>
        /// <param name="entries">计划条目列表</param>
        public PlanUpdate(List<PlanEntry> entries)
        {
            Entries = entries;
        }

        private static List<PlanEntry> ValidateEntries(List<PlanEntry>? entries)
        {
            if (entries is null)
            {
                throw new JsonException("Plan update entries must not be null.");
            }

            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    throw new JsonException("Plan update entries must not contain null items.");
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// 当前模式更新（current_mode_update）。
    /// ACP 会通过 session/update 通知发送当前模式的变化。
    /// </summary>
    public class CurrentModeUpdate : SessionUpdate
    {
        [JsonPropertyName("currentModeId")]
        public string ModeId { get; set; } = string.Empty;

        public CurrentModeUpdate()
        {
        }

        public CurrentModeUpdate(string modeId)
        {
            ModeId = modeId;
        }
    }

    /// <summary>
    /// 工具调用状态更新（tool_call_update）。
    /// 某些 Agent 不会在 tool_call update 中发送完整 toolCall 对象，只会推送状态与输出内容。
    /// </summary>
    public class ToolCallStatusUpdate : SessionUpdate
    {
        /// <summary>
        /// 工具调用 ID。
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; set; }

        /// <summary>
        /// 工具调用类型。
        /// </summary>
        [JsonPropertyName("kind")]
        public ToolCallKind? Kind { get; set; }

        /// <summary>
        /// 标题（可选）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 工具调用状态。
        /// </summary>
        [JsonPropertyName("status")]
        public ToolCallStatus? Status { get; set; }

        /// <summary>
        /// 工具调用产生的内容。
        /// </summary>
        [JsonPropertyName("content")]
        public List<ToolCallContent>? Content { get; set; }

        /// <summary>
        /// 文件位置列表，表示工具调用影响的文件。
        /// </summary>
        [JsonPropertyName("locations")]
        public List<ToolCallLocation>? Locations { get; set; }

        /// <summary>
        /// 原始输入参数。
        /// </summary>
        [JsonPropertyName("rawInput")]
        public JsonElement? RawInput { get; set; }

        /// <summary>
        /// 原始输出结果。
        /// </summary>
        [JsonPropertyName("rawOutput")]
        public JsonElement? RawOutput { get; set; }
    }

    /// <summary>
    /// 配置选项更新（config_option_update）。
    /// </summary>
    public class ConfigOptionUpdate : SessionUpdate
    {
        /// <summary>
        /// 配置选项列表。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }
    }

    /// <summary>
    /// 会话信息更新（session_info_update）。
    /// </summary>
    public class SessionInfoUpdate : SessionUpdate
    {
        private string? _title;
        private string? _updatedAt;

        /// <summary>
        /// 会话标题（可选）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                HasTitle = true;
            }
        }

        [JsonIgnore]
        public bool HasTitle { get; set; }

        /// <summary>
        /// 最近更新时间（UTC iso8601）。
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt
        {
            get => _updatedAt;
            set
            {
                _updatedAt = value;
                HasUpdatedAt = true;
            }
        }

        [JsonIgnore]
        public bool HasUpdatedAt { get; set; }
    }
}
