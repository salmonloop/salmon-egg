using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Session/Prompt 方法的请求参数。
    /// 用于向会话发送提示并请求 Agent 响应。
    /// </summary>
    public class SessionPromptParams
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        
        /// <summary>
        /// 要发送的提示内容块列表（必填，对端要求为数组）。
        /// </summary>
        [JsonPropertyName("prompt")]
        public object Prompt { get; set; } = new object[0];


        /// <summary>
        /// 最大生成的令牌数（可选）。
        /// </summary>
        [JsonPropertyName("maxTokens")]
        public int? MaxTokens { get; set; }

        /// <summary>
        /// 停止序列列表（可选）。
        /// 当生成遇到这些序列时会停止。
        /// </summary>
        [JsonPropertyName("stopSequences")]
        public List<string>? StopSequences { get; set; }

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
        /// <param name="maxTokens">最大令牌数</param>
        /// <param name="stopSequences">停止序列</param>
        public SessionPromptParams(string sessionId, object prompt, int? maxTokens = null, List<string>? stopSequences = null)
        {
            SessionId = sessionId;
            Prompt = prompt;

            MaxTokens = maxTokens;
            StopSequences = stopSequences;
        }
    }

    /// <summary>
    /// Session/Prompt 方法的响应。
    /// Agent 对提示请求的响应，仅包含停止原因。
    /// </summary>
    public class SessionPromptResponse
    {
        /// <summary>
        /// 停止原因。
        /// 指示 Agent 为什么停止生成响应。
        /// </summary>
        [JsonPropertyName("stopReason")]
        public StopReason StopReason { get; set; }

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
