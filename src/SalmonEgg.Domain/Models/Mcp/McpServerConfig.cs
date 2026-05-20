using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Mcp
{
    /// <summary>
    /// MCP 服务器配置类。
    /// 支持多种传输类型（stdio、http、sse）的配置。
    /// </summary>
    [JsonConverter(typeof(McpServerJsonConverter))]
    public abstract class McpServer
    {
        /// <summary>
        /// 服务器的显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public enum McpServerTransport
    {
        Stdio,
        Http,
        Sse
    }

    /// <summary>
    /// Stdio 类型的 MCP 服务器配置。
    /// 通过标准输入/输出与服务器通信。
    /// </summary>
    public class StdioMcpServer : McpServer
    {
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
        public List<McpEnvVariable>? Env { get; set; }

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
        public StdioMcpServer(
            string name,
            string command,
            List<string>? args = null,
            List<McpEnvVariable>? env = null)
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

    public sealed class McpServerJsonConverter : JsonConverter<McpServer>
    {
        public override McpServer? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var transport = ResolveTransport(root);

            return transport switch
            {
                McpServerTransport.Http => ReadHttp(root),
                McpServerTransport.Sse => ReadSse(root),
                _ => ReadStdio(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, McpServer value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case StdioMcpServer stdio:
                    WriteStdio(writer, stdio);
                    break;
                case HttpMcpServer http:
                    writer.WriteStartObject();
                    writer.WriteString("type", "http");
                    writer.WriteString("name", http.Name);
                    writer.WriteString("url", http.Url);
                    WriteHeaders(writer, http.Headers);
                    writer.WriteEndObject();
                    break;
                case SseMcpServer sse:
                    writer.WriteStartObject();
                    writer.WriteString("type", "sse");
                    writer.WriteString("name", sse.Name);
                    writer.WriteString("url", sse.Url);
                    WriteHeaders(writer, sse.Headers);
                    writer.WriteEndObject();
                    break;
                default:
                    throw new JsonException($"Unsupported MCP server type: {value.GetType().FullName}");
            }
        }

        private static McpServerTransport ResolveTransport(JsonElement root)
        {
            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return McpServerTransport.Stdio;
            }

            return typeElement.GetString() switch
            {
                "http" => McpServerTransport.Http,
                "sse" => McpServerTransport.Sse,
                _ => throw new JsonException("Unknown MCP server transport type.")
            };
        }

        private static StdioMcpServer ReadStdio(JsonElement root)
        {
            return new StdioMcpServer
            {
                Name = ReadString(root, "name"),
                Command = ReadString(root, "command"),
                Args = ReadStringArray(root, "args"),
                Env = ReadNameValueArray<McpEnvVariable>(root, "env", (name, value) => new McpEnvVariable(name, value))
            };
        }

        private static HttpMcpServer ReadHttp(JsonElement root)
        {
            return new HttpMcpServer
            {
                Name = ReadString(root, "name"),
                Url = ReadString(root, "url"),
                Headers = ReadNameValueArray<McpHttpHeader>(root, "headers", (name, value) => new McpHttpHeader(name, value))
            };
        }

        private static SseMcpServer ReadSse(JsonElement root)
        {
            return new SseMcpServer
            {
                Name = ReadString(root, "name"),
                Url = ReadString(root, "url"),
                Headers = ReadNameValueArray<McpHttpHeader>(root, "headers", (name, value) => new McpHttpHeader(name, value))
            };
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static List<string> ReadStringArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            var result = new List<string>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    result.Add(value.GetString() ?? string.Empty);
                }
            }

            return result;
        }

        private static List<TValue> ReadNameValueArray<TValue>(
            JsonElement root,
            string propertyName,
            Func<string, string, TValue> factory)
        {
            if (!root.TryGetProperty(propertyName, out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return new List<TValue>();
            }

            var result = new List<TValue>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(factory(ReadString(value, "name"), ReadString(value, "value")));
            }

            return result;
        }

        private static void WriteStdio(Utf8JsonWriter writer, StdioMcpServer stdio)
        {
            writer.WriteStartObject();
            writer.WriteString("name", stdio.Name);
            writer.WriteString("command", stdio.Command);
            writer.WritePropertyName("args");
            writer.WriteStartArray();
            if (stdio.Args != null)
            {
                foreach (var arg in stdio.Args)
                {
                    writer.WriteStringValue(arg);
                }
            }

            writer.WriteEndArray();
            writer.WritePropertyName("env");
            writer.WriteStartArray();
            if (stdio.Env != null)
            {
                foreach (var variable in stdio.Env)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", variable.Name);
                    writer.WriteString("value", variable.Value);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteHeaders(Utf8JsonWriter writer, List<McpHttpHeader>? headers)
        {
            writer.WritePropertyName("headers");
            writer.WriteStartArray();
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", header.Name);
                    writer.WriteString("value", header.Value);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }
    }
}
