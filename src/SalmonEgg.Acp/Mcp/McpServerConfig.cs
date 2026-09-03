using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Mcp
{
    /// <summary>
    /// MCP server configuration.
    /// Supports configuration for multiple transport types (stdio, http, sse).
    /// </summary>
    [JsonConverter(typeof(McpServerJsonConverter))]
    public abstract record McpServer : AcpProtocolObject
    {
        /// <summary>
        /// The display name of the server.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

    }

    public enum McpServerTransport
    {
        Stdio,
        Http,
        Sse,
        Custom
    }

    /// <summary>
    /// Configuration for a stdio MCP server.
    /// Communicates with the server over standard input/output.
    /// </summary>
    public sealed record StdioMcpServer : McpServer
    {
        /// <summary>
        /// The command that launches the server executable.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; init; } = string.Empty;

        /// <summary>
        /// The command-line argument list.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; init; }

        /// <summary>
        /// The environment variable configuration.
        /// </summary>
        [JsonPropertyName("env")]
        public List<McpEnvVariable>? Env { get; init; }

        /// <summary>
        /// Creates a new StdioMcpServer instance.
        /// </summary>
        public StdioMcpServer()
        {
        }

        /// <summary>
        /// Creates a new StdioMcpServer instance.
        /// </summary>
        /// <param name="name">The server name</param>
        /// <param name="command">The command</param>
        /// <param name="args">The argument list</param>
        /// <param name="env">The environment variables</param>
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
    /// Configuration for an HTTP MCP server.
    /// Communicates with the server over HTTP requests.
    /// </summary>
    public sealed record HttpMcpServer : McpServer
    {
        /// <summary>
        /// The URL of the server.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        /// <summary>
        /// The HTTP header configuration.
        /// </summary>
        [JsonPropertyName("headers")]
        public List<McpHttpHeader>? Headers { get; init; }

        /// <summary>
        /// Creates a new HttpMcpServer instance.
        /// </summary>
        public HttpMcpServer()
        {
        }

        /// <summary>
        /// Creates a new HttpMcpServer instance.
        /// </summary>
        /// <param name="name">The server name</param>
        /// <param name="url">The URL</param>
        /// <param name="headers">The HTTP headers</param>
        public HttpMcpServer(string name, string url, List<McpHttpHeader>? headers = null)
        {
            Name = name;
            Url = url;
            Headers = headers;
        }
    }

    /// <summary>
    /// Configuration for an SSE (Server-Sent Events) MCP server.
    /// Communicates with the server over an SSE stream.
    /// </summary>
    public sealed record SseMcpServer : McpServer
    {
        /// <summary>
        /// The URL of the SSE endpoint.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        /// <summary>
        /// The HTTP header configuration.
        /// </summary>
        [JsonPropertyName("headers")]
        public List<McpHttpHeader>? Headers { get; init; }

        /// <summary>
        /// Creates a new SseMcpServer instance.
        /// </summary>
        public SseMcpServer()
        {
        }

        /// <summary>
        /// Creates a new SseMcpServer instance.
        /// </summary>
        /// <param name="name">The server name</param>
        /// <param name="url">The URL</param>
        /// <param name="headers">The HTTP headers</param>
        public SseMcpServer(string name, string url, List<McpHttpHeader>? headers = null)
        {
            Name = name;
            Url = url;
            Headers = headers;
        }
    }

    /// <summary>
    /// Configuration for a custom / future MCP transport.
    /// Carries the forward-compatible passthrough for the V2 schema "other" branch (a <c>type</c> value other than
    /// stdio/http/sse): the spec requires a receiver to "preserve the raw payload" for a transport it does not
    /// recognize, leaving it to the Agent rather than the client to accept or reject it.
    /// </summary>
    public sealed record CustomMcpServer : McpServer
    {
        /// <summary>
        /// The raw <c>type</c> transport value (such as a <c>_custom</c> extension or a future ACP variant value).
        /// </summary>
        [JsonPropertyName("type")]
        public string Transport { get; init; } = string.Empty;

        /// <summary>
        /// The raw server object payload, preserved verbatim for passthrough.
        /// Read and written manually by <see cref="McpServerJsonConverter"/>, bypassing default serialization.
        /// </summary>
        public JsonElement RawPayload { get; init; }

        /// <summary>
        /// Creates a new CustomMcpServer instance.
        /// </summary>
        public CustomMcpServer()
        {
        }

        /// <summary>
        /// Creates a new CustomMcpServer instance.
        /// </summary>
        /// <param name="name">The server name</param>
        /// <param name="transport">The raw transport value</param>
        /// <param name="rawPayload">The raw server object payload</param>
        public CustomMcpServer(string name, string transport, JsonElement rawPayload)
        {
            Name = name;
            Transport = transport;
            RawPayload = rawPayload;
        }
    }

    /// <summary>
    /// MCP environment variable configuration.
    /// </summary>
    public sealed record McpEnvVariable : AcpProtocolObject
    {
        /// <summary>
        /// The environment variable name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The environment variable value.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;

        /// <summary>
        /// Creates a new McpEnvVariable instance.
        /// </summary>
        public McpEnvVariable()
        {
        }

        /// <summary>
        /// Creates a new McpEnvVariable instance.
        /// </summary>
        /// <param name="name">The variable name</param>
        /// <param name="value">The variable value</param>
        public McpEnvVariable(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// MCP HTTP header configuration.
    /// </summary>
    public sealed record McpHttpHeader : AcpProtocolObject
    {
        /// <summary>
        /// The header name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The header value.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;

        /// <summary>
        /// Creates a new McpHttpHeader instance.
        /// </summary>
        public McpHttpHeader()
        {
        }

        /// <summary>
        /// Creates a new McpHttpHeader instance.
        /// </summary>
        /// <param name="name">The header name</param>
        /// <param name="value">The header value</param>
        public McpHttpHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Deep-copy helpers for MCP server wire DTOs.
    /// Host code uses these when snapshotting catalog/runtime server lists; JSON conversion stays internal.
    /// </summary>
    public static class McpServerSnapshots
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

    internal sealed class McpServerJsonConverter : JsonConverter<McpServer>
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
                McpServerTransport.Custom => ReadOther(root),
                _ => ReadStdio(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, McpServer value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case StdioMcpServer stdio:
                    WriteStdio(writer, stdio, options);
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
                // V2 schema "other" branch: any type value other than stdio/http/sse (including `_` extensions and
                // future ACP variants) must preserve the raw payload for forward passthrough, leaving it to the Agent
                // rather than the client to tighten. Read is purely tolerant and does not branch on version.
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
                // headers is optional in the schema (V2 requires only [name,url], and Rust annotates it with
                // skip_serializing_if=Vec::is_empty): its absence no longer throws and reads as an empty list.
                // The type contract is not relaxed, though - if it is provided but is not an array, or an entry is
                // missing name|value, it still throws.
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
                // headers behaves as for http: optional in the schema, absence reads as an empty list instead of
                // throwing; the type contract is not relaxed.
                Headers = ReadOptionalNameValueArray<McpHttpHeader>(
                    root,
                    "headers",
                    (name, value, meta) => new McpHttpHeader(name, value) { Meta = meta }),
                Meta = AcpMetaJson.Read(root)
            };
        }

        /// <summary>
        /// Reads the V2 schema "other" branch: a transport other than stdio/http/sse.
        /// Per the spec's "preserve the raw payload" rule the entire server object is kept verbatim, leaving it to the
        /// Agent rather than the client to accept or reject it; the client layer tightens no field.
        /// </summary>
        private static CustomMcpServer ReadOther(JsonElement root)
        {
            // type is guaranteed to be present and a string (ResolveTransport is the precondition for reaching this
            // branch), but the value is still read defensively, falling back to an empty string instead of throwing,
            // which upholds the pure passthrough semantics.
            var transport = root.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;

            // name is the required field common to every transport and is read so upper layers can display it;
            // all remaining unknown fields are kept in RawPayload for passthrough and are not interpreted.
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
            // V2 relaxes args to optional: return null when it is absent (faithfully expressing "not provided", as
            // distinct from "an explicit empty array").
            if (!root.TryGetProperty(propertyName, out var values))
            {
                return null;
            }

            // The type contract is not relaxed, though: once provided, a non-array or a non-string entry is still
            // treated as a protocol violation and throws - no over-tolerance in the other direction (args is not
            // annotated with x-deserialize-default-on-error).
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

        private static void WriteStdio(Utf8JsonWriter writer, StdioMcpServer stdio, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            // The V2 schema discriminates stdio/http/sse via the `type` field; V1 stdio has no type field and is
            // identified implicitly by its absence. Write type only when the negotiated version is V2, so a V1 Agent
            // is never sent a field it does not recognize.
            if (AcpWireFormat.NegotiatedVersion(options) >= AcpProtocolVersion.V2)
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
            // Forward-compatible passthrough: write back the raw payload preserved at read time verbatim, without
            // reordering fields or dropping unknown properties, leaving it to the Agent rather than the client to
            // accept or reject the transport. RawPayload is the single authoritative source of truth for a Custom
            // transport (including its _meta), so custom.Meta is not written on top of it here, which avoids a second
            // state owner.
            // Uses WriteRawValue(GetRawText()) rather than WriteTo: consistent with AcpProtocolObject, this avoids
            // WriteTo re-encoding escape sequences and numeric token shapes, achieving byte-level fidelity.
            // If RawPayload is empty (for example when hand-constructed), fall back to a minimal write of the known
            // fields, still carrying the original type value.
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

    }
}
