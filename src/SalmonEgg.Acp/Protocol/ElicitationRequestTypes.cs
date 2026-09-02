using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Scope binding an elicitation either to a session (optionally a tool call within it) or to a
    /// JSON-RPC request outside any session.
    /// </summary>
    /// <remarks>
    /// The wire form is flattened onto the request object rather than nested: a session-scoped request
    /// carries <c>sessionId</c> (plus optional <c>toolCallId</c>), and a request-scoped one carries
    /// <c>requestId</c>. Request scope exists for the auth and configuration phases, before any session
    /// has been created.
    /// </remarks>
    public sealed record ElicitationScope
    {
        /// <summary>
        /// The session this elicitation is tied to, or <c>null</c> for a request-scoped elicitation.
        /// </summary>
        public string? SessionId { get; init; }

        /// <summary>
        /// The tool call within the session, when the elicitation originated from one (for example an
        /// elicitation an Agent received from an MCP server during a tool call and redirected to the
        /// user). <c>null</c> means the elicitation is scoped to the session as a whole.
        /// </summary>
        public string? ToolCallId { get; init; }

        /// <summary>
        /// The JSON-RPC request this elicitation is tied to, or <c>null</c> for a session-scoped
        /// elicitation. Carried as the raw JSON token because a JSON-RPC id may be a string, a number, or
        /// null, and it must be echoed back in the shape it arrived.
        /// </summary>
        public JsonElement? RequestId { get; init; }

        /// <summary>
        /// Whether this scope is bound to a session.
        /// </summary>
        [JsonIgnore]
        public bool IsSessionScoped => SessionId is not null;

        /// <summary>
        /// Creates a session-scoped elicitation scope.
        /// </summary>
        /// <param name="sessionId">The session the elicitation belongs to.</param>
        /// <param name="toolCallId">The originating tool call, when there is one.</param>
        public static ElicitationScope ForSession(string sessionId, string? toolCallId = null)
        {
            ArgumentNullException.ThrowIfNull(sessionId);
            return new ElicitationScope { SessionId = sessionId, ToolCallId = toolCallId };
        }

        /// <summary>
        /// Creates a request-scoped elicitation scope.
        /// </summary>
        /// <param name="requestId">The JSON-RPC request the elicitation belongs to.</param>
        public static ElicitationScope ForRequest(JsonElement requestId)
            => new() { RequestId = requestId.Clone() };
    }

    /// <summary>
    /// Agent-to-client request for structured user input, via a form or a URL.
    /// </summary>
    [JsonConverter(typeof(CreateElicitationRequestJsonConverter))]
    public abstract record CreateElicitationRequest : AcpProtocolObject
    {
        /// <summary>
        /// A human-readable message describing what input is needed.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// The scope this elicitation is bound to.
        /// </summary>
        [JsonIgnore]
        public ElicitationScope Scope { get; init; } = new();

        /// <summary>
        /// The raw <c>mode</c> discriminator value carried on the wire.
        /// </summary>
        [JsonIgnore]
        public abstract string Mode { get; }
    }

    /// <summary>
    /// Form-mode elicitation: the client renders a form from the requested schema.
    /// </summary>
    /// <remarks>
    /// Form mode must never be used to request secrets or credentials that grant access or authorize
    /// transactions; the specification directs Agents to URL mode for those, and forbids falling back to
    /// form mode when the client does not advertise URL support.
    /// </remarks>
    public sealed record FormElicitationRequest : CreateElicitationRequest
    {
        /// <summary>
        /// A JSON Schema describing the form fields to present to the user.
        /// </summary>
        [JsonPropertyName("requestedSchema")]
        public ElicitationSchema RequestedSchema { get; init; } = new();

        /// <inheritdoc />
        [JsonIgnore]
        public override string Mode => ElicitationModes.Form;
    }

    /// <summary>
    /// URL-mode elicitation: the client shows the target and, on consent, directs the user to the URL.
    /// </summary>
    /// <remarks>
    /// An <c>accept</c> response means the user consented to opening the URL; it does not mean the
    /// external interaction completed. Completion arrives separately as
    /// <see cref="ElicitationMethods.Complete"/>.
    /// </remarks>
    public sealed record UrlElicitationRequest : CreateElicitationRequest
    {
        /// <summary>
        /// The unique identifier for this elicitation, opaque to the client.
        /// </summary>
        [JsonPropertyName("elicitationId")]
        public string ElicitationId { get; init; } = string.Empty;

        /// <summary>
        /// The URL to direct the user to.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        /// <inheritdoc />
        [JsonIgnore]
        public override string Mode => ElicitationModes.Url;
    }

    /// <summary>
    /// Custom or future elicitation mode.
    /// </summary>
    /// <remarks>
    /// The specification requires a client that does not understand the mode to preserve the raw payload
    /// when storing, replaying, proxying, or forwarding the request, and forbids rendering it as a known
    /// mode. <see cref="RawPayload"/> is the authoritative source for this variant.
    /// </remarks>
    public sealed record CustomElicitationRequest : CreateElicitationRequest
    {
        /// <summary>
        /// The raw <c>mode</c> value (a <c>_</c>-prefixed extension or a future ACP variant value).
        /// </summary>
        [JsonPropertyName("mode")]
        public string RawMode { get; init; } = string.Empty;

        /// <summary>
        /// The raw request params object, preserved verbatim for passthrough.
        /// Read and written by <see cref="CreateElicitationRequestJsonConverter"/>, bypassing default
        /// serialization.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }

        /// <inheritdoc />
        [JsonIgnore]
        public override string Mode => RawMode;
    }

    internal sealed class CreateElicitationRequestJsonConverter : JsonConverter<CreateElicitationRequest>
    {
        public override CreateElicitationRequest? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("elicitation/create params must be an object.");
            }

            var mode = ElicitationSchemaJson.ReadOptionalString(root, "mode");
            var message = ReadRequiredMessage(root);
            var scope = ReadScope(root);
            var meta = AcpMetaJson.Read(root);

            return mode switch
            {
                ElicitationModes.Form => new FormElicitationRequest
                {
                    Message = message,
                    Scope = scope,
                    RequestedSchema = ReadRequestedSchema(root),
                    Meta = meta
                },
                ElicitationModes.Url => new UrlElicitationRequest
                {
                    Message = message,
                    Scope = scope,
                    ElicitationId = ReadRequiredString(root, "elicitationId"),
                    Url = ReadRequiredString(root, "url"),
                    Meta = meta
                },
                // ACP requires mode explicitly and does not apply MCP's omitted-mode form default, so an
                // absent mode is not silently promoted to form: it lands in the passthrough variant with
                // an empty discriminator, leaving acceptance to the Agent.
                _ => new CustomElicitationRequest
                {
                    Message = message,
                    Scope = scope,
                    RawMode = mode ?? string.Empty,
                    RawPayload = root.Clone(),
                    Meta = meta
                }
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            CreateElicitationRequest value,
            JsonSerializerOptions options)
        {
            if (value is CustomElicitationRequest custom)
            {
                WritePassthrough(writer, custom);
                return;
            }

            writer.WriteStartObject();
            WriteScope(writer, value.Scope);
            writer.WriteString("mode", value.Mode);
            writer.WriteString("message", value.Message);

            switch (value)
            {
                case FormElicitationRequest form:
                    writer.WritePropertyName("requestedSchema");
                    JsonSerializer.Serialize(
                        writer,
                        form.RequestedSchema,
                        AcpJsonContext.Default.ElicitationSchema);
                    break;
                case UrlElicitationRequest url:
                    writer.WriteString("elicitationId", url.ElicitationId);
                    writer.WriteString("url", url.Url);
                    break;
                default:
                    throw new JsonException(
                        $"Unsupported elicitation request type: {value.GetType().FullName}");
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WritePassthrough(Utf8JsonWriter writer, CustomElicitationRequest custom)
        {
            // See ElicitationSchemaJson.WritePassthrough for why the raw text is replayed rather than
            // re-serialized; the fallback differs only in that the scope must be re-emitted here.
            if (custom.RawPayload.ValueKind == JsonValueKind.Object)
            {
                writer.WriteRawValue(custom.RawPayload.GetRawText());
                return;
            }

            writer.WriteStartObject();
            WriteScope(writer, custom.Scope);
            writer.WriteString("mode", custom.RawMode);
            writer.WriteString("message", custom.Message);
            AcpMetaJson.Write(writer, custom.Meta);
            writer.WriteEndObject();
        }

        private static string ReadRequiredMessage(JsonElement root)
        {
            var message = ElicitationSchemaJson.ReadOptionalString(root, "message");
            return message ?? throw new JsonException("elicitation/create is missing required 'message'.");
        }

        private static string ReadRequiredString(JsonElement root, string propertyName)
        {
            var value = ElicitationSchemaJson.ReadOptionalString(root, propertyName);
            return value
                ?? throw new JsonException($"elicitation/create is missing required '{propertyName}'.");
        }

        private static ElicitationSchema ReadRequestedSchema(JsonElement root)
        {
            if (!root.TryGetProperty("requestedSchema", out var schemaElement)
                || schemaElement.ValueKind == JsonValueKind.Null)
            {
                throw new JsonException("elicitation/create form mode is missing required 'requestedSchema'.");
            }

            return schemaElement.Deserialize(AcpJsonContext.Default.ElicitationSchema)
                ?? throw new JsonException("elicitation/create 'requestedSchema' must be an object.");
        }

        private static ElicitationScope ReadScope(JsonElement root)
        {
            if (root.TryGetProperty("sessionId", out var sessionId)
                && sessionId.ValueKind == JsonValueKind.String)
            {
                return new ElicitationScope
                {
                    SessionId = sessionId.GetString(),
                    ToolCallId = ElicitationSchemaJson.ReadOptionalString(root, "toolCallId")
                };
            }

            // requestId may legitimately be a string, a number, or null, so it is kept as the raw token
            // rather than coerced; only its absence means "not request-scoped".
            if (root.TryGetProperty("requestId", out var requestId))
            {
                return new ElicitationScope { RequestId = requestId.Clone() };
            }

            return new ElicitationScope();
        }

        private static void WriteScope(Utf8JsonWriter writer, ElicitationScope scope)
        {
            if (scope.SessionId is not null)
            {
                writer.WriteString("sessionId", scope.SessionId);
                if (scope.ToolCallId is not null)
                {
                    writer.WriteString("toolCallId", scope.ToolCallId);
                }

                return;
            }

            if (scope.RequestId.HasValue)
            {
                writer.WritePropertyName("requestId");
                writer.WriteRawValue(scope.RequestId.Value.GetRawText());
            }
        }
    }
}
