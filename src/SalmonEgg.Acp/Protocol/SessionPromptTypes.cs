using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Session/Prompt 方法的请求参数。
    /// 用于向会话发送提示并请求 Agent 响应。
    /// </summary>
    public sealed record SessionPromptParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// 要发送的提示内容块列表（必填，根据协议要求为数组）。
        /// </summary>
        [JsonPropertyName("prompt")]
        public List<ContentBlock> Prompt { get; init; } = new List<ContentBlock>();

        /// <summary>
        /// 协议扩展字段（_meta）。
        /// </summary>
        /// <summary>
        /// 创建新的 SessionPromptParams 实例。
        /// </summary>
        public SessionPromptParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionPromptParams 实例。
        /// </summary>

        /// <param name="sessionId">会话 ID</param>
        /// <param name="prompt">提示内容块数组</param>
        public SessionPromptParams(string sessionId, List<ContentBlock> prompt)
        {
            SessionId = sessionId;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// Session/Prompt 方法的响应。
    /// Agent 对提示请求的响应，仅包含停止原因。
    /// </summary>
    public sealed record SessionPromptResponse : AcpProtocolObject
    {
        /// <summary>
        /// 停止原因。
        /// 指示 Agent 为什么停止生成响应。
        /// </summary>
        [JsonPropertyName("stopReason")]
        public StopReason StopReason { get; init; } = StopReason.EndTurn;

        /// <summary>
        /// 协议扩展字段（_meta）。
        /// </summary>
        /// <summary>
        /// 创建新的 SessionPromptResponse 实例。
        /// </summary>
        public SessionPromptResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionPromptResponse 实例。
        /// </summary>
        /// <param name="stopReason">停止原因</param>
        public SessionPromptResponse(StopReason stopReason)
        {
            StopReason = stopReason;
        }
    }
}
