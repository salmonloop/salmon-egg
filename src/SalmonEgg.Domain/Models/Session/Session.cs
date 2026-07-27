using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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
        /// 会话显示名称缓存。远程 ACP 会话必须由 session metadata title 投影写入。
        /// </summary>
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

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
    /// Domain-owned session history entry.
    /// Captures a projection of an ACP session/update for in-process recovery without
    /// retaining protocol wire types in the Domain model.
    /// </summary>
    public sealed class SessionUpdateEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ACP sessionUpdate discriminator (for example agent_message_chunk, plan).
        /// </summary>
        public string SessionUpdateType { get; set; } = string.Empty;

        /// <summary>
        /// Flattened text for content-bearing updates (agent/user/thought message chunks).
        /// Non-text content is intentionally not retained in Domain history.
        /// </summary>
        public string? TextContent { get; set; }

        /// <summary>
        /// Content block type when TextContent is present (for example text).
        /// </summary>
        public string? ContentType { get; set; }

        public IReadOnlyList<SessionPlanHistoryEntry>? PlanEntries { get; set; }

        public string? ToolCallId { get; set; }

        public string? Title { get; set; }

        /// <summary>
        /// Open ACP tool-call kind wire value.
        /// </summary>
        public string? ToolCallKind { get; set; }

        /// <summary>
        /// Open ACP tool-call status wire value.
        /// </summary>
        public string? ToolCallStatus { get; set; }

        public string? ModeId { get; set; }

        public SessionUpdateEntry()
        {
        }

        public static SessionUpdateEntry CreateTextMessage(string text)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "agent_message_chunk",
                ContentType = "text",
                TextContent = text ?? string.Empty,
                Timestamp = DateTime.UtcNow
            };
        }

        public static SessionUpdateEntry CreatePlan(IEnumerable<SessionPlanHistoryEntry> entries)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "plan",
                PlanEntries = entries?.ToList() ?? new List<SessionPlanHistoryEntry>(),
                Timestamp = DateTime.UtcNow
            };
        }

        public static SessionUpdateEntry CreateModeChange(string modeId)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "current_mode_update",
                ModeId = modeId,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Domain projection of a plan entry retained in session history.
    /// Status/priority are open ACP wire strings.
    /// </summary>
    public sealed class SessionPlanHistoryEntry
    {
        public string Content { get; set; } = string.Empty;

        public string Status { get; set; } = "pending";

        public string Priority { get; set; } = "medium";

        public SessionPlanHistoryEntry()
        {
        }

        public SessionPlanHistoryEntry(string content, string? status = null, string? priority = null)
        {
            Content = content ?? string.Empty;
            Status = string.IsNullOrWhiteSpace(status) ? "pending" : status;
            Priority = string.IsNullOrWhiteSpace(priority) ? "medium" : priority;
        }
    }
}
