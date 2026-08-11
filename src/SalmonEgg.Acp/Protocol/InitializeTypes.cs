using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Initialize 方法的请求参数。
    /// 用于客户端向 Agent 发起初始化请求。
    /// </summary>
    [JsonConverter(typeof(InitializeParamsJsonConverter))]
    public sealed record InitializeParams : AcpProtocolObject
    {
        /// <summary>
        /// 协议版本号。必须是整数。
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; } = AcpProtocolVersion.Default;

        /// <summary>
        /// 客户端信息。
        /// </summary>
        [JsonPropertyName("clientInfo")]
        public ClientInfo ClientInfo { get; init; } = new ClientInfo();

        /// <summary>
        /// 客户端能力声明。
        /// </summary>
        [JsonPropertyName("clientCapabilities")]
        public ClientCapabilities ClientCapabilities { get; init; } = new ClientCapabilities();

        /// <summary>
        /// 扩展字段（_meta），用于协议可扩展性。
        /// </summary>
        /// <summary>
        /// 创建新的 InitializeParams 实例。
        /// </summary>
        public InitializeParams()
        {
        }

        /// <summary>
        /// 创建新的 InitializeParams 实例。
        /// </summary>
        /// <param name="clientInfo">客户端信息</param>
        /// <param name="clientCapabilities">客户端能力</param>
        public InitializeParams(ClientInfo clientInfo, ClientCapabilities clientCapabilities)
        {
            ClientInfo = clientInfo;
            ClientCapabilities = clientCapabilities;
        }
    }

    /// <summary>
    /// 客户端信息类。
    /// 包含客户端的名称、标题和版本信息。
    /// </summary>
    public sealed record ClientInfo : AcpProtocolObject
    {
        /// <summary>
        /// 客户端的名称（标识符）。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// 客户端的显示标题。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// 客户端的版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = "1.0.0";

        /// <summary>
        /// 创建新的 ClientInfo 实例。
        /// </summary>
        public ClientInfo()
        {
        }

        /// <summary>
        /// 创建新的 ClientInfo 实例。
        /// </summary>
        /// <param name="name">客户端名称</param>
        /// <param name="version">版本号</param>
        /// <param name="title">显示标题</param>
        [SetsRequiredMembers]
        public ClientInfo(string name, string version, string? title = null)
        {
            Name = name;
            Version = version;
            Title = title;
        }
    }

    /// <summary>
    /// 客户端能力声明类。
    /// 声明客户端支持的功能。
    /// </summary>
    public sealed record ClientCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 文件系统能力。
        /// </summary>
        [JsonPropertyName("fs")]
        public FsCapability? Fs { get; init; }

        /// <summary>
        /// 终端能力。
        /// </summary>
        [JsonPropertyName("terminal")]
        public bool? Terminal { get; init; }

        /// <summary>
        /// 会话相关客户端能力。
        /// </summary>
        [JsonPropertyName("session")]
        public ClientSessionCapabilities? Session { get; init; }

        /// <summary>
        /// 扩展字段（_meta），用于声明自定义客户端能力。
        /// </summary>
        /// <summary>
        /// 创建新的 ClientCapabilities 实例。
        /// </summary>
        public ClientCapabilities()
        {
        }

        /// <summary>
        /// 创建新的 ClientCapabilities 实例。
        /// </summary>
        /// <param name="fs">文件系统能力</param>
        /// <param name="terminal">终端能力</param>
        /// <param name="session">会话能力</param>
        /// <param name="meta">扩展能力元数据</param>
        public ClientCapabilities(
            FsCapability? fs = null,
            bool? terminal = null,
            ClientSessionCapabilities? session = null,
            Dictionary<string, object?>? meta = null)
        {
            Fs = fs;
            Terminal = terminal;
            Session = session;
            Meta = meta;
        }

        /// <summary>
        /// 判断是否声明支持指定的扩展能力。
        /// </summary>
        /// <param name="extensionName">扩展能力名称</param>
        /// <returns>如果声明支持返回 true，否则返回 false</returns>
        public bool SupportsExtension(string extensionName)
        {
            if (string.IsNullOrWhiteSpace(extensionName)
                || Meta == null
                || !Meta.TryGetValue(ClientCapabilityMetadata.ExtensionsMetaKey, out var extensions))
            {
                return false;
            }

            return TryReadDeclaredExtensionSupport(extensions, extensionName);
        }

        private static bool TryReadDeclaredExtensionSupport(object? extensions, string extensionName)
        {
            if (extensions is Dictionary<string, object?> extensionMap
                && extensionMap.TryGetValue(extensionName, out var declaredSupport))
            {
                return TryReadBoolean(declaredSupport);
            }

            if (extensions is JsonElement element
                && element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(extensionName, out var declaredElement))
            {
                return TryReadBoolean(declaredElement);
            }

            return false;
        }

        private static bool TryReadBoolean(object? rawValue)
            => rawValue switch
            {
                bool value => value,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => false
            };
    }

    /// <summary>
    /// 客户端会话能力。
    /// </summary>
    public sealed record ClientSessionCapabilities : AcpProtocolObject
    {
        [JsonPropertyName("configOptions")]
        public SessionConfigOptionsCapabilities? ConfigOptions { get; init; }

    }

    /// <summary>
    /// 客户端会话配置选项能力。
    /// </summary>
    public sealed record SessionConfigOptionsCapabilities : AcpProtocolObject
    {
        [JsonPropertyName("boolean")]
        public BooleanConfigOptionCapabilities? Boolean { get; init; }
    }

    public sealed record BooleanConfigOptionCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// 文件系统能力类。
    /// </summary>
    public sealed record FsCapability : AcpProtocolObject
    {
        /// <summary>
        /// 是否支持读取文本文件。
        /// </summary>
        [JsonPropertyName("readTextFile")]
        public bool ReadTextFile { get; init; } = true;

        /// <summary>
        /// 是否支持写入文本文件。
        /// </summary>
        [JsonPropertyName("writeTextFile")]
        public bool WriteTextFile { get; init; } = true;

        /// <summary>
        /// 创建新的 FsCapability 实例。
        /// </summary>
        public FsCapability()
        {
        }

        /// <summary>
        /// 创建新的 FsCapability 实例。
        /// </summary>
        /// <param name="readTextFile">是否支持读取</param>
        /// <param name="writeTextFile">是否支持写入</param>
        public FsCapability(bool readTextFile = true, bool writeTextFile = true)
        {
            ReadTextFile = readTextFile;
            WriteTextFile = writeTextFile;
        }
    }

    internal static class InitializeClientProtocolPolicy
    {
        internal const string UnsupportedProtocolVersionMessage =
            "ACP initialize only supports client protocolVersion 1 or 2.";

        internal const string V2LegacyClientCapabilitiesMessage =
            "ACP v2 initialize cannot use the v1 client capability fields 'fs', 'terminal', or 'session'. Move experimental declarations to _meta or use protocolVersion 1.";

        internal static void Validate(int protocolVersion, ClientCapabilities capabilities)
        {
            ArgumentNullException.ThrowIfNull(capabilities);

            if (!AcpProtocolVersion.IsSupported(protocolVersion))
            {
                throw new JsonException(UnsupportedProtocolVersionMessage);
            }

            if (protocolVersion == AcpProtocolVersion.V2
                && (capabilities.Fs is not null
                    || capabilities.Terminal is not null
                    || capabilities.Session is not null))
            {
                throw new JsonException(V2LegacyClientCapabilitiesMessage);
            }
        }
    }

    /// <summary>
    /// Initialize 方法的响应。
    /// Agent 对初始化请求的响应。
    /// </summary>
    [JsonConverter(typeof(InitializeResponseJsonConverter))]
    public sealed record InitializeResponse : AcpProtocolObject
    {
        /// <summary>
        /// 协议版本号。必须是整数。
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; } = AcpProtocolVersion.Default;

        /// <summary>
        /// Agent 信息。
        /// </summary>
        public AgentInfo AgentInfo { get; init; } = new AgentInfo();

        /// <summary>
        /// Agent 能力声明。
        /// </summary>
        public AgentCapabilities AgentCapabilities { get; init; } = new AgentCapabilities();

        /// <summary>
        /// 可选的认证方法列表（当 Agent 需要认证时提供）。
        /// </summary>
        [JsonPropertyName("authMethods")]
        public List<AuthMethodDefinition>? AuthMethods { get; init; }

        /// <summary>
        /// 扩展字段（_meta），用于协议可扩展性。
        /// </summary>
        /// <summary>
        /// 创建新的 InitializeResponse 实例。
        /// </summary>
        public InitializeResponse()
        {
        }

        /// <summary>
        /// 创建新的 InitializeResponse 实例。
        /// </summary>
        /// <param name="protocolVersion">协议版本</param>
        /// <param name="agentInfo">Agent 信息</param>
        /// <param name="agentCapabilities">Agent 能力</param>
        public InitializeResponse(int protocolVersion, AgentInfo agentInfo, AgentCapabilities agentCapabilities)
        {
            ProtocolVersion = protocolVersion;
            AgentInfo = agentInfo;
            AgentCapabilities = agentCapabilities;
        }
    }

    /// <summary>
    /// Agent 信息类。
    /// 包含 Agent 的名称、标题和版本信息。
    /// </summary>
    public sealed record AgentInfo : AcpProtocolObject
    {
        /// <summary>
        /// Agent 的名称（标识符）。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Agent 的显示标题。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// Agent 的版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = "1.0.0";

        /// <summary>
        /// 创建新的 AgentInfo 实例。
        /// </summary>
        public AgentInfo()
        {
        }

        /// <summary>
        /// 创建新的 AgentInfo 实例。
        /// </summary>
        /// <param name="name">Agent 名称</param>
        /// <param name="version">版本号</param>
        /// <param name="title">显示标题</param>
        [SetsRequiredMembers]
        public AgentInfo(string name, string version, string? title = null)
        {
            Name = name;
            Version = version;
            Title = title;
        }
    }

    /// <summary>
    /// Agent 能力声明类。
    /// 声明 Agent 支持的功能。
    /// </summary>
    public sealed record AgentCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 提示相关能力。
        /// </summary>
        [JsonPropertyName("promptCapabilities")]
        public PromptCapabilities? PromptCapabilities { get; init; }

        /// <summary>
        /// 是否支持会话加载。
        /// </summary>
        [JsonPropertyName("loadSession")]
        public bool? LoadSession { get; init; }

        /// <summary>
        /// MCP 相关能力。
        /// </summary>
        [JsonPropertyName("mcpCapabilities")]
        public McpCapabilities? McpCapabilities { get; init; }

        /// <summary>
        /// 会话相关能力。
        /// </summary>
        [JsonPropertyName("sessionCapabilities")]
        public SessionCapabilities? SessionCapabilities { get; init; }

        /// <summary>
        /// 认证相关能力。
        /// </summary>
        [JsonPropertyName("auth")]
        public AgentAuthCapabilities? Auth { get; init; }

        /// <summary>
        /// 创建新的 AgentCapabilities 实例。
        /// </summary>
        public AgentCapabilities()
        {
        }

        /// <summary>
        /// 创建新的 AgentCapabilities 实例。
        /// </summary>
        /// <param name="promptCapabilities">提示能力</param>
        /// <param name="loadSession">是否支持会话加载</param>
        /// <param name="mcpCapabilities">MCP 能力</param>
        /// <param name="sessionCapabilities">会话能力</param>
        /// <param name="auth">认证能力</param>
        public AgentCapabilities(
            PromptCapabilities? promptCapabilities = null,
            bool? loadSession = null,
            McpCapabilities? mcpCapabilities = null,
            SessionCapabilities? sessionCapabilities = null,
            AgentAuthCapabilities? auth = null)
        {
            PromptCapabilities = promptCapabilities;
            LoadSession = loadSession;
            McpCapabilities = mcpCapabilities;
            SessionCapabilities = sessionCapabilities;
            Auth = auth;
        }

        /// <summary>
        /// 判断是否支持图片内容。
        /// </summary>
        public bool SupportsImage => PromptCapabilities?.Image == true || SessionCapabilities?.Prompt?.Image == true;

        /// <summary>
        /// 判断是否支持音频内容。
        /// </summary>
        public bool SupportsAudio => PromptCapabilities?.Audio == true || SessionCapabilities?.Prompt?.Audio == true;

        /// <summary>
        /// 判断是否支持嵌入上下文。
        /// </summary>
        public bool SupportsEmbeddedContext => PromptCapabilities?.EmbeddedContext == true || SessionCapabilities?.Prompt?.EmbeddedContext == true;

        /// <summary>
        /// 判断是否支持会话加载。
        /// </summary>
        public bool SupportsSessionLoading => LoadSession ?? false;

        /// <summary>
        /// 判断是否支持会话恢复。
        /// </summary>
        public bool SupportsSessionResume => SessionCapabilities?.Resume != null;

        /// <summary>
        /// 判断是否支持会话关闭。
        /// </summary>
        public bool SupportsSessionClose => SessionCapabilities?.Close != null;

        /// <summary>
        /// 判断是否支持会话删除。
        /// </summary>
        public bool SupportsSessionDelete => SessionCapabilities?.Delete != null;

        /// <summary>
        /// 判断是否支持 additionalDirectories。
        /// </summary>
        public bool SupportsSessionAdditionalDirectories => SessionCapabilities?.AdditionalDirectories != null;

        /// <summary>
        /// 判断是否支持会话列表。
        /// </summary>
        public bool SupportsSessionList => SessionCapabilities?.List != null;

        /// <summary>
        /// 判断是否支持登出。
        /// </summary>
        public bool SupportsLogout => Auth?.Logout != null;

        /// <summary>
        /// 判断是否支持 HTTP 传输。
        /// </summary>
        public bool SupportsHttp => McpCapabilities?.Http == true || SessionCapabilities?.Mcp?.Http == true;

        /// <summary>
        /// 判断是否支持 SSE 传输。
        /// </summary>
        public bool SupportsSse => McpCapabilities?.Sse == true || SessionCapabilities?.Mcp?.Sse == true;

        /// <summary>
        /// 判断是否支持 stdio 传输。
        /// </summary>
        public bool SupportsStdio => SessionCapabilities?.Mcp?.SupportsStdio == true;
    }

    /// <summary>
    /// Agent 认证能力。
    /// </summary>
    public sealed record AgentAuthCapabilities : AcpProtocolObject
    {
        [JsonPropertyName("logout")]
        public LogoutCapabilities? Logout { get; init; }

    }

    /// <summary>
    /// Logout 方法能力。
    /// </summary>
    public sealed record LogoutCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// 提示相关能力类。
    /// </summary>
    public sealed record PromptCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 是否支持图片内容。
        /// </summary>
        [JsonPropertyName("image")]
        public bool Image { get; init; }

        /// <summary>
        /// 是否支持音频内容。
        /// </summary>
        [JsonPropertyName("audio")]
        public bool Audio { get; init; }

        /// <summary>
        /// 是否支持嵌入上下文。
        /// </summary>
        [JsonPropertyName("embeddedContext")]
        public bool EmbeddedContext { get; init; }

        /// <summary>
        /// 创建新的 PromptCapabilities 实例。
        /// </summary>
        public PromptCapabilities()
        {
        }

        /// <summary>
        /// 创建新的 PromptCapabilities 实例。
        /// </summary>
        /// <param name="image">是否支持图片</param>
        /// <param name="audio">是否支持音频</param>
        /// <param name="embeddedContext">是否支持嵌入上下文</param>
        public PromptCapabilities(bool image = false, bool audio = false, bool embeddedContext = false)
        {
            Image = image;
            Audio = audio;
            EmbeddedContext = embeddedContext;
        }
    }

    /// <summary>
    /// MCP 相关能力类。
    /// </summary>
    public sealed record McpCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 是否支持 HTTP 传输。
        /// </summary>
        [JsonPropertyName("http")]
        public bool Http { get; init; }

        /// <summary>
        /// 是否支持 SSE 传输。
        /// </summary>
        [JsonPropertyName("sse")]
        public bool Sse { get; init; }

        /// <summary>
        /// ACP 保留的扩展元数据。
        /// </summary>
        /// <summary>
        /// 创建新的 McpCapabilities 实例。
        /// </summary>
        public McpCapabilities()
        {
        }

        /// <summary>
        /// 创建新的 McpCapabilities 实例。
        /// </summary>
        /// <param name="http">是否支持 HTTP</param>
        /// <param name="sse">是否支持 SSE</param>
        /// <param name="meta">扩展元数据</param>
        public McpCapabilities(
            bool http = false,
            bool sse = false,
            Dictionary<string, object?>? meta = null,
            bool? stdio = null)
        {
            Http = http;
            Sse = sse;
            Meta = meta;
            Stdio = stdio;
        }

        /// <summary>
        /// 是否支持 stdio 传输。
        /// v1 wire 不公开该字段，v2 wire 通过 session.mcp.stdio 公开。
        /// </summary>
        [JsonIgnore]
        public bool? Stdio { get; init; }

        /// <summary>
        /// 判断是否支持 stdio 传输。
        /// </summary>
        public bool SupportsStdio => Stdio ?? false;
    }

    /// <summary>
    /// 会话相关能力类。
    /// </summary>
    public sealed record SessionCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 是否支持 prompt 扩展。
        /// </summary>
        [JsonPropertyName("prompt")]
        public PromptCapabilities? Prompt { get; init; }

        /// <summary>
        /// 是否支持 MCP 传输。
        /// </summary>
        [JsonPropertyName("mcp")]
        public McpCapabilities? Mcp { get; init; }

        /// <summary>
        /// 是否支持会话列表功能。
        /// </summary>
        [JsonPropertyName("list")]
        public SessionListCapabilities? List { get; init; }

        /// <summary>
        /// 是否支持会话恢复功能。
        /// </summary>
        [JsonPropertyName("resume")]
        public SessionResumeCapabilities? Resume { get; init; }

        /// <summary>
        /// 是否支持会话关闭功能。
        /// </summary>
        [JsonPropertyName("close")]
        public SessionCloseCapabilities? Close { get; init; }

        /// <summary>
        /// 是否支持会话删除功能。
        /// </summary>
        [JsonPropertyName("delete")]
        public SessionDeleteCapabilities? Delete { get; init; }

        /// <summary>
        /// 是否支持 additionalDirectories。
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public SessionAdditionalDirectoriesCapabilities? AdditionalDirectories { get; init; }

        /// <summary>
        /// 创建新的 SessionCapabilities 实例。
        /// </summary>
        public SessionCapabilities()
        {
        }
    }

    /// <summary>
    /// 会话列表能力类。
    /// </summary>
    public sealed record SessionListCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 创建新的 SessionListCapabilities 实例。
        /// </summary>
        public SessionListCapabilities()
        {
        }
    }

    /// <summary>
    /// 会话恢复能力类。
    /// </summary>
    public sealed record SessionResumeCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 创建新的 SessionResumeCapabilities 实例。
        /// </summary>
        public SessionResumeCapabilities()
        {
        }
    }

    /// <summary>
    /// 会话关闭能力类。
    /// </summary>
    public sealed record SessionCloseCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// 创建新的 SessionCloseCapabilities 实例。
        /// </summary>
        public SessionCloseCapabilities()
        {
        }
    }

    /// <summary>
    /// 会话删除能力类。
    /// </summary>
    public sealed record SessionDeleteCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// additionalDirectories 能力类。
    /// </summary>
    public sealed record SessionAdditionalDirectoriesCapabilities : AcpProtocolObject
    {
    }

    internal sealed class InitializeParamsJsonConverter : JsonConverter<InitializeParams>
    {
        public override InitializeParams? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            var result = new InitializeParams
            {
                ProtocolVersion = ReadProtocolVersion(root),
                ClientInfo = ReadClientInfo(root),
                ClientCapabilities = ReadClientCapabilities(root),
                Meta = AcpMetaJson.Read(root)
            };

            return result;
        }

        public override void Write(Utf8JsonWriter writer, InitializeParams value, JsonSerializerOptions options)
        {
            InitializeClientProtocolPolicy.Validate(value.ProtocolVersion, value.ClientCapabilities);

            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", value.ProtocolVersion);

            if (value.ProtocolVersion == AcpProtocolVersion.V1)
            {
                writer.WritePropertyName("clientInfo");
                JsonSerializer.Serialize(writer, value.ClientInfo, AcpJsonContext.Default.ClientInfo);
                writer.WritePropertyName("clientCapabilities");
                JsonSerializer.Serialize(writer, value.ClientCapabilities, AcpJsonContext.Default.ClientCapabilities);
            }
            else
            {
                writer.WritePropertyName("info");
                JsonSerializer.Serialize(writer, value.ClientInfo, AcpJsonContext.Default.ClientInfo);
                WriteClientCapabilitiesV2(writer, value.ClientCapabilities);
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static int ReadProtocolVersion(JsonElement root)
        {
            if (!root.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number)
            {
                return AcpProtocolVersion.V1;
            }

            return version.GetInt32();
        }

        private static ClientInfo ReadClientInfo(JsonElement root)
        {
            if (root.TryGetProperty("info", out var info))
            {
                return JsonSerializer.Deserialize(info.GetRawText(), AcpJsonContext.Default.ClientInfo) ?? new ClientInfo();
            }

            if (root.TryGetProperty("clientInfo", out var clientInfo))
            {
                return JsonSerializer.Deserialize(clientInfo.GetRawText(), AcpJsonContext.Default.ClientInfo) ?? new ClientInfo();
            }

            return new ClientInfo();
        }

        private static ClientCapabilities ReadClientCapabilities(JsonElement root)
        {
            if (root.TryGetProperty("capabilities", out var capabilities))
            {
                return JsonSerializer.Deserialize(capabilities.GetRawText(), AcpJsonContext.Default.ClientCapabilities) ?? new ClientCapabilities();
            }

            if (root.TryGetProperty("clientCapabilities", out var clientCapabilities))
            {
                return JsonSerializer.Deserialize(clientCapabilities.GetRawText(), AcpJsonContext.Default.ClientCapabilities) ?? new ClientCapabilities();
            }

            return new ClientCapabilities();
        }

        private static void WriteClientCapabilitiesV2(Utf8JsonWriter writer, ClientCapabilities value)
        {
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }

    internal sealed class InitializeResponseJsonConverter : JsonConverter<InitializeResponse>
    {
        public override InitializeResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            var result = new InitializeResponse
            {
                ProtocolVersion = ReadProtocolVersion(root),
                AgentInfo = ReadAgentInfo(root),
                AgentCapabilities = ReadAgentCapabilities(root),
                AuthMethods = ReadAuthMethods(root),
                Meta = AcpMetaJson.Read(root)
            };

            return result;
        }

        public override void Write(Utf8JsonWriter writer, InitializeResponse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", value.ProtocolVersion);

            if (value.ProtocolVersion == AcpProtocolVersion.V1)
            {
                writer.WritePropertyName("agentInfo");
                JsonSerializer.Serialize(writer, value.AgentInfo, AcpJsonContext.Default.AgentInfo);
                writer.WritePropertyName("agentCapabilities");
                JsonSerializer.Serialize(writer, value.AgentCapabilities, AcpJsonContext.Default.AgentCapabilities);
            }
            else
            {
                writer.WritePropertyName("info");
                JsonSerializer.Serialize(writer, value.AgentInfo, AcpJsonContext.Default.AgentInfo);
                WriteAgentCapabilitiesV2(writer, value.AgentCapabilities);
            }

            writer.WritePropertyName("authMethods");
            WriteAuthMethods(writer, value.AuthMethods, value.ProtocolVersion);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static int ReadProtocolVersion(JsonElement root)
        {
            if (!root.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number)
            {
                return AcpProtocolVersion.V1;
            }

            return version.GetInt32();
        }

        private static AgentInfo ReadAgentInfo(JsonElement root)
        {
            if (root.TryGetProperty("info", out var info))
            {
                return JsonSerializer.Deserialize(info.GetRawText(), AcpJsonContext.Default.AgentInfo) ?? new AgentInfo();
            }

            if (root.TryGetProperty("agentInfo", out var agentInfo))
            {
                return JsonSerializer.Deserialize(agentInfo.GetRawText(), AcpJsonContext.Default.AgentInfo) ?? new AgentInfo();
            }

            return new AgentInfo();
        }

        private static AgentCapabilities ReadAgentCapabilities(JsonElement root)
        {
            if (root.TryGetProperty("capabilities", out var capabilities))
            {
                return ReadAgentCapabilitiesV2(capabilities);
            }

            if (root.TryGetProperty("agentCapabilities", out var agentCapabilities))
            {
                return JsonSerializer.Deserialize(agentCapabilities.GetRawText(), AcpJsonContext.Default.AgentCapabilities) ?? new AgentCapabilities();
            }

            return new AgentCapabilities();
        }

        private static List<AuthMethodDefinition>? ReadAuthMethods(JsonElement root)
        {
            if (!root.TryGetProperty("authMethods", out var authMethods) || authMethods.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return JsonSerializer.Deserialize(authMethods.GetRawText(), AcpJsonContext.Default.ListAuthMethodDefinition);
        }

        private static AgentCapabilities ReadAgentCapabilitiesV2(JsonElement root)
        {
            SessionCapabilities? sessionCapabilities = null;
            AgentAuthCapabilities? auth = null;

            if (root.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
            {
                sessionCapabilities = ReadSessionCapabilitiesV2(session);
            }

            if (root.TryGetProperty("auth", out var authElement) && authElement.ValueKind == JsonValueKind.Object)
            {
                auth = JsonSerializer.Deserialize(authElement.GetRawText(), AcpJsonContext.Default.AgentAuthCapabilities);
            }

            return new AgentCapabilities
            {
                SessionCapabilities = sessionCapabilities,
                Auth = auth,
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static SessionCapabilities ReadSessionCapabilitiesV2(JsonElement session)
        {
            SessionDeleteCapabilities? deleteCapabilities = null;
            SessionAdditionalDirectoriesCapabilities? additionalDirectoriesCapabilities = null;
            PromptCapabilities? promptCapabilities = null;
            McpCapabilities? mcpCapabilities = null;

            if (session.TryGetProperty("delete", out var delete) && delete.ValueKind == JsonValueKind.Object)
            {
                deleteCapabilities = new SessionDeleteCapabilities();
            }

            if (session.TryGetProperty("additionalDirectories", out var additionalDirectories) && additionalDirectories.ValueKind == JsonValueKind.Object)
            {
                additionalDirectoriesCapabilities = new SessionAdditionalDirectoriesCapabilities();
            }

            if (session.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.Object)
            {
                promptCapabilities = new PromptCapabilities
                {
                    Image = IsObjectMarkerPresent(prompt, "image"),
                    Audio = IsObjectMarkerPresent(prompt, "audio"),
                    EmbeddedContext = IsObjectMarkerPresent(prompt, "embeddedContext"),
                    Meta = AcpMetaJson.Read(prompt)
                };
            }

            if (session.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
            {
                mcpCapabilities = new McpCapabilities(
                    http: IsObjectMarkerPresent(mcp, "http"),
                    sse: false,
                    meta: AcpMetaJson.Read(mcp),
                    stdio: IsObjectMarkerPresent(mcp, "stdio"));
            }

            return new SessionCapabilities
            {
                List = new SessionListCapabilities(),
                Resume = new SessionResumeCapabilities(),
                Close = new SessionCloseCapabilities(),
                Delete = deleteCapabilities,
                AdditionalDirectories = additionalDirectoriesCapabilities,
                Prompt = promptCapabilities,
                Mcp = mcpCapabilities,
                Meta = AcpMetaJson.Read(session)
            };
        }

        private static bool IsObjectMarkerPresent(JsonElement root, string propertyName)
            => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object;

        private static void WriteAgentCapabilitiesV2(Utf8JsonWriter writer, AgentCapabilities value)
        {
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();

            if (value.SessionCapabilities != null)
            {
                writer.WritePropertyName("session");
                writer.WriteStartObject();

                if (value.SessionCapabilities?.Prompt != null)
                {
                    writer.WritePropertyName("prompt");
                    writer.WriteStartObject();
                    WritePromptCapabilityMarker(writer, "image", value.SessionCapabilities.Prompt.Image);
                    WritePromptCapabilityMarker(writer, "audio", value.SessionCapabilities.Prompt.Audio);
                    WritePromptCapabilityMarker(writer, "embeddedContext", value.SessionCapabilities.Prompt.EmbeddedContext);
                    AcpMetaJson.Write(writer, value.SessionCapabilities.Prompt.Meta);
                    writer.WriteEndObject();
                }

                if (value.SessionCapabilities?.Mcp != null)
                {
                    writer.WritePropertyName("mcp");
                    writer.WriteStartObject();
                    if (value.SessionCapabilities.Mcp.SupportsStdio)
                    {
                        writer.WritePropertyName("stdio");
                        writer.WriteStartObject();
                        AcpMetaJson.Write(writer, value.SessionCapabilities.Mcp.Meta);
                        writer.WriteEndObject();
                    }

                    if (value.SessionCapabilities.Mcp.Http)
                    {
                        writer.WritePropertyName("http");
                        writer.WriteStartObject();
                        AcpMetaJson.Write(writer, value.SessionCapabilities.Mcp.Meta);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                if (value.SessionCapabilities?.Delete != null)
                {
                    writer.WritePropertyName("delete");
                    writer.WriteStartObject();
                    AcpMetaJson.Write(writer, value.SessionCapabilities.Delete.Meta);
                    writer.WriteEndObject();
                }

                if (value.SessionCapabilities?.AdditionalDirectories != null)
                {
                    writer.WritePropertyName("additionalDirectories");
                    writer.WriteStartObject();
                    AcpMetaJson.Write(writer, value.SessionCapabilities.AdditionalDirectories.Meta);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            if (value.Auth != null)
            {
                writer.WritePropertyName("auth");
                JsonSerializer.Serialize(writer, value.Auth, AcpJsonContext.Default.AgentAuthCapabilities);
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WritePromptCapabilityMarker(Utf8JsonWriter writer, string name, bool supported)
        {
            if (!supported)
            {
                return;
            }

            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        private static void WriteAuthMethods(Utf8JsonWriter writer, List<AuthMethodDefinition>? authMethods, int protocolVersion)
        {
            if (authMethods == null)
            {
                writer.WriteStartArray();
                writer.WriteEndArray();
                return;
            }

            writer.WriteStartArray();
            foreach (var authMethod in authMethods)
            {
                if (protocolVersion == AcpProtocolVersion.V1)
                {
                    WriteAuthMethodV1(writer, authMethod);
                }
                else
                {
                    WriteAuthMethodV2(writer, authMethod);
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteAuthMethodV1(Utf8JsonWriter writer, AuthMethodDefinition authMethod)
        {
            writer.WriteStartObject();
            writer.WriteString("id", authMethod.Id);
            writer.WriteString("name", authMethod.Name);
            if (!string.IsNullOrWhiteSpace(authMethod.Description))
            {
                writer.WriteString("description", authMethod.Description);
            }

            if (!string.IsNullOrWhiteSpace(authMethod.Type))
            {
                writer.WriteString("type", authMethod.Type);
            }

            AcpMetaJson.Write(writer, authMethod.Meta);
            writer.WriteEndObject();
        }

        private static void WriteAuthMethodV2(Utf8JsonWriter writer, AuthMethodDefinition authMethod)
        {
            writer.WriteStartObject();
            writer.WriteString("methodId", authMethod.Id);
            writer.WriteString("name", authMethod.Name);
            writer.WriteString("type", string.IsNullOrWhiteSpace(authMethod.Type) ? "agent" : authMethod.Type);
            if (!string.IsNullOrWhiteSpace(authMethod.Description))
            {
                writer.WriteString("description", authMethod.Description);
            }

            AcpMetaJson.Write(writer, authMethod.Meta);
            writer.WriteEndObject();
        }
    }
}
