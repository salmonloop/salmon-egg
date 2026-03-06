using System;
using System.Text.Json;

namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// Represents an Agent Client Protocol (ACP) message.
    /// This is the core data structure for all ACP communication.
    /// </summary>
    public class AcpMessage
    {
        /// <summary>
        /// Unique identifier for the message.
        /// Required for all message types.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Type of the message: "request", "response", "notification", or "initialize".
        /// Required for all message types.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Method name for request and notification messages.
        /// Required for "request" and "notification" types; null for others.
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Parameters for request messages.
        /// Optional, used with "request" type.
        /// </summary>
        public JsonElement? Params { get; set; }

        /// <summary>
        /// Result data for response messages.
        /// Used with "response" type when the request succeeded.
        /// </summary>
        public JsonElement? Result { get; set; }

        /// <summary>
        /// Error information for response messages.
        /// Used with "response" type when the request failed; null on success.
        /// </summary>
        public AcpError? Error { get; set; }

        /// <summary>
        /// Protocol version string (e.g., "1.0", "1.1", "2.0").
        /// Optional; may be null for messages that do not carry version info.
        /// </summary>
        public string? ProtocolVersion { get; set; }

        /// <summary>
        /// Timestamp when the message was created.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
