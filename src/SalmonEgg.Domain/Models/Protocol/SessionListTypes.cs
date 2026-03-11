using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Session/List 方法的请求参数。
    /// 用于列出已存在的会话。
    /// </summary>
    public class ListSessionsParams
    {
        /// <summary>
        /// 工作目录过滤器（可选）。
        /// 必须是绝对路径。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        /// <summary>
        /// 分页游标（可选）。
        /// </summary>
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }

        /// <summary>
        /// 创建新的 ListSessionsParams 实例。
        /// </summary>
        public ListSessionsParams()
        {
        }
    }

    /// <summary>
    /// Session/List 方法的响应。
    /// </summary>
    public class ListSessionsResponse
    {
        /// <summary>
        /// 会话列表。
        /// </summary>
        [JsonPropertyName("sessions")]
        public List<SessionInfo> Sessions { get; set; } = new List<SessionInfo>();

        /// <summary>
        /// 下一页游标（可选）。
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; }

        /// <summary>
        /// 创建新的 ListSessionsResponse 实例。
        /// </summary>
        public ListSessionsResponse()
        {
        }
    }

    /// <summary>
    /// 会话信息。
    /// </summary>
    public class SessionInfo
    {
        /// <summary>
        /// 会话 ID。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 工作目录。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        /// <summary>
        /// 会话标题（可选）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 最后更新时间（可选）。
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }

        /// <summary>
        /// 创建新的 SessionInfo 实例。
        /// </summary>
        public SessionInfo()
        {
        }
    }
}
