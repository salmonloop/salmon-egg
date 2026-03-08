using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Session/Set_Mode 方法的请求参数。
    /// 用于切换会话的工作模式。
    /// </summary>
    public class SessionSetModeParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 要切换到的目标模式 ID（必填）。
        /// </summary>
        [JsonPropertyName("modeId")]
        public string ModeId { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 SessionSetModeParams 实例。
        /// </summary>
        public SessionSetModeParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetModeParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="modeId">目标模式 ID</param>
        public SessionSetModeParams(string sessionId, string modeId)
        {
            SessionId = sessionId;
            ModeId = modeId;
        }
    }

    /// <summary>
    /// Session/Set_Mode 方法的响应。
    /// </summary>
    public class SessionSetModeResponse
    {
        /// <summary>
        /// 新的模式 ID。
        /// </summary>
        [JsonPropertyName("modeId")]
        public string ModeId { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 SessionSetModeResponse 实例。
        /// </summary>
        public SessionSetModeResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetModeResponse 实例。
        /// </summary>
        /// <param name="modeId">新的模式 ID</param>
        public SessionSetModeResponse(string modeId)
        {
            ModeId = modeId;
        }
    }

    /// <summary>
    /// Session/Cancel 方法的请求参数。
    /// 用于取消正在进行的会话。
    /// </summary>
    public class SessionCancelParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 取消原因（可选）。
        /// </summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>
        /// 创建新的 SessionCancelParams 实例。
        /// </summary>
        public SessionCancelParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionCancelParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="reason">取消原因</param>
        public SessionCancelParams(string sessionId, string? reason = null)
        {
            SessionId = sessionId;
            Reason = reason;
        }
    }

    /// <summary>
    /// Session/Cancel 方法的响应。
    /// </summary>
    public class SessionCancelResponse
    {
        /// <summary>
        /// 是否成功取消。
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>
        /// 可选的消息。
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>
        /// 创建新的 SessionCancelResponse 实例。
        /// </summary>
        public SessionCancelResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionCancelResponse 实例。
        /// </summary>
        /// <param name="success">是否成功</param>
        /// <param name="message">消息</param>
        public SessionCancelResponse(bool success, string? message = null)
        {
            Success = success;
            Message = message;
        }
    }

    /// <summary>
    /// Session/Load 方法的请求参数。
    /// 用于加载已存在的会话历史。
    /// </summary>
    public class SessionLoadParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话的工作目录（必填）。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        /// <summary>
        /// MCP 服务器配置列表（可选）。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, object>? McpServers { get; set; }

        /// <summary>
        /// 创建新的 SessionLoadParams 实例。
        /// </summary>
        public SessionLoadParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionLoadParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        public SessionLoadParams(string sessionId, string cwd, Dictionary<string, object>? mcpServers = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            McpServers = mcpServers;
        }
    }

    /// <summary>
    /// Session/Load 方法的响应。
    /// 当历史加载完成时返回 null 或空对象。
    /// </summary>
    public class SessionLoadResponse
    {
        /// <summary>
        /// 创建新的 SessionLoadResponse 实例。
        /// </summary>
        public SessionLoadResponse()
        {
        }

        /// <summary>
        /// 表示加载完成的静态实例。
        /// </summary>
        public static readonly SessionLoadResponse Completed = new SessionLoadResponse();
    }

    /// <summary>
    /// Session/Set_Config_Option 方法的请求参数。
    /// 用于设置会话的配置选项。
    /// </summary>
    public class SessionSetConfigOptionParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 配置选项 ID（必填）。
        /// </summary>
        [JsonPropertyName("configId")]
        public string ConfigId { get; set; } = string.Empty;

        /// <summary>
        /// 配置选项的值（必填）。
        /// </summary>
        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionParams 实例。
        /// </summary>
        public SessionSetConfigOptionParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="configId">配置选项 ID</param>
        /// <param name="value">配置选项的值</param>
        public SessionSetConfigOptionParams(string sessionId, string configId, JsonElement value)
        {
            SessionId = sessionId;
            ConfigId = configId;
            Value = value;
        }
    }

    /// <summary>
    /// Session/Set_Config_Option 方法的响应。
    /// </summary>
    public class SessionSetConfigOptionResponse
    {
        /// <summary>
        /// 更新后的配置选项列表。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public Dictionary<string, object>? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionResponse 实例。
        /// </summary>
        public SessionSetConfigOptionResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionResponse 实例。
        /// </summary>
        /// <param name="configOptions">配置选项列表</param>
        public SessionSetConfigOptionResponse(Dictionary<string, object>? configOptions = null)
        {
            ConfigOptions = configOptions;
        }
    }
}
