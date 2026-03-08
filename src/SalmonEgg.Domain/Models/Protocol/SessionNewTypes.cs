using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Session/New 方法的请求参数。
    /// 用于创建新的会话。
    /// </summary>
    public class SessionNewParams
    {
        /// <summary>
        /// 会话的工作目录（必填）。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        
        
        /// <summary>
        /// MCP 服务器配置列表（根据对端Agent要求，似乎是必填的数组）。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public object McpServers { get; set; } = new object[0];



        /// <summary>
        /// 创建新的 SessionNewParams 实例。
        /// </summary>
        public SessionNewParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionNewParams 实例。
        /// </summary>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        public SessionNewParams(string cwd, object? mcpServers = null)
        {
            Cwd = cwd;
            McpServers = mcpServers ?? new object[0];
        }
    }

    /// <summary>
    /// Session/New 方法的响应。
    /// Agent 对创建会话请求的响应。
    /// </summary>
    public class SessionNewResponse
    {
        /// <summary>
        /// 新创建的会话 ID。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 可用的会话模式列表（可选）。
        /// </summary>
        [JsonPropertyName("modes")]
        public List<SessionMode>? Modes { get; set; }

        
        /// <summary>
        /// 可用的配置选项列表（可选）。
        /// 根据对端Agent返回的实际数据格式，这里是一个数组。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public object? ConfigOptions { get; set; }


        /// <summary>
        /// 创建新的 SessionNewResponse 实例。
        /// </summary>
        public SessionNewResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionNewResponse 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="modes">可用模式列表</param>
        /// <param name="configOptions">配置选项</param>
        public SessionNewResponse(string sessionId, List<SessionMode>? modes = null, object? configOptions = null)
        {
            SessionId = sessionId;
            Modes = modes;
            ConfigOptions = configOptions;
        }
    }

    /// <summary>
    /// 会话模式类（用于 Session/New 响应）。
    /// </summary>
    public class SessionMode
    {
        /// <summary>
        /// 模式的唯一标识符。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 模式的显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模式的描述信息。
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 创建新的会话模式实例。
        /// </summary>
        public SessionMode()
        {
        }

        /// <summary>
        /// 创建新的会话模式实例。
        /// </summary>
        /// <param name="id">模式 ID</param>
        /// <param name="name">模式名称</param>
        /// <param name="description">模式描述</param>
        public SessionMode(string id, string name, string? description = null)
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }
}
