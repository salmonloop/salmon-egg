using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Mcp
{
    /// <summary>
    /// MCP 服务器配置类。
    /// 支持多种传输类型（stdio、http、sse）的配置。
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(StdioMcpServer), "stdio")]
    [JsonDerivedType(typeof(HttpMcpServer), "http")]
    [JsonDerivedType(typeof(SseMcpServer), "sse")]
    public abstract class McpServer
    {
        /// <summary>
        /// 服务器类型标识符。
        /// </summary>
        [JsonPropertyName("type")]
        public abstract string Type { get; }

        /// <summary>
        /// 服务器的显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Stdio 类型的 MCP 服务器配置。
    /// 通过标准输入/输出与服务器通信。
    /// </summary>
    public class StdioMcpServer : McpServer
    {
        /// <summary>
        /// 服务器类型标识符，固定为 "stdio"。
        /// </summary>
        [JsonPropertyName("type")]
        public override string Type => "stdio";

        /// <summary>
        /// 服务器可执行文件的命令。
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// 命令行参数列表。
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        /// <summary>
        /// 环境变量配置。
        /// </summary>
        [JsonPropertyName("env")]
        public Dictionary<string, string>? Env { get; set; }

        /// <summary>
        /// 创建新的 StdioMcpServer 实例。
        /// </summary>
        public StdioMcpServer()
        {
        }

        /// <summary>
        /// 创建新的 StdioMcpServer 实例。
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="command">命令</param>
        /// <param name="args">参数列表</param>
        /// <param name="env">环境变量</param>
        public StdioMcpServer(string name, string command, List<string>? args = null, Dictionary<string, string>? env = null)
        {
            Name = name;
            Command = command;
            Args = args;
            Env = env;
        }
    }

    /// <summary>
    /// HTTP 类型的 MCP 服务器配置。
    /// 通过 HTTP 请求与服务器通信。
    /// </summary>
    public class HttpMcpServer : McpServer
    {
        /// <summary>
        /// 服务器类型标识符，固定为 "http"。
        /// </summary>
        [JsonPropertyName("type")]
        public override string Type => "http";

        /// <summary>
        /// 服务器的 URL 地址。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// HTTP 请求头配置。
        /// </summary>
        [JsonPropertyName("headers")]
        public List<McpHttpHeader>? Headers { get; set; }

        /// <summary>
        /// 创建新的 HttpMcpServer 实例。
        /// </summary>
        public HttpMcpServer()
        {
        }

        /// <summary>
        /// 创建新的 HttpMcpServer 实例。
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="url">URL 地址</param>
        /// <param name="headers">HTTP 请求头</param>
        public HttpMcpServer(string name, string url, List<McpHttpHeader>? headers = null)
        {
            Name = name;
            Url = url;
            Headers = headers;
        }
    }

    /// <summary>
    /// SSE (Server-Sent Events) 类型的 MCP 服务器配置。
    /// 通过 SSE 流与服务器通信。
        /// </summary>
    public class SseMcpServer : McpServer
    {
        /// <summary>
        /// 服务器类型标识符，固定为 "sse"。
        /// </summary>
        [JsonPropertyName("type")]
        public override string Type => "sse";

        /// <summary>
        /// SSE 端点的 URL 地址。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// HTTP 请求头配置。
        /// </summary>
        [JsonPropertyName("headers")]
        public List<McpHttpHeader>? Headers { get; set; }

        /// <summary>
        /// 创建新的 SseMcpServer 实例。
        /// </summary>
        public SseMcpServer()
        {
        }

        /// <summary>
        /// 创建新的 SseMcpServer 实例。
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="url">URL 地址</param>
        /// <param name="headers">HTTP 请求头</param>
        public SseMcpServer(string name, string url, List<McpHttpHeader>? headers = null)
        {
            Name = name;
            Url = url;
            Headers = headers;
        }
    }

    /// <summary>
    /// MCP 环境变量配置类。
    /// </summary>
    public class McpEnvVariable
    {
        /// <summary>
        /// 环境变量名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 环境变量值。
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 McpEnvVariable 实例。
        /// </summary>
        public McpEnvVariable()
        {
        }

        /// <summary>
        /// 创建新的 McpEnvVariable 实例。
        /// </summary>
        /// <param name="name">变量名</param>
        /// <param name="value">变量值</param>
        public McpEnvVariable(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// MCP HTTP 请求头配置类。
    /// </summary>
    public class McpHttpHeader
    {
        /// <summary>
        /// 请求头名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 请求头值。
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 McpHttpHeader 实例。
        /// </summary>
        public McpHttpHeader()
        {
        }

        /// <summary>
        /// 创建新的 McpHttpHeader 实例。
        /// </summary>
        /// <param name="name">请求头名称</param>
        /// <param name="value">请求头值</param>
        public McpHttpHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
}
