using System;
using System.Collections;
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

        /// <summary>
        /// ACP 保留的扩展元数据。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }
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
        /// ACP 保留的扩展元数据。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }

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
        /// ACP 保留的扩展元数据。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }

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
                default:
                    throw new ArgumentException("Unsupported MCP server type.", nameof(server));
            }
        }

        public static Dictionary<string, object?>? CloneMeta(Dictionary<string, object?>? meta)
        {
            if (meta == null)
            {
                return null;
            }

            var result = new Dictionary<string, object?>(meta.Comparer);
            foreach (var item in meta)
            {
                result[item.Key] = CloneMetaValue(item.Value);
            }

            return result;
        }

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
                    WriteMeta(writer, http.Meta);
                    writer.WriteEndObject();
                    break;
                case SseMcpServer sse:
                    writer.WriteStartObject();
                    writer.WriteString("type", "sse");
                    writer.WriteString("name", sse.Name);
                    writer.WriteString("url", sse.Url);
                    WriteHeaders(writer, sse.Headers);
                    WriteMeta(writer, sse.Meta);
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
                Name = ReadRequiredString(root, "name"),
                Command = ReadRequiredString(root, "command"),
                Args = ReadRequiredStringArray(root, "args"),
                Env = ReadRequiredNameValueArray<McpEnvVariable>(
                    root,
                    "env",
                    (name, value, meta) => new McpEnvVariable(name, value) { Meta = meta }),
                Meta = ReadOptionalMeta(root)
            };
        }

        private static HttpMcpServer ReadHttp(JsonElement root)
        {
            return new HttpMcpServer
            {
                Name = ReadRequiredString(root, "name"),
                Url = ReadRequiredString(root, "url"),
                Headers = ReadRequiredNameValueArray<McpHttpHeader>(
                    root,
                    "headers",
                    (name, value, meta) => new McpHttpHeader(name, value) { Meta = meta }),
                Meta = ReadOptionalMeta(root)
            };
        }

        private static SseMcpServer ReadSse(JsonElement root)
        {
            return new SseMcpServer
            {
                Name = ReadRequiredString(root, "name"),
                Url = ReadRequiredString(root, "url"),
                Headers = ReadRequiredNameValueArray<McpHttpHeader>(
                    root,
                    "headers",
                    (name, value, meta) => new McpHttpHeader(name, value) { Meta = meta }),
                Meta = ReadOptionalMeta(root)
            };
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

        private static List<string> ReadRequiredStringArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var values))
            {
                throw new JsonException($"MCP server is missing required '{propertyName}'.");
            }

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
                    ReadOptionalMeta(value)));
            }

            return result;
        }

        private static Dictionary<string, object?>? ReadOptionalMeta(JsonElement root)
        {
            if (!root.TryGetProperty("_meta", out var metaElement)
                || metaElement.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (metaElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("MCP '_meta' must be an object or null.");
            }

            var meta = new Dictionary<string, object?>();
            foreach (var property in metaElement.EnumerateObject())
            {
                meta[property.Name] = property.Value.Clone();
            }

            return meta;
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
                    WriteMeta(writer, variable.Meta);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            WriteMeta(writer, stdio.Meta);
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
                    WriteMeta(writer, header.Meta);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteMeta(Utf8JsonWriter writer, Dictionary<string, object?>? meta)
        {
            if (meta == null)
            {
                return;
            }

            writer.WritePropertyName("_meta");
            WriteMetaObject(writer, meta);
        }

        private static void WriteMetaObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> meta)
        {
            writer.WriteStartObject();
            foreach (var item in meta)
            {
                writer.WritePropertyName(item.Key);
                WriteMetaValue(writer, item.Value);
            }

            writer.WriteEndObject();
        }

        private static void WriteDictionaryMetaObject(Utf8JsonWriter writer, IDictionary meta)
        {
            writer.WriteStartObject();
            foreach (DictionaryEntry item in meta)
            {
                if (item.Key is not string key)
                {
                    throw new JsonException("MCP '_meta' object keys must be strings.");
                }

                writer.WritePropertyName(key);
                WriteMetaValue(writer, item.Value);
            }

            writer.WriteEndObject();
        }

        private static void WriteMetaArray(Utf8JsonWriter writer, IEnumerable values)
        {
            writer.WriteStartArray();
            foreach (var item in values)
            {
                WriteMetaValue(writer, item);
            }

            writer.WriteEndArray();
        }

        private static void WriteMetaValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case JsonElement element:
                    element.WriteTo(writer);
                    break;
                case JsonDocument document:
                    document.RootElement.WriteTo(writer);
                    break;
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case byte number:
                    writer.WriteNumberValue(number);
                    break;
                case sbyte number:
                    writer.WriteNumberValue(number);
                    break;
                case short number:
                    writer.WriteNumberValue(number);
                    break;
                case ushort number:
                    writer.WriteNumberValue(number);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case uint number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case ulong number:
                    writer.WriteNumberValue(number);
                    break;
                case float number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                    WriteMetaObject(writer, readOnlyDictionary);
                    break;
                case IDictionary dictionary:
                    WriteDictionaryMetaObject(writer, dictionary);
                    break;
                case IEnumerable values:
                    WriteMetaArray(writer, values);
                    break;
                default:
                    throw new JsonException($"Unsupported MCP '_meta' value type: {value.GetType().FullName}");
            }
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

        private static object? CloneMetaValue(object? value)
        {
            switch (value)
            {
                case null:
                case string:
                case bool:
                case byte:
                case sbyte:
                case short:
                case ushort:
                case int:
                case uint:
                case long:
                case ulong:
                case float:
                case double:
                case decimal:
                    return value;
                case JsonElement element:
                    return element.Clone();
                case JsonDocument document:
                    return document.RootElement.Clone();
                case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                    return CloneReadOnlyMetaDictionary(readOnlyDictionary);
                case IDictionary dictionary:
                    return CloneMetaDictionary(dictionary);
                case IEnumerable values:
                    return CloneMetaArray(values);
                default:
                    throw new JsonException($"Unsupported MCP '_meta' value type: {value.GetType().FullName}");
            }
        }

        private static Dictionary<string, object?> CloneReadOnlyMetaDictionary(
            IReadOnlyDictionary<string, object?> dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (var item in dictionary)
            {
                result[item.Key] = CloneMetaValue(item.Value);
            }

            return result;
        }

        private static Dictionary<string, object?> CloneMetaDictionary(IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry item in dictionary)
            {
                if (item.Key is not string key)
                {
                    throw new JsonException("MCP '_meta' object keys must be strings.");
                }

                result[key] = CloneMetaValue(item.Value);
            }

            return result;
        }

        private static List<object?> CloneMetaArray(IEnumerable values)
        {
            var result = new List<object?>();
            foreach (var item in values)
            {
                result.Add(CloneMetaValue(item));
            }

            return result;
        }
    }
}
