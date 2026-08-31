using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the <c>session/prompt</c> method.
    /// Sends a prompt to a session and requests a response from the Agent.
    /// </summary>
    public sealed record SessionPromptParams : AcpProtocolObject
    {
        /// <summary>
        /// The session id. Required.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The prompt content blocks to send. Required, and serialized as an array per the protocol.
        /// </summary>
        [JsonPropertyName("prompt")]
        public List<ContentBlock> Prompt { get; init; } = new List<ContentBlock>();

        /// <summary>
        /// Protocol extension field (<c>_meta</c>).
        /// </summary>
        /// <summary>
        /// Creates a new <see cref="SessionPromptParams"/> instance.
        /// </summary>
        public SessionPromptParams()
        {
        }

        /// <summary>
        /// Creates a new <see cref="SessionPromptParams"/> instance.
        /// </summary>

        /// <param name="sessionId">The session id.</param>
        /// <param name="prompt">The prompt content blocks.</param>
        public SessionPromptParams(string sessionId, List<ContentBlock> prompt)
        {
            SessionId = sessionId;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// Response for the <c>session/prompt</c> method.
    /// The Agent's reply to a prompt request, carrying only the stop reason.
    /// </summary>
    public sealed record SessionPromptResponse : AcpProtocolObject
    {
        /// <summary>
        /// The stop reason, indicating why the Agent stopped generating a response.
        /// </summary>
        [JsonPropertyName("stopReason")]
        public StopReason StopReason { get; init; } = StopReason.EndTurn;

        /// <summary>
        /// Protocol extension field (<c>_meta</c>).
        /// </summary>
        /// <summary>
        /// Creates a new <see cref="SessionPromptResponse"/> instance.
        /// </summary>
        public SessionPromptResponse()
        {
        }

        /// <summary>
        /// Creates a new <see cref="SessionPromptResponse"/> instance.
        /// </summary>
        /// <param name="stopReason">The stop reason.</param>
        public SessionPromptResponse(StopReason stopReason)
        {
            StopReason = stopReason;
        }
    }
}
