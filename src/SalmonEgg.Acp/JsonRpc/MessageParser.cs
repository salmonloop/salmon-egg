using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 message parser implementation.
    /// Uses System.Text.Json to parse and serialize messages.
    /// </summary>
    internal sealed class MessageParser
    {
        private readonly JsonSerializerOptions _options;

        /// <summary>
        /// Gets the JsonSerializerOptions instance for external use.
        /// </summary>
        public JsonSerializerOptions Options => _options;

        /// <summary>
        /// Creates a new MessageParser instance.
        /// </summary>
        public MessageParser()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                IncludeFields = false,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = false,
                // ACP agents can be strict about optional fields; omit nulls rather than writing `"foo": null`.
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                // ACP session/update payloads may place protocol extension fields like `_meta`
                // before the polymorphic discriminator (`sessionUpdate`).
                AllowOutOfOrderMetadataProperties = true,
                // Public protocol contracts + internal JSON-RPC envelopes.
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    AcpJsonContext.Default,
                    AcpJsonRpcContext.Default)
            };
        }

        /// <summary>
        /// Parses a JSON string into a JSON-RPC message.
        /// </summary>
        public JsonRpcMessage ParseMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    "Empty or null JSON message");
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return ParseMessage(doc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    $"Invalid JSON: {ex.Message}",
                    ex);
            }
            catch (AcpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    $"Error parsing message: {ex.Message}",
                    ex);
            }
        }

        internal ParsedMessageFrame ParseFrame(string json, int protocolVersion)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return new ParsedMessageFrame(false, false, [new ParsedMessageItem(ParseMessage(root))]);
                }

                if (protocolVersion != AcpProtocolVersion.V2 || root.GetArrayLength() == 0)
                {
                    throw new AcpException(JsonRpcErrorCode.InvalidRequest,
                        protocolVersion == AcpProtocolVersion.V2
                            ? "A JSON-RPC batch must contain at least one item."
                            : "JSON-RPC batches require a negotiated ACP v2 connection.");
                }

                return ParseBatch(root);
            }
            catch (JsonException exception)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, exception.Message, exception);
            }
        }

        internal string SerializeResponses(IReadOnlyList<JsonRpcResponse> responses)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartArray();
            foreach (var response in responses)
            {
                JsonSerializer.Serialize(writer, response, GetTypeInfo<JsonRpcResponse>());
            }
            writer.WriteEndArray();
            writer.Flush();
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private JsonRpcMessage ParseMessage(JsonElement root)
        {
            if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _))
            {
                return root.Deserialize(GetTypeInfo<JsonRpcResponse>())
                    ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse response");
            }

            if (root.TryGetProperty("id", out var id))
            {
                var request = root.Deserialize(GetTypeInfo<JsonRpcRequest>())
                    ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse request");
                // Keep an explicit null id distinct from an absent id. Event callbacks require an
                // object identity, while JsonElement preserves all three schema-defined wire forms.
                request.Id = id.Clone();
                return request;
            }

            return root.Deserialize(GetTypeInfo<JsonRpcNotification>())
                ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse notification");
        }

        private ParsedMessageFrame ParseBatch(JsonElement root)
        {
            var items = new List<ParsedMessageItem>(root.GetArrayLength());
            var hasCalls = false;
            var hasResponses = false;
            foreach (var item in root.EnumerateArray())
            {
                var isResponse = item.ValueKind == JsonValueKind.Object
                    && (item.TryGetProperty("result", out _) || item.TryGetProperty("error", out _));
                hasResponses |= isResponse;
                hasCalls |= item.ValueKind == JsonValueKind.Object && item.TryGetProperty("method", out _);
                var error = ValidateBatchEnvelope(item);
                try
                {
                    items.Add(error is null
                        ? new ParsedMessageItem(ParseMessage(item), IsResponse: isResponse)
                        : new ParsedMessageItem(null, error, isResponse));
                }
                catch (JsonException exception)
                {
                    items.Add(new ParsedMessageItem(null, exception.Message, isResponse));
                }
            }

            // ACP's schema separates BatchCall from BatchResponse. A response smuggled into a call
            // batch cannot settle a pending request, but it must not discard the legitimate calls.
            if (hasCalls && hasResponses)
            {
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index].IsResponse)
                    {
                        items[index] = new ParsedMessageItem(null,
                            "ACP call batches must not contain response items.", IsResponse: true);
                    }
                }
            }

            return new ParsedMessageFrame(true, hasResponses && !hasCalls, items);
        }

        private static string? ValidateBatchEnvelope(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
            {
                return "A batch item must be a JSON-RPC 2.0 object.";
            }

            var hasId = item.TryGetProperty("id", out var id);
            if (hasId && !AcpRequestId.TryFromEnvelopeId(id, out _))
            {
                return "A JSON-RPC id must be null, a number, or a string.";
            }

            var hasMethod = item.TryGetProperty("method", out var method);
            var hasResult = item.TryGetProperty("result", out _);
            var hasError = item.TryGetProperty("error", out var error);
            if (hasMethod)
            {
                if (method.ValueKind != JsonValueKind.String || hasResult || hasError)
                {
                    return "A call must contain a string method and no result or error.";
                }
                if (item.TryGetProperty("params", out var parameters)
                    && parameters.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    return "JSON-RPC params must be an object or an array when present.";
                }
                return null;
            }

            if (!hasId || hasResult == hasError)
            {
                return "A response must contain an id and exactly one of result or error.";
            }
            if (hasError && (error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out _)
                || !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String))
            {
                return "A JSON-RPC error must contain an integer code and a string message.";
            }
            return null;
        }

        /// <summary>
        /// Parses a JSON string into a request message.
        /// </summary>
        public JsonRpcRequest ParseRequest(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcRequest request)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a request (missing 'id' field or wrong type)");
            }

            return request;
        }

        /// <summary>
        /// Parses a JSON string into a notification message.
        /// </summary>
        public JsonRpcNotification ParseNotification(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcNotification notification)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a notification (should not have 'id' field)");
            }

            return notification;
        }

        /// <summary>
        /// Parses a JSON string into a response message.
        /// </summary>
        public JsonRpcResponse ParseResponse(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcResponse response)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a response (missing 'result' or 'error' field)");
            }

            return response;
        }

        /// <summary>
        /// Serializes a JSON-RPC message into a JSON string.
        /// </summary>
        public string SerializeMessage(JsonRpcMessage message)
        {
            if (message == null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Cannot serialize null message");
            }

            try
            {
                return message switch
                {
                    JsonRpcRequest request => JsonSerializer.Serialize(request, GetTypeInfo<JsonRpcRequest>()),
                    JsonRpcNotification notification => JsonSerializer.Serialize(notification, GetTypeInfo<JsonRpcNotification>()),
                    JsonRpcResponse response => JsonSerializer.Serialize(response, GetTypeInfo<JsonRpcResponse>()),
                    _ => throw new JsonException($"Unknown JsonRpcMessage type: {message.GetType().Name}")
                };
            }
            catch (JsonException ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InternalError,
                    $"Failed to serialize message: {ex.Message}",
                    ex);
            }
        }

        private JsonTypeInfo<T> GetTypeInfo<T>()
        {
            return (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
        }
    }
}
