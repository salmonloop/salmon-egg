using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// 会话状态的枚举。
    /// 表示会话在其生命周期中的当前状态。
    /// </summary>
    public enum SessionState
    {
        /// <summary>
        /// 会话处于活动状态，正在处理请求。
        /// </summary>
        [JsonPropertyName("active")]
        Active,

        /// <summary>
        /// 会话正在等待用户输入或外部事件。
        /// </summary>
        [JsonPropertyName("waiting")]
        Waiting,

        /// <summary>
        /// 会话已被用户取消。
        /// </summary>
        [JsonPropertyName("cancelled")]
        Cancelled,

        /// <summary>
        /// 会话已成功完成。
        /// </summary>
        [JsonPropertyName("completed")]
        Completed,

        /// <summary>
        /// 会话因错误而终止。
        /// </summary>
        [JsonPropertyName("error")]
        Error
    }

    /// <summary>
    /// 停止原因的枚举。
    /// 表示 Agent 生成响应时停止的原因。
    /// </summary>
    public enum StopReason
    {
        /// <summary>
        /// 正常结束回合，Agent 完成了回复。
        /// </summary>
        [JsonPropertyName("end_turn")]
        EndTurn,

        /// <summary>
        /// 达到最大令牌数限制。
        /// </summary>
        [JsonPropertyName("max_tokens")]
        MaxTokens,

        /// <summary>
        /// 遇到了停止序列。
        /// </summary>
        [JsonPropertyName("stop_sequence")]
        StopSequence,

        /// <summary>
        /// 用户取消了请求。
        /// </summary>
        [JsonPropertyName("user_cancelled")]
        UserCancelled,

        /// <summary>
        /// 发生错误导致停止。
        /// </summary>
        [JsonPropertyName("error")]
        Error
    }
}
