using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Mcp;
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
        /// MCP 服务器配置列表。
        /// ACP session/load 要求该字段始终为数组，即使当前没有任何 MCP server 也必须发送 []。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

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
        public SessionLoadParams(string sessionId, string cwd, List<McpServer>? mcpServers = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
        }
    }

    /// <summary>
    /// Session/Load 方法的响应。
    /// 可能返回 null / 空对象，或返回模式与配置选项快照。
    /// </summary>
    public class SessionLoadResponse
    {
        /// <summary>
        /// 会话模式状态（可选，兼容旧 Agent 可能直接返回数组）。
        /// </summary>
        [JsonPropertyName("modes")]
        [JsonConverter(typeof(SessionModesStateJsonConverter))]
        public SessionModesState? Modes { get; set; }

        /// <summary>
        /// 可用的配置选项列表（可选）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的 SessionLoadResponse 实例。
        /// </summary>
        public SessionLoadResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionLoadResponse 实例。
        /// </summary>
        /// <param name="modes">模式状态</param>
        /// <param name="configOptions">配置选项列表</param>
        public SessionLoadResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// 表示加载完成的静态实例。
        /// </summary>
        public static readonly SessionLoadResponse Completed = new SessionLoadResponse();
    }

    /// <summary>
    /// Session/Resume 方法的请求参数。
    /// 用于恢复已存在的会话上下文，但不要求 Agent 重放历史消息。
    /// </summary>
    public class SessionResumeParams
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
        /// MCP 服务器配置列表。
        /// ACP session/resume 要求该字段始终为数组，即使当前没有任何 MCP server 也必须发送 []。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

        /// <summary>
        /// 创建新的 SessionResumeParams 实例。
        /// </summary>
        public SessionResumeParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionResumeParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        public SessionResumeParams(string sessionId, string cwd, List<McpServer>? mcpServers = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
        }
    }

    /// <summary>
    /// Session/Resume 方法的响应。
    /// 可能返回 null / 空对象，或返回模式与配置选项快照。
    /// </summary>
    public class SessionResumeResponse
    {
        /// <summary>
        /// 会话模式状态（可选，兼容旧 Agent 可能直接返回数组）。
        /// </summary>
        [JsonPropertyName("modes")]
        [JsonConverter(typeof(SessionModesStateJsonConverter))]
        public SessionModesState? Modes { get; set; }

        /// <summary>
        /// 可用的配置选项列表（可选）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的 SessionResumeResponse 实例。
        /// </summary>
        public SessionResumeResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionResumeResponse 实例。
        /// </summary>
        /// <param name="modes">模式状态</param>
        /// <param name="configOptions">配置选项列表</param>
        public SessionResumeResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// 表示恢复完成的静态实例。
        /// </summary>
        public static readonly SessionResumeResponse Completed = new SessionResumeResponse();
    }

    /// <summary>
    /// Session/Close 方法的请求参数。
    /// 用于关闭已存在的会话并释放 Agent 侧资源。
    /// </summary>
    public class SessionCloseParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 SessionCloseParams 实例。
        /// </summary>
        public SessionCloseParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionCloseParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        public SessionCloseParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Session/Close 方法的响应。
    /// </summary>
    public class SessionCloseResponse
    {
        /// <summary>
        /// 创建新的 SessionCloseResponse 实例。
        /// </summary>
        public SessionCloseResponse()
        {
        }

        /// <summary>
        /// 表示关闭完成的静态实例。
        /// </summary>
        public static readonly SessionCloseResponse Completed = new SessionCloseResponse();
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
        public string Value { get; set; } = string.Empty;

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
        public SessionSetConfigOptionParams(string sessionId, string configId, string value)
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
        /// 更新后的配置选项列表（完整状态）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }

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
        public SessionSetConfigOptionResponse(List<ConfigOption>? configOptions = null)
        {
            ConfigOptions = configOptions;
        }
    }
}
