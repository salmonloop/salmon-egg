using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Models.Plan;

namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// 会话类。
    /// 表示与 Agent 的一次完整对话会话，包含会话状态、历史和配置。
    /// </summary>
    public class Session
    {
        /// <summary>
        /// 会话的唯一标识符。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话的当前工作模式。
        /// </summary>
        [JsonPropertyName("mode")]
        public SessionModeState Mode { get; set; } = new SessionModeState();

        /// <summary>
        /// 会话的历史记录列表。
        /// 包含所有的消息、工具调用、计划更新等。
        /// </summary>
        [JsonPropertyName("history")]
        public List<SessionUpdateEntry> History { get; set; } = new List<SessionUpdateEntry>();

        /// <summary>
        /// 会话的当前状态。
        /// </summary>
        [JsonPropertyName("state")]
        public SessionState State { get; set; } = SessionState.Active;

        /// <summary>
        /// 会话的创建时间。
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 会话的最后活动时间。
        /// </summary>
        [JsonPropertyName("lastActivityAt")]
        public DateTime LastActivityAt { get; set; }

        /// <summary>
        /// 会话的工作目录。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        /// <summary>
        /// MCP 服务器配置列表。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public object? McpServers { get; set; }

        /// <summary>
        /// 会话的配置选项。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public object? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的会话实例。
        /// </summary>
        public Session()
        {
            CreatedAt = DateTime.UtcNow;
            LastActivityAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 创建新的会话实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        public Session(string sessionId, string? cwd = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            CreatedAt = DateTime.UtcNow;
            LastActivityAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 更新会话的最后活动时间。
        /// </summary>
        public void UpdateActivity()
        {
            LastActivityAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 向会话历史添加条目。
        /// </summary>
        /// <param name="entry">要添加的条目</param>
        public void AddHistoryEntry(SessionUpdateEntry entry)
        {
            History.Add(entry);
            UpdateActivity();
        }

        /// <summary>
        /// 获取会话的最后 N 个历史条目。
        /// </summary>
        /// <param name="count">要获取的条目数量</param>
        /// <returns>历史条目列表</returns>
        public List<SessionUpdateEntry> GetRecentHistory(int count = 10)
        {
            var recentCount = Math.Min(count, History.Count);
            return History.GetRange(History.Count - recentCount, recentCount);
        }

        /// <summary>
        /// 判断会话是否活跃。
        /// </summary>
        public bool IsActive => State == SessionState.Active || State == SessionState.Waiting;

        /// <summary>
        /// 判断会话是否已完成或终止。
        /// </summary>
        public bool IsTerminated => State == SessionState.Completed ||
                                    State == SessionState.Cancelled ||
                                    State == SessionState.Error;
    }

    /// <summary>
    /// 会话更新条目类。
    /// 表示会话历史中的一个条目，可以是消息、工具调用、计划更新等。
    /// </summary>
    public class SessionUpdateEntry
    {
        /// <summary>
        /// 条目的时间戳。
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 更新类型。
        /// </summary>
        [JsonPropertyName("sessionUpdate")]
        public string SessionUpdateType { get; set; } = string.Empty;

        /// <summary>
        /// 文本内容（用于 agent_message 类型的更新）。
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }

        /// <summary>
        /// 计划条目列表（用于 plan 类型的更新）。
        /// </summary>
        [JsonPropertyName("entries")]
        public List<PlanEntry>? Entries { get; set; }

        /// <summary>
        /// 工具调用 ID（用于 tool_call 类型的更新）。
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; set; }

        /// <summary>
        /// 工具调用数据（用于 tool_call 类型的更新）。
        /// </summary>
        [JsonPropertyName("toolCall")]
        public object? ToolCall { get; set; }

        /// <summary>
        /// 标题（用于某些类型的更新）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 工具调用类型（用于 tool_call 类型的更新）。
        /// </summary>
        [JsonPropertyName("kind")]
        public ToolCallKind? Kind { get; set; }

        /// <summary>
        /// 工具调用状态（用于 tool_call 类型的更新）。
        /// </summary>
        [JsonPropertyName("status")]
        public ToolCallStatus? Status { get; set; }

        /// <summary>
        /// 模式 ID（用于 mode_change 类型的更新）。
        /// </summary>
        [JsonPropertyName("modeId")]
        public string? ModeId { get; set; }

        /// <summary>
        /// 配置选项（用于 config_update 类型的更新）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public object? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的会话更新条目实例。
        /// </summary>
        public SessionUpdateEntry()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// 创建新的消息类型更新条目。
        /// </summary>
        /// <param name="content">内容块</param>
        /// <returns>会话更新条目</returns>
        public static SessionUpdateEntry CreateMessage(ContentBlock content)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "agent_message_chunk",
                Content = content,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建新的工具调用类型更新条目。
        /// </summary>
        /// <param name="toolCallId">工具调用 ID</param>
        /// <param name="toolCall">工具调用数据</param>
        /// <param name="kind">工具调用类型</param>
        /// <param name="status">工具调用状态</param>
        /// <returns>会话更新条目</returns>
        public static SessionUpdateEntry CreateToolCall(
            string toolCallId,
            object toolCall,
            ToolCallKind kind,
            ToolCallStatus status)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "tool_call",
                ToolCallId = toolCallId,
                ToolCall = toolCall,
                Kind = kind,
                Status = status,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建新的计划类型更新条目。
        /// </summary>
        /// <param name="entries">计划条目列表</param>
        /// <returns>会话更新条目</returns>
        public static SessionUpdateEntry CreatePlan(List<PlanEntry> entries)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "plan",
                Entries = entries,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建新的模式切换类型更新条目。
        /// </summary>
        /// <param name="modeId">新的模式 ID</param>
        /// <returns>会话更新条目</returns>
        public static SessionUpdateEntry CreateModeChange(string modeId)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "mode_change",
                ModeId = modeId,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建新的配置更新类型更新条目。
        /// </summary>
        /// <param name="configOptions">配置选项</param>
        /// <returns>会话更新条目</returns>
        public static SessionUpdateEntry CreateConfigUpdate(object configOptions)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "config_update",
                ConfigOptions = configOptions,
                Timestamp = DateTime.UtcNow
            };
        }
    }
}
