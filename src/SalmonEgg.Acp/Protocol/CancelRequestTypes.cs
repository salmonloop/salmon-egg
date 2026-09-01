using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Which of the three JSON-RPC request-id forms an <see cref="AcpRequestId"/> carries.
    /// </summary>
    public enum AcpRequestIdKind
    {
        /// <summary>The JSON-RPC <c>null</c> request id.</summary>
        Null = 0,

        /// <summary>A numeric JSON-RPC request id.</summary>
        Number = 1,

        /// <summary>A string JSON-RPC request id.</summary>
        String = 2
    }

    /// <summary>
    /// A JSON-RPC request id: <c>null</c>, a number, or a string.
    /// </summary>
    /// <remarks>
    /// Modeled as a union rather than a single numeric type because the ACP <c>RequestId</c> schema
    /// is a union of all three forms, and JSON-RPC 2.0 requires a responder to echo back the very
    /// same value. Collapsing it to <see cref="long"/> would reject a peer's string ids, and
    /// re-encoding a number through <see cref="long"/> would rewrite unusual-but-legal numeric
    /// tokens — either way the echoed id would no longer correlate. The numeric form therefore
    /// keeps its raw token text, and <see cref="TryGetNumber"/> exposes the int64 view the schema
    /// documents for the ordinary case.
    /// </remarks>
    [JsonConverter(typeof(AcpRequestIdJsonConverter))]
    public readonly struct AcpRequestId : IEquatable<AcpRequestId>
    {
        /// <summary>
        /// For <see cref="AcpRequestIdKind.String"/> the string value; for
        /// <see cref="AcpRequestIdKind.Number"/> the raw JSON numeric token.
        /// </summary>
        private readonly string? _raw;

        private AcpRequestId(AcpRequestIdKind kind, string? raw)
        {
            Kind = kind;
            _raw = raw;
        }

        /// <summary>
        /// The JSON-RPC <c>null</c> request id, which is also the <c>default</c> value.
        /// </summary>
        public static AcpRequestId Null => default;

        /// <summary>
        /// Creates a numeric request id.
        /// </summary>
        /// <param name="value">The numeric id.</param>
        public static AcpRequestId FromNumber(long value)
            => new(AcpRequestIdKind.Number, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Creates a string request id.
        /// </summary>
        /// <param name="value">The string id.</param>
        public static AcpRequestId FromString(string value)
            => new(AcpRequestIdKind.String, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>
        /// Which of the three request-id forms this value carries.
        /// </summary>
        public AcpRequestIdKind Kind { get; }

        /// <summary>
        /// Gets the numeric value when this id is a number that fits in an <see cref="long"/>.
        /// </summary>
        /// <param name="value">The numeric value, or <c>0</c> when this id is not such a number.</param>
        /// <returns><c>true</c> when a numeric value was produced.</returns>
        public bool TryGetNumber(out long value)
        {
            if (Kind == AcpRequestIdKind.Number
                && long.TryParse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>
        /// Gets the string value when this id is a string.
        /// </summary>
        /// <param name="value">The string value, or <c>null</c> when this id is not a string.</param>
        /// <returns><c>true</c> when a string value was produced.</returns>
        public bool TryGetString(out string? value)
        {
            if (Kind == AcpRequestIdKind.String)
            {
                value = _raw ?? string.Empty;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Projects a JSON-RPC envelope id, whose CLR shape depends on how it was produced or parsed,
        /// onto this union.
        /// </summary>
        /// <param name="envelopeId">The envelope id value.</param>
        /// <param name="requestId">The projected request id.</param>
        /// <returns><c>false</c> when the value is none of the three legal forms (for example a boolean).</returns>
        /// <remarks>
        /// A locally issued id arrives here as a CLR integer, while a parsed one arrives as a
        /// <see cref="JsonElement"/>; both must land on the same wire form so an echoed id still
        /// correlates.
        /// </remarks>
        public static bool TryFromEnvelopeId(object? envelopeId, out AcpRequestId requestId)
        {
            switch (envelopeId)
            {
                case null:
                    requestId = Null;
                    return true;
                case string text:
                    requestId = FromString(text);
                    return true;
                case byte or sbyte or short or ushort or int or uint or long:
                    requestId = FromNumber(Convert.ToInt64(envelopeId, CultureInfo.InvariantCulture));
                    return true;
                case ulong unsigned:
                    // Outside int64 the raw token is still the faithful wire form.
                    requestId = new AcpRequestId(
                        AcpRequestIdKind.Number,
                        unsigned.ToString(CultureInfo.InvariantCulture));
                    return true;
                case JsonElement { ValueKind: JsonValueKind.Null }:
                    requestId = Null;
                    return true;
                case JsonElement { ValueKind: JsonValueKind.String } stringElement:
                    requestId = FromString(stringElement.GetString() ?? string.Empty);
                    return true;
                case JsonElement { ValueKind: JsonValueKind.Number } numberElement:
                    requestId = new AcpRequestId(AcpRequestIdKind.Number, numberElement.GetRawText());
                    return true;
                default:
                    requestId = Null;
                    return false;
            }
        }

        /// <inheritdoc />
        public bool Equals(AcpRequestId other)
            => Kind == other.Kind && string.Equals(_raw, other._raw, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is AcpRequestId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
            => HashCode.Combine(Kind, _raw is null ? 0 : StringComparer.Ordinal.GetHashCode(_raw));

        /// <summary>
        /// Determines whether two request ids carry the same form and value.
        /// </summary>
        public static bool operator ==(AcpRequestId left, AcpRequestId right) => left.Equals(right);

        /// <summary>
        /// Determines whether two request ids differ in form or value.
        /// </summary>
        public static bool operator !=(AcpRequestId left, AcpRequestId right) => !left.Equals(right);

        /// <summary>
        /// Renders the id for diagnostics. Not a wire form: a string id and the numeric token that
        /// spells the same characters render identically.
        /// </summary>
        public override string ToString() => Kind == AcpRequestIdKind.Null ? "null" : _raw ?? string.Empty;

        /// <summary>
        /// Writes this id as its JSON-RPC wire form.
        /// </summary>
        internal void Write(Utf8JsonWriter writer)
        {
            switch (Kind)
            {
                case AcpRequestIdKind.Number:
                    // The raw token, not a re-encoded number: writing through a CLR numeric type
                    // would normalise the text and break correlation with the original request.
                    writer.WriteRawValue(_raw ?? "0");
                    break;
                case AcpRequestIdKind.String:
                    writer.WriteStringValue(_raw ?? string.Empty);
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }

    internal sealed class AcpRequestIdJsonConverter : JsonConverter<AcpRequestId>
    {
        public override AcpRequestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return AcpRequestId.Null;
                case JsonTokenType.String:
                    return AcpRequestId.FromString(reader.GetString() ?? string.Empty);
                case JsonTokenType.Number:
                    using (var document = JsonDocument.ParseValue(ref reader))
                    {
                        return AcpRequestId.TryFromEnvelopeId(document.RootElement.Clone(), out var numeric)
                            ? numeric
                            : throw new JsonException("JSON-RPC request id must be null, a number, or a string.");
                    }

                default:
                    // The type contract is not relaxed: a boolean, object, or array id is illegal in
                    // JSON-RPC 2.0 and stays an error rather than being coerced.
                    throw new JsonException("JSON-RPC request id must be null, a number, or a string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, AcpRequestId value, JsonSerializerOptions options)
            => value.Write(writer);
    }

    /// <summary>
    /// Parameters of the <c>$/cancel_request</c> notification, sent by whichever side issued a
    /// request to ask that it be cancelled.
    /// </summary>
    /// <remarks>
    /// A protocol-level notification: the <c>$/</c> prefix marks it implementation dependent, and
    /// the receiver is free to ignore it. When the receiver does honour it, the specification still
    /// requires a terminal response to the original request — either a valid result or the
    /// <see cref="JsonRpc.JsonRpcErrorCode.Cancelled"/> error.
    /// </remarks>
    public sealed record CancelRequestParams : AcpProtocolObject
    {
        /// <summary>
        /// The JSON-RPC method name of the notification these parameters belong to.
        /// </summary>
        public const string Method = "$/cancel_request";

        /// <summary>
        /// The id of the request to cancel (required).
        /// </summary>
        [JsonPropertyName("requestId")]
        public AcpRequestId RequestId { get; init; }

        /// <summary>
        /// Creates empty cancellation parameters.
        /// </summary>
        public CancelRequestParams()
        {
        }

        /// <summary>
        /// Creates cancellation parameters for the given request id.
        /// </summary>
        /// <param name="requestId">The id of the request to cancel.</param>
        public CancelRequestParams(AcpRequestId requestId)
        {
            RequestId = requestId;
        }
    }
}
