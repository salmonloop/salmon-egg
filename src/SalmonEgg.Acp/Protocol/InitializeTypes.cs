using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the Initialize method.
    /// Used by the client to send an initialization request to the Agent.
    /// </summary>
    [JsonConverter(typeof(InitializeParamsJsonConverter))]
    public sealed record InitializeParams : AcpProtocolObject
    {
        /// <summary>
        /// The protocol version number. Must be an integer.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; } = AcpProtocolVersion.Default;

        /// <summary>
        /// Client information.
        /// </summary>
        [JsonPropertyName("clientInfo")]
        public ClientInfo ClientInfo { get; init; } = new ClientInfo();

        /// <summary>
        /// Client capability declaration.
        /// </summary>
        [JsonPropertyName("clientCapabilities")]
        public ClientCapabilities ClientCapabilities { get; init; } = new ClientCapabilities();

        /// <summary>
        /// Extension field (_meta) used for protocol extensibility.
        /// </summary>
        /// <summary>
        /// Creates a new InitializeParams instance.
        /// </summary>
        public InitializeParams()
        {
        }

        /// <summary>
        /// Creates a new InitializeParams instance.
        /// </summary>
        /// <param name="clientInfo">Client information</param>
        /// <param name="clientCapabilities">Client capabilities</param>
        public InitializeParams(ClientInfo clientInfo, ClientCapabilities clientCapabilities)
        {
            ClientInfo = clientInfo;
            ClientCapabilities = clientCapabilities;
        }
    }

    /// <summary>
    /// Client information class.
    /// Contains the name, title, and version information of the client.
    /// </summary>
    public sealed record ClientInfo : AcpProtocolObject
    {
        /// <summary>
        /// The client name (identifier).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The client display title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// The client version.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = "1.0.0";

        /// <summary>
        /// Creates a new ClientInfo instance.
        /// </summary>
        public ClientInfo()
        {
        }

        /// <summary>
        /// Creates a new ClientInfo instance.
        /// </summary>
        /// <param name="name">Client name</param>
        /// <param name="version">Version</param>
        /// <param name="title">Display title</param>
        [SetsRequiredMembers]
        public ClientInfo(string name, string version, string? title = null)
        {
            Name = name;
            Version = version;
            Title = title;
        }
    }

    /// <summary>
    /// Client capability declaration class.
    /// Declares the features supported by the client.
    /// </summary>
    public sealed record ClientCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// File system capabilities.
        /// </summary>
        [JsonPropertyName("fs")]
        public FsCapability? Fs { get; init; }

        /// <summary>
        /// Terminal capability.
        /// </summary>
        [JsonPropertyName("terminal")]
        public bool? Terminal { get; init; }

        /// <summary>
        /// Session-related client capabilities.
        /// </summary>
        [JsonPropertyName("session")]
        public ClientSessionCapabilities? Session { get; init; }

        /// <summary>
        /// Elicitation capabilities, declaring which <c>elicitation/create</c> modes the agent may use.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Fs"/>, <see cref="Terminal"/>, and <see cref="Session"/>, this field is a
        /// root capability in both v1 and v2 schemas, so it survives the v2 wire form rather than being
        /// rejected as a legacy field.
        /// </remarks>
        [JsonPropertyName("elicitation")]
        public ElicitationCapabilities? Elicitation { get; init; }

        /// <summary>
        /// Extension field (_meta) used to declare custom client capabilities.
        /// </summary>
        /// <summary>
        /// Creates a new ClientCapabilities instance.
        /// </summary>
        public ClientCapabilities()
        {
        }

        /// <summary>
        /// Creates a new ClientCapabilities instance.
        /// </summary>
        /// <param name="fs">File system capabilities</param>
        /// <param name="terminal">Terminal capability</param>
        /// <param name="session">Session capabilities</param>
        /// <param name="meta">Extension capability metadata</param>
        /// <remarks>
        /// <see cref="Elicitation"/> is deliberately not a constructor parameter: adding one would change
        /// this published constructor's signature, which is binary-breaking for the shipped package even
        /// though an optional parameter looks source-compatible. Set it through the init-only property.
        /// </remarks>
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
        /// Determines whether support for the specified extension capability is declared.
        /// </summary>
        /// <param name="extensionName">Extension capability name</param>
        /// <returns>true if support is declared; otherwise false</returns>
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
    /// Client session capabilities.
    /// </summary>
    public sealed record ClientSessionCapabilities : AcpProtocolObject
    {
        [JsonPropertyName("configOptions")]
        public SessionConfigOptionsCapabilities? ConfigOptions { get; init; }

    }

    /// <summary>
    /// Client session configuration option capabilities.
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
    /// File system capability class.
    /// </summary>
    public sealed record FsCapability : AcpProtocolObject
    {
        /// <summary>
        /// Whether reading text files is supported.
        /// </summary>
        [JsonPropertyName("readTextFile")]
        public bool ReadTextFile { get; init; } = true;

        /// <summary>
        /// Whether writing text files is supported.
        /// </summary>
        [JsonPropertyName("writeTextFile")]
        public bool WriteTextFile { get; init; } = true;

        /// <summary>
        /// Creates a new FsCapability instance.
        /// </summary>
        public FsCapability()
        {
        }

        /// <summary>
        /// Creates a new FsCapability instance.
        /// </summary>
        /// <param name="readTextFile">Whether reading is supported</param>
        /// <param name="writeTextFile">Whether writing is supported</param>
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
    /// Response for the Initialize method.
    /// The Agent response to the initialization request.
    /// </summary>
    [JsonConverter(typeof(InitializeResponseJsonConverter))]
    public sealed record InitializeResponse : AcpProtocolObject
    {
        /// <summary>
        /// The protocol version number. Must be an integer.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; } = AcpProtocolVersion.Default;

        /// <summary>
        /// Agent information.
        /// </summary>
        public AgentInfo AgentInfo { get; init; } = new AgentInfo();

        /// <summary>
        /// Agent capability declaration.
        /// </summary>
        public AgentCapabilities AgentCapabilities { get; init; } = new AgentCapabilities();

        /// <summary>
        /// Optional list of authentication methods (provided when the Agent requires authentication).
        /// </summary>
        [JsonPropertyName("authMethods")]
        public List<AuthMethodDefinition>? AuthMethods { get; init; }

        /// <summary>
        /// Extension field (_meta) used for protocol extensibility.
        /// </summary>
        /// <summary>
        /// Creates a new InitializeResponse instance.
        /// </summary>
        public InitializeResponse()
        {
        }

        /// <summary>
        /// Creates a new InitializeResponse instance.
        /// </summary>
        /// <param name="protocolVersion">Protocol version</param>
        /// <param name="agentInfo">Agent information</param>
        /// <param name="agentCapabilities">Agent capabilities</param>
        public InitializeResponse(int protocolVersion, AgentInfo agentInfo, AgentCapabilities agentCapabilities)
        {
            ProtocolVersion = protocolVersion;
            AgentInfo = agentInfo;
            AgentCapabilities = agentCapabilities;
        }
    }

    /// <summary>
    /// Agent information class.
    /// Contains the name, title, and version information of the Agent.
    /// </summary>
    public sealed record AgentInfo : AcpProtocolObject
    {
        /// <summary>
        /// The Agent name (identifier).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The Agent display title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// The Agent version.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = "1.0.0";

        /// <summary>
        /// Creates a new AgentInfo instance.
        /// </summary>
        public AgentInfo()
        {
        }

        /// <summary>
        /// Creates a new AgentInfo instance.
        /// </summary>
        /// <param name="name">Agent name</param>
        /// <param name="version">Version</param>
        /// <param name="title">Display title</param>
        [SetsRequiredMembers]
        public AgentInfo(string name, string version, string? title = null)
        {
            Name = name;
            Version = version;
            Title = title;
        }
    }

    /// <summary>
    /// Agent capability declaration class.
    /// Declares the features supported by the Agent.
    /// </summary>
    public sealed record AgentCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Prompt-related capabilities.
        /// </summary>
        [JsonPropertyName("promptCapabilities")]
        public PromptCapabilities? PromptCapabilities { get; init; }

        /// <summary>
        /// Whether session loading is supported.
        /// </summary>
        [JsonPropertyName("loadSession")]
        public bool? LoadSession { get; init; }

        /// <summary>
        /// MCP-related capabilities.
        /// </summary>
        [JsonPropertyName("mcpCapabilities")]
        public McpCapabilities? McpCapabilities { get; init; }

        /// <summary>
        /// Session-related capabilities.
        /// </summary>
        [JsonPropertyName("sessionCapabilities")]
        public SessionCapabilities? SessionCapabilities { get; init; }

        /// <summary>
        /// Authentication-related capabilities.
        /// </summary>
        [JsonPropertyName("auth")]
        public AgentAuthCapabilities? Auth { get; init; }

        /// <summary>
        /// Creates a new AgentCapabilities instance.
        /// </summary>
        public AgentCapabilities()
        {
        }

        /// <summary>
        /// Creates a new AgentCapabilities instance.
        /// </summary>
        /// <param name="promptCapabilities">Prompt capabilities</param>
        /// <param name="loadSession">Whether session loading is supported</param>
        /// <param name="mcpCapabilities">MCP capabilities</param>
        /// <param name="sessionCapabilities">Session capabilities</param>
        /// <param name="auth">Authentication capabilities</param>
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
        /// Gets a value indicating whether image content is supported.
        /// </summary>
        public bool SupportsImage => PromptCapabilities?.Image == true || SessionCapabilities?.Prompt?.Image == true;

        /// <summary>
        /// Gets a value indicating whether audio content is supported.
        /// </summary>
        public bool SupportsAudio => PromptCapabilities?.Audio == true || SessionCapabilities?.Prompt?.Audio == true;

        /// <summary>
        /// Gets a value indicating whether embedded context is supported.
        /// </summary>
        public bool SupportsEmbeddedContext => PromptCapabilities?.EmbeddedContext == true || SessionCapabilities?.Prompt?.EmbeddedContext == true;

        /// <summary>
        /// Gets a value indicating whether session loading is supported.
        /// </summary>
        public bool SupportsSessionLoading => LoadSession ?? false;

        /// <summary>
        /// Gets a value indicating whether session resumption is supported.
        /// </summary>
        public bool SupportsSessionResume => SessionCapabilities?.Resume != null;

        /// <summary>
        /// Gets a value indicating whether closing a session is supported.
        /// </summary>
        public bool SupportsSessionClose => SessionCapabilities?.Close != null;

        /// <summary>
        /// Gets a value indicating whether deleting a session is supported.
        /// </summary>
        public bool SupportsSessionDelete => SessionCapabilities?.Delete != null;

        /// <summary>
        /// Gets a value indicating whether additionalDirectories is supported.
        /// </summary>
        public bool SupportsSessionAdditionalDirectories => SessionCapabilities?.AdditionalDirectories != null;

        /// <summary>
        /// Gets a value indicating whether listing sessions is supported.
        /// </summary>
        public bool SupportsSessionList => SessionCapabilities?.List != null;

        /// <summary>
        /// Gets a value indicating whether logout is supported.
        /// </summary>
        public bool SupportsLogout => Auth?.Logout != null;

        /// <summary>
        /// Gets a value indicating whether the HTTP transport is supported.
        /// </summary>
        public bool SupportsHttp => McpCapabilities?.Http == true || SessionCapabilities?.Mcp?.Http == true;

        /// <summary>
        /// Gets a value indicating whether the SSE transport is supported.
        /// </summary>
        public bool SupportsSse => McpCapabilities?.Sse == true || SessionCapabilities?.Mcp?.Sse == true;

        /// <summary>
        /// Gets a value indicating whether the stdio transport is supported.
        /// </summary>
        public bool SupportsStdio => SessionCapabilities?.Mcp?.SupportsStdio == true;
    }

    /// <summary>
    /// Agent authentication capabilities.
    /// </summary>
    public sealed record AgentAuthCapabilities : AcpProtocolObject
    {
        [JsonPropertyName("logout")]
        public LogoutCapabilities? Logout { get; init; }

    }

    /// <summary>
    /// Logout method capabilities.
    /// </summary>
    public sealed record LogoutCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// Prompt-related capability class.
    /// </summary>
    public sealed record PromptCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Whether image content is supported.
        /// </summary>
        [JsonPropertyName("image")]
        public bool Image { get; init; }

        /// <summary>
        /// Whether audio content is supported.
        /// </summary>
        [JsonPropertyName("audio")]
        public bool Audio { get; init; }

        /// <summary>
        /// Whether embedded context is supported.
        /// </summary>
        [JsonPropertyName("embeddedContext")]
        public bool EmbeddedContext { get; init; }

        /// <summary>
        /// Creates a new PromptCapabilities instance.
        /// </summary>
        public PromptCapabilities()
        {
        }

        /// <summary>
        /// Creates a new PromptCapabilities instance.
        /// </summary>
        /// <param name="image">Whether images are supported</param>
        /// <param name="audio">Whether audio is supported</param>
        /// <param name="embeddedContext">Whether embedded context is supported</param>
        public PromptCapabilities(bool image = false, bool audio = false, bool embeddedContext = false)
        {
            Image = image;
            Audio = audio;
            EmbeddedContext = embeddedContext;
        }
    }

    /// <summary>
    /// MCP-related capability class.
    /// </summary>
    public sealed record McpCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Whether the HTTP transport is supported.
        /// </summary>
        [JsonPropertyName("http")]
        public bool Http { get; init; }

        /// <summary>
        /// Whether the SSE transport is supported.
        /// </summary>
        [JsonPropertyName("sse")]
        public bool Sse { get; init; }

        /// <summary>
        /// Extension metadata reserved by ACP.
        /// </summary>
        /// <summary>
        /// Creates a new McpCapabilities instance.
        /// </summary>
        public McpCapabilities()
        {
        }

        /// <summary>
        /// Creates a new McpCapabilities instance.
        /// </summary>
        /// <param name="http">Whether HTTP is supported</param>
        /// <param name="sse">Whether SSE is supported</param>
        /// <param name="meta">Extension metadata</param>
        /// <param name="stdio">Whether stdio is supported; null when the wire did not carry the field</param>
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
        /// Whether the stdio transport is supported.
        /// The v1 wire does not expose this field; the v2 wire exposes it through session.mcp.stdio.
        /// </summary>
        [JsonIgnore]
        public bool? Stdio { get; init; }

        /// <summary>
        /// Gets a value indicating whether the stdio transport is supported.
        /// </summary>
        public bool SupportsStdio => Stdio ?? false;
    }

    /// <summary>
    /// Session-related capability class.
    /// </summary>
    public sealed record SessionCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Whether prompt extensions are supported.
        /// </summary>
        [JsonPropertyName("prompt")]
        public PromptCapabilities? Prompt { get; init; }

        /// <summary>
        /// Whether MCP transports are supported.
        /// </summary>
        [JsonPropertyName("mcp")]
        public McpCapabilities? Mcp { get; init; }

        /// <summary>
        /// Whether the session listing feature is supported.
        /// </summary>
        [JsonPropertyName("list")]
        public SessionListCapabilities? List { get; init; }

        /// <summary>
        /// Whether the session resume feature is supported.
        /// </summary>
        [JsonPropertyName("resume")]
        public SessionResumeCapabilities? Resume { get; init; }

        /// <summary>
        /// Whether the session close feature is supported.
        /// </summary>
        [JsonPropertyName("close")]
        public SessionCloseCapabilities? Close { get; init; }

        /// <summary>
        /// Whether the session delete feature is supported.
        /// </summary>
        [JsonPropertyName("delete")]
        public SessionDeleteCapabilities? Delete { get; init; }

        /// <summary>
        /// Whether additionalDirectories is supported.
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public SessionAdditionalDirectoriesCapabilities? AdditionalDirectories { get; init; }

        /// <summary>
        /// Creates a new SessionCapabilities instance.
        /// </summary>
        public SessionCapabilities()
        {
        }
    }

    /// <summary>
    /// Session listing capability class.
    /// </summary>
    public sealed record SessionListCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Creates a new SessionListCapabilities instance.
        /// </summary>
        public SessionListCapabilities()
        {
        }
    }

    /// <summary>
    /// Session resume capability class.
    /// </summary>
    public sealed record SessionResumeCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Creates a new SessionResumeCapabilities instance.
        /// </summary>
        public SessionResumeCapabilities()
        {
        }
    }

    /// <summary>
    /// Session close capability class.
    /// </summary>
    public sealed record SessionCloseCapabilities : AcpProtocolObject
    {
        /// <summary>
        /// Creates a new SessionCloseCapabilities instance.
        /// </summary>
        public SessionCloseCapabilities()
        {
        }
    }

    /// <summary>
    /// Session delete capability class.
    /// </summary>
    public sealed record SessionDeleteCapabilities : AcpProtocolObject
    {
    }

    /// <summary>
    /// additionalDirectories capability class.
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
                ClientInfo = ReadClientInfo(root, options),
                ClientCapabilities = ReadClientCapabilities(root, options),
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
                JsonSerializer.Serialize(writer, value.ClientInfo, (JsonTypeInfo<ClientInfo>)options.GetTypeInfo(typeof(ClientInfo)));
                writer.WritePropertyName("clientCapabilities");
                JsonSerializer.Serialize(writer, value.ClientCapabilities, (JsonTypeInfo<ClientCapabilities>)options.GetTypeInfo(typeof(ClientCapabilities)));
            }
            else
            {
                writer.WritePropertyName("info");
                JsonSerializer.Serialize(writer, value.ClientInfo, (JsonTypeInfo<ClientInfo>)options.GetTypeInfo(typeof(ClientInfo)));
                WriteClientCapabilitiesV2(writer, value.ClientCapabilities, options);
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

        private static ClientInfo ReadClientInfo(JsonElement root, JsonSerializerOptions options)
        {
            if (root.TryGetProperty("info", out var info))
            {
                return JsonSerializer.Deserialize(info.GetRawText(), (JsonTypeInfo<ClientInfo>)options.GetTypeInfo(typeof(ClientInfo))) ?? new ClientInfo();
            }

            if (root.TryGetProperty("clientInfo", out var clientInfo))
            {
                return JsonSerializer.Deserialize(clientInfo.GetRawText(), (JsonTypeInfo<ClientInfo>)options.GetTypeInfo(typeof(ClientInfo))) ?? new ClientInfo();
            }

            return new ClientInfo();
        }

        private static ClientCapabilities ReadClientCapabilities(JsonElement root, JsonSerializerOptions options)
        {
            if (root.TryGetProperty("capabilities", out var capabilities))
            {
                return JsonSerializer.Deserialize(capabilities.GetRawText(), (JsonTypeInfo<ClientCapabilities>)options.GetTypeInfo(typeof(ClientCapabilities))) ?? new ClientCapabilities();
            }

            if (root.TryGetProperty("clientCapabilities", out var clientCapabilities))
            {
                return JsonSerializer.Deserialize(clientCapabilities.GetRawText(), (JsonTypeInfo<ClientCapabilities>)options.GetTypeInfo(typeof(ClientCapabilities))) ?? new ClientCapabilities();
            }

            return new ClientCapabilities();
        }

        private static void WriteClientCapabilitiesV2(Utf8JsonWriter writer, ClientCapabilities value, JsonSerializerOptions options)
        {
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();

            // elicitation is a root capability in the v2 schema too (unlike fs/terminal/session, which
            // v2 dropped), so dropping it here would silently un-advertise a mode the client supports and
            // make every standards-compliant agent fall back.
            if (value.Elicitation is not null)
            {
                writer.WritePropertyName("elicitation");
                JsonSerializer.Serialize(writer, value.Elicitation, (JsonTypeInfo<ElicitationCapabilities>)options.GetTypeInfo(typeof(ElicitationCapabilities)));
            }

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
                AgentInfo = ReadAgentInfo(root, options),
                AgentCapabilities = ReadAgentCapabilities(root, options),
                AuthMethods = ReadAuthMethods(root, options),
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
                JsonSerializer.Serialize(writer, value.AgentInfo, (JsonTypeInfo<AgentInfo>)options.GetTypeInfo(typeof(AgentInfo)));
                writer.WritePropertyName("agentCapabilities");
                JsonSerializer.Serialize(writer, value.AgentCapabilities, (JsonTypeInfo<AgentCapabilities>)options.GetTypeInfo(typeof(AgentCapabilities)));
            }
            else
            {
                writer.WritePropertyName("info");
                JsonSerializer.Serialize(writer, value.AgentInfo, (JsonTypeInfo<AgentInfo>)options.GetTypeInfo(typeof(AgentInfo)));
                WriteAgentCapabilitiesV2(writer, value.AgentCapabilities, options);
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

        private static AgentInfo ReadAgentInfo(JsonElement root, JsonSerializerOptions options)
        {
            if (root.TryGetProperty("info", out var info))
            {
                return JsonSerializer.Deserialize(info.GetRawText(), (JsonTypeInfo<AgentInfo>)options.GetTypeInfo(typeof(AgentInfo))) ?? new AgentInfo();
            }

            if (root.TryGetProperty("agentInfo", out var agentInfo))
            {
                return JsonSerializer.Deserialize(agentInfo.GetRawText(), (JsonTypeInfo<AgentInfo>)options.GetTypeInfo(typeof(AgentInfo))) ?? new AgentInfo();
            }

            return new AgentInfo();
        }

        private static AgentCapabilities ReadAgentCapabilities(JsonElement root, JsonSerializerOptions options)
        {
            if (root.TryGetProperty("capabilities", out var capabilities))
            {
                return ReadAgentCapabilitiesV2(capabilities, options);
            }

            if (root.TryGetProperty("agentCapabilities", out var agentCapabilities))
            {
                return JsonSerializer.Deserialize(agentCapabilities.GetRawText(), (JsonTypeInfo<AgentCapabilities>)options.GetTypeInfo(typeof(AgentCapabilities))) ?? new AgentCapabilities();
            }

            return new AgentCapabilities();
        }

        private static List<AuthMethodDefinition>? ReadAuthMethods(JsonElement root, JsonSerializerOptions options)
        {
            if (!root.TryGetProperty("authMethods", out var authMethods) || authMethods.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return JsonSerializer.Deserialize(authMethods.GetRawText(), (JsonTypeInfo<List<AuthMethodDefinition>>)options.GetTypeInfo(typeof(List<AuthMethodDefinition>)));
        }

        private static AgentCapabilities ReadAgentCapabilitiesV2(JsonElement root, JsonSerializerOptions options)
        {
            SessionCapabilities? sessionCapabilities = null;
            AgentAuthCapabilities? auth = null;

            if (root.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
            {
                sessionCapabilities = ReadSessionCapabilitiesV2(session);
            }

            if (root.TryGetProperty("auth", out var authElement) && authElement.ValueKind == JsonValueKind.Object)
            {
                auth = JsonSerializer.Deserialize(authElement.GetRawText(), (JsonTypeInfo<AgentAuthCapabilities>)options.GetTypeInfo(typeof(AgentAuthCapabilities)));
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

        private static void WriteAgentCapabilitiesV2(Utf8JsonWriter writer, AgentCapabilities value, JsonSerializerOptions options)
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
                JsonSerializer.Serialize(writer, value.Auth, (JsonTypeInfo<AgentAuthCapabilities>)options.GetTypeInfo(typeof(AgentAuthCapabilities)));
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
            writer.WriteString("type", authMethod.ResolvedType);
            if (!string.IsNullOrWhiteSpace(authMethod.Description))
            {
                writer.WriteString("description", authMethod.Description);
            }

            AcpMetaJson.Write(writer, authMethod.Meta);
            writer.WriteEndObject();
        }
    }
}
