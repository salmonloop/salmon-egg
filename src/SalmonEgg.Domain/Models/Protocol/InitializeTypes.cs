using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Initialize 方法的请求参数。
    /// 用于客户端向 Agent 发起初始化请求。
    /// </summary>
    public class InitializeParams
    {
        /// <summary>
        /// 协议版本号。必须是整数。
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// 客户端信息。
        /// </summary>
        [JsonPropertyName("clientInfo")]
        public ClientInfo ClientInfo { get; set; } = new ClientInfo();

        /// <summary>
        /// 客户端能力声明。
        /// </summary>
        [JsonPropertyName("clientCapabilities")]
        public ClientCapabilities ClientCapabilities { get; set; } = new ClientCapabilities();

        /// <summary>
        /// 扩展字段（_meta），用于协议可扩展性。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }

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
    public class ClientInfo
    {
        /// <summary>
        /// 客户端的名称（标识符）。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 客户端的显示标题。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 客户端的版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

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
    public class ClientCapabilities
    {
        /// <summary>
        /// 文件系统能力。
        /// </summary>
        [JsonPropertyName("fs")]
        public FsCapability? Fs { get; set; }

        /// <summary>
        /// 终端能力。
        /// </summary>
        [JsonPropertyName("terminal")]
        public bool? Terminal { get; set; }

        /// <summary>
        /// 扩展字段（_meta），用于声明自定义客户端能力。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }

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
        /// <param name="meta">扩展能力元数据</param>
        public ClientCapabilities(
            FsCapability? fs = null,
            bool? terminal = null,
            Dictionary<string, object?>? meta = null)
        {
            Fs = fs;
            Terminal = terminal;
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
    /// 文件系统能力类。
    /// </summary>
    public class FsCapability
    {
        /// <summary>
        /// 是否支持读取文本文件。
        /// </summary>
        [JsonPropertyName("readTextFile")]
        public bool ReadTextFile { get; set; } = true;

        /// <summary>
        /// 是否支持写入文本文件。
        /// </summary>
        [JsonPropertyName("writeTextFile")]
        public bool WriteTextFile { get; set; } = true;

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

    /// <summary>
    /// Initialize 方法的响应。
    /// Agent 对初始化请求的响应。
    /// </summary>
    public class InitializeResponse
    {
        /// <summary>
        /// 协议版本号。必须是整数。
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// Agent 信息。
        /// </summary>
        [JsonPropertyName("agentInfo")]
        public AgentInfo AgentInfo { get; set; } = new AgentInfo();

        /// <summary>
        /// Agent 能力声明。
        /// </summary>
        [JsonPropertyName("agentCapabilities")]
        public AgentCapabilities AgentCapabilities { get; set; } = new AgentCapabilities();

        /// <summary>
        /// 可选的认证方法列表（当 Agent 需要认证时提供）。
        /// </summary>
        [JsonPropertyName("authMethods")]
        public List<AuthMethodDefinition>? AuthMethods { get; set; }

        /// <summary>
        /// 扩展字段（_meta），用于协议可扩展性。
        /// </summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; set; }

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
    public class AgentInfo
    {
        /// <summary>
        /// Agent 的名称（标识符）。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Agent 的显示标题。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Agent 的版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

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
    public class AgentCapabilities
    {
        /// <summary>
        /// 提示相关能力。
        /// </summary>
        [JsonPropertyName("promptCapabilities")]
        public PromptCapabilities? PromptCapabilities { get; set; }

        /// <summary>
        /// 是否支持会话加载。
        /// </summary>
        [JsonPropertyName("loadSession")]
        public bool? LoadSession { get; set; }

        /// <summary>
        /// MCP 相关能力。
        /// </summary>
        [JsonPropertyName("mcpCapabilities")]
        public McpCapabilities? McpCapabilities { get; set; }

        /// <summary>
        /// 会话相关能力。
        /// </summary>
        [JsonPropertyName("sessionCapabilities")]
        public SessionCapabilities? SessionCapabilities { get; set; }

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
        public AgentCapabilities(
            PromptCapabilities? promptCapabilities = null,
            bool? loadSession = null,
            McpCapabilities? mcpCapabilities = null,
            SessionCapabilities? sessionCapabilities = null)
        {
            PromptCapabilities = promptCapabilities;
            LoadSession = loadSession;
            McpCapabilities = mcpCapabilities;
            SessionCapabilities = sessionCapabilities;
        }

        /// <summary>
        /// 判断是否支持图片内容。
        /// </summary>
        public bool SupportsImage => PromptCapabilities?.Image ?? false;

        /// <summary>
        /// 判断是否支持音频内容。
        /// </summary>
        public bool SupportsAudio => PromptCapabilities?.Audio ?? false;

        /// <summary>
        /// 判断是否支持嵌入上下文。
        /// </summary>
        public bool SupportsEmbeddedContext => PromptCapabilities?.EmbeddedContext ?? false;

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
        /// 判断是否支持会话列表。
        /// </summary>
        public bool SupportsSessionList => SessionCapabilities?.List != null;

        /// <summary>
        /// 判断是否支持 HTTP 传输。
        /// </summary>
        public bool SupportsHttp => McpCapabilities?.Http ?? false;

        /// <summary>
        /// 判断是否支持 SSE 传输。
        /// </summary>
        public bool SupportsSse => McpCapabilities?.Sse ?? false;
    }

    /// <summary>
    /// 提示相关能力类。
    /// </summary>
    public class PromptCapabilities
    {
        /// <summary>
        /// 是否支持图片内容。
        /// </summary>
        [JsonPropertyName("image")]
        public bool Image { get; set; }

        /// <summary>
        /// 是否支持音频内容。
        /// </summary>
        [JsonPropertyName("audio")]
        public bool Audio { get; set; }

        /// <summary>
        /// 是否支持嵌入上下文。
        /// </summary>
        [JsonPropertyName("embeddedContext")]
        public bool EmbeddedContext { get; set; }

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
    public class McpCapabilities
    {
        /// <summary>
        /// 是否支持 HTTP 传输。
        /// </summary>
        [JsonPropertyName("http")]
        public bool Http { get; set; }

        /// <summary>
        /// 是否支持 SSE 传输。
        /// </summary>
        [JsonPropertyName("sse")]
        public bool Sse { get; set; }

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
        public McpCapabilities(bool http = false, bool sse = false)
        {
            Http = http;
            Sse = sse;
        }
    }

    /// <summary>
    /// 会话相关能力类。
    /// </summary>
    public class SessionCapabilities
    {
        /// <summary>
        /// 是否支持会话列表功能。
        /// </summary>
        [JsonPropertyName("list")]
        public SessionListCapabilities? List { get; set; }

        /// <summary>
        /// 是否支持会话恢复功能。
        /// </summary>
        [JsonPropertyName("resume")]
        public SessionResumeCapabilities? Resume { get; set; }

        /// <summary>
        /// 是否支持会话关闭功能。
        /// </summary>
        [JsonPropertyName("close")]
        public SessionCloseCapabilities? Close { get; set; }

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
    public class SessionListCapabilities
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
    public class SessionResumeCapabilities
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
    public class SessionCloseCapabilities
    {
        /// <summary>
        /// 创建新的 SessionCloseCapabilities 实例。
        /// </summary>
        public SessionCloseCapabilities()
        {
        }
    }
}
