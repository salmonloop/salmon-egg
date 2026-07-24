using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Mcp
{
    /// <summary>
    /// MCP 服务器配置类。
    /// 支持多种传输类型（stdio、http、sse）的配置。
    /// </summary>
    [JsonConverter(typeof(McpServerJsonConverter))]
    public abstract class McpServer : AcpProtocolObject
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
        Sse,
        Custom
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
    /// 自定义 / 未来的 MCP transport 配置。
    /// 承载 V2 schema "other" 分支（<c>type</c> 值非 stdio/http/sse）的前向兼容透传：
    /// spec 要求 receiver 对不认识的 transport「preserve the raw payload」，
    /// 由 Agent 而非 client 决定接受或拒绝。
    /// </summary>
    public class CustomMcpServer : McpServer
    {
        /// <summary>
        /// 原始 <c>type</c> transport 值（如 <c>_custom</c> 扩展或未来 ACP 变体值）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Transport { get; set; } = string.Empty;

        /// <summary>
        /// 原始 server object payload，原样保留以供透传。
        /// 由 <see cref="McpServerJsonConverter"/> 手动读写，不经默认序列化。
        /// </summary>
        public JsonElement RawPayload { get; set; }

        /// <summary>
        /// 创建新的 CustomMcpServer 实例。
        /// </summary>
        public CustomMcpServer()
        {
        }

        /// <summary>
        /// 创建新的 CustomMcpServer 实例。
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="transport">原始 transport 值</param>
        /// <param name="rawPayload">原始 server object payload</param>
        public CustomMcpServer(string name, string transport, JsonElement rawPayload)
        {
            Name = name;
            Transport = transport;
            RawPayload = rawPayload;
        }
    }

    /// <summary>
    /// MCP 环境变量配置类。
    /// </summary>
    public class McpEnvVariable : AcpProtocolObject
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
    public class McpHttpHeader : AcpProtocolObject
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
        public static List<McpServer> CloneServers(IEnumerable<McpServer>? servers)
        {
            if (servers == null)
            {
                return new List<McpServer>();
            }

            var result = new List<McpServer>();
            foreach (var server in servers)
            {
                result.Add(CloneServer(server));
            }

            return result;
        }

        public static McpServer CloneServer(McpServer server)
        {
            switch (server)
            {
                case StdioMcpServer stdio:
                    return new StdioMcpServer(
                        stdio.Name,
                        stdio.Command,
                        stdio.Args == null ? null : new List<string>(stdio.Args),
                        CloneEnv(stdio.Env))
                    {
                        Meta = CloneMeta(stdio.Meta)
                    };
                case HttpMcpServer http:
                    return new HttpMcpServer(
                        http.Name,
                        http.Url,
                        CloneHeaders(http.Headers))
                    {
                        Meta = CloneMeta(http.Meta)
                    };
                case SseMcpServer sse:
                    return new SseMcpServer(
                        sse.Name,
                        sse.Url,
                        CloneHeaders(sse.Headers))
                    {
                        Meta = CloneMeta(sse.Meta)
                    };
                case CustomMcpServer custom:
                    return new CustomMcpServer(
                        custom.Name,
                        custom.Transport,
                        custom.RawPayload.Clone())
                    {
                        Meta = CloneMeta(custom.Meta)
                    };
                default:
                    throw new ArgumentException("Unsupported MCP server type.", nameof(server));
            }
        }

        public static Dictionary<string, object?>? CloneMeta(Dictionary<string, object?>? meta)
            => AcpMetaJson.Clone(meta);

        public override McpServer? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var transport = ResolveTransport(root);

            return transport switch
            {
                McpServerTransport.Http => ReadHttp(root),
                McpServerTransport.Sse => ReadSse(root),
                McpServerTransport.Custom => ReadOther(root),
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
                    AcpMetaJson.Write(writer, http.Meta);
                    writer.WriteEndObject();
                    break;
                case SseMcpServer sse:
                    writer.WriteStartObject();
                    writer.WriteString("type", "sse");
                    writer.WriteString("name", sse.Name);
                    writer.WriteString("url", sse.Url);
                    WriteHeaders(writer, sse.Headers);
                    AcpMetaJson.Write(writer, sse.Meta);
                    writer.WriteEndObject();
                    break;
                case CustomMcpServer custom:
                    WriteCustom(writer, custom);
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
                "stdio" => McpServerTransport.Stdio,
                "http" => McpServerTransport.Http,
                "sse" => McpServerTransport.Sse,
                // V2 schema "other" 分支：任何非 stdio/http/sse 的 type 值（含 `_` 扩展与未来 ACP 变体）
                // 均须 preserve raw payload 前向透传，由 Agent 而非 client 收紧。Read 纯容错，不按版本区分。
                _ => McpServerTransport.Custom
            };
        }

        private static StdioMcpServer ReadStdio(JsonElement root)
        {
            return new StdioMcpServer
            {
                Name = ReadRequiredString(root, "name"),
                Command = ReadRequiredString(root, "command"),
                Args = ReadOptionalStringArray(root, "args"),
                Env = ReadOptionalNameValueArray<McpEnvVariable>(
                    root,
                    "env",
                    (name, value, meta) => new McpEnvVariable(name, value) { Meta = meta }),
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static HttpMcpServer ReadHttp(JsonElement root)
        {
            return new HttpMcpServer
            {
                Name = ReadRequiredString(root, "name"),
                Url = ReadRequiredString(root, "url"),
                // headers 在 schema 中为可选（V2 required 仅 [name,url]，Rust 标注
                // skip_serializing_if=Vec::is_empty）：缺省不再抛，读为空列表。
                // 但类型契约不放宽——一旦提供却非数组 / 条目缺 name|value 仍抛。
                Headers = ReadOptionalNameValueArray<McpHttpHeader>(
                    root,
                    "headers",
                    (name, value, meta) => new McpHttpHeader(name, value) { Meta = meta }),
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static SseMcpServer ReadSse(JsonElement root)
        {
            return new SseMcpServer
            {
                Name = ReadRequiredString(root, "name"),
                Url = ReadRequiredString(root, "url"),
                // headers 同 http：schema 可选，缺省读为空列表而非抛；类型契约不放宽。
                Headers = ReadOptionalNameValueArray<McpHttpHeader>(
                    root,
                    "headers",
                    (name, value, meta) => new McpHttpHeader(name, value) { Meta = meta }),
                Meta = AcpMetaJson.Read(root)
            };
        }

        /// <summary>
        /// 读取 V2 schema "other" 分支：非 stdio/http/sse 的 transport。
        /// 按 spec「preserve the raw payload」原样保留整个 server object，
        /// 由 Agent 而非 client 决定接受或拒绝，client 层不做任何字段收紧。
        /// </summary>
        private static CustomMcpServer ReadOther(JsonElement root)
        {
            // type 一定存在且为字符串（ResolveTransport 已保证进入本分支的前提），
            // 但仍防御性取值，缺省回退空串而非抛出，贯彻纯透传语义。
            var transport = root.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;

            // name 是所有 transport 共有的必需字段，读取以供上层展示；
            // 其余未知字段全部保存在 RawPayload 中透传，不做解释。
            var name = root.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

            return new CustomMcpServer(name, transport, root.Clone());
        }

        private static string ReadRequiredString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                throw new JsonException($"MCP server is missing required '{propertyName}'.");
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"MCP server '{propertyName}' must be a string.");
            }

            return value.GetString() ?? string.Empty;
        }

        private static List<string>? ReadOptionalStringArray(JsonElement root, string propertyName)
        {
            // V2 将 args 放宽为可选：缺省时返回 null（忠实表达「未提供」，区别于「显式空数组」）。
            if (!root.TryGetProperty(propertyName, out var values))
            {
                return null;
            }

            // 但类型契约不放宽：一旦提供，非数组 / 非字符串条目仍视为协议违规而抛出，
            // 不做反向的过度容忍（args 未标注 x-deserialize-default-on-error）。
            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"MCP server '{propertyName}' must be an array.");
            }

            var result = new List<string>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException($"MCP server '{propertyName}' entries must be strings.");
                }

                result.Add(value.GetString() ?? string.Empty);
            }

            return result;
        }

        private static List<TValue> ReadOptionalNameValueArray<TValue>(
            JsonElement root,
            string propertyName,
            Func<string, string, Dictionary<string, object?>?, TValue> factory)
        {
            if (!root.TryGetProperty(propertyName, out var values))
            {
                return new List<TValue>();
            }

            return ReadNameValueArray(values, propertyName, factory);
        }

        private static List<TValue> ReadRequiredNameValueArray<TValue>(
            JsonElement root,
            string propertyName,
            Func<string, string, Dictionary<string, object?>?, TValue> factory)
        {
            if (!root.TryGetProperty(propertyName, out var values))
            {
                throw new JsonException($"MCP server is missing required '{propertyName}'.");
            }

            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"MCP server '{propertyName}' must be an array.");
            }

            return ReadNameValueArray(values, propertyName, factory);
        }

        private static List<TValue> ReadNameValueArray<TValue>(
            JsonElement values,
            string propertyName,
            Func<string, string, Dictionary<string, object?>?, TValue> factory)
        {
            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"MCP server '{propertyName}' must be an array.");
            }

            var result = new List<TValue>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException($"MCP server '{propertyName}' entries must be objects.");
                }

                result.Add(factory(
                    ReadRequiredString(value, "name"),
                    ReadRequiredString(value, "value"),
                    AcpMetaJson.Read(value)));
            }

            return result;
        }

        private static void WriteStdio(Utf8JsonWriter writer, StdioMcpServer stdio)
        {
            writer.WriteStartObject();
            // V2 schema 以 `type` 判别式区分 stdio/http/sse；V1 stdio 无 type 字段，
            // 靠缺省 type 隐式判定。仅在协商为 V2 时写出 type，避免向 V1 Agent 发送其不认识的字段。
            if (AcpProtocolWriteContext.Current >= AcpProtocolVersion.V2)
            {
                writer.WriteString("type", "stdio");
            }

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
                    AcpMetaJson.Write(writer, variable.Meta);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            AcpMetaJson.Write(writer, stdio.Meta);
            writer.WriteEndObject();
        }

        private static void WriteCustom(Utf8JsonWriter writer, CustomMcpServer custom)
        {
            // 前向兼容透传：原样写回读入时保留的 raw payload，不重排字段、不丢弃未知属性，
            // 由 Agent 而非 client 决定接受或拒绝该 transport。RawPayload 是 Custom transport 的
            // 唯一权威事实源（含其 _meta），故此处不叠加写出 custom.Meta，避免第二套状态 owner。
            // 用 WriteRawValue(GetRawText()) 而非 WriteTo：与 AcpProtocolObject 一致，
            // 避免 WriteTo 对转义/数字 token 形态的 re-encode，实现字节级保真。
            // 若 RawPayload 为空（如手工构造），退化为按已知字段最小写出，仍携带原始 type 值。
            if (custom.RawPayload.ValueKind == JsonValueKind.Object)
            {
                writer.WriteRawValue(custom.RawPayload.GetRawText());
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", custom.Transport);
            writer.WriteString("name", custom.Name);
            AcpMetaJson.Write(writer, custom.Meta);
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
                    AcpMetaJson.Write(writer, header.Meta);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        private static List<McpEnvVariable>? CloneEnv(List<McpEnvVariable>? env)
        {
            if (env == null)
            {
                return null;
            }

            var result = new List<McpEnvVariable>();
            foreach (var variable in env)
            {
                result.Add(new McpEnvVariable(variable.Name, variable.Value)
                {
                    Meta = CloneMeta(variable.Meta)
                });
            }

            return result;
        }

        private static List<McpHttpHeader>? CloneHeaders(List<McpHttpHeader>? headers)
        {
            if (headers == null)
            {
                return null;
            }

            var result = new List<McpHttpHeader>();
            foreach (var header in headers)
            {
                result.Add(new McpHttpHeader(header.Name, header.Value)
                {
                    Meta = CloneMeta(header.Meta)
                });
            }

            return result;
        }

    }
}
