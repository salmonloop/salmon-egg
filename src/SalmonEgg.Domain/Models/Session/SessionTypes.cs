using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// 会话状态的枚举。
    /// 表示会话在其生命周期中的当前状态。
    /// </summary>
    [JsonConverter(typeof(SessionStateJsonConverter))]
    public enum SessionState
    {
        /// <summary>
        /// 会话处于活动状态，正在处理请求。
        /// </summary>
        Active,

        /// <summary>
        /// 会话正在等待用户输入或外部事件。
        /// </summary>
        Waiting,

        /// <summary>
        /// 会话已被用户取消。
        /// </summary>
        Cancelled,

        /// <summary>
        /// 会话已成功完成。
        /// </summary>
        Completed,

        /// <summary>
        /// 会话因错误而终止。
        /// </summary>
        Error
    }

    public sealed class SessionStateJsonConverter : JsonConverter<SessionState>
    {
        public override SessionState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Session state must be a string.");
            }

            return reader.GetString() switch
            {
                "active" => SessionState.Active,
                "waiting" => SessionState.Waiting,
                "cancelled" => SessionState.Cancelled,
                "completed" => SessionState.Completed,
                "error" => SessionState.Error,
                var value => throw new JsonException($"Unsupported session state '{value}'.")
            };
        }

        public override void Write(Utf8JsonWriter writer, SessionState value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                SessionState.Active => "active",
                SessionState.Waiting => "waiting",
                SessionState.Cancelled => "cancelled",
                SessionState.Completed => "completed",
                SessionState.Error => "error",
                _ => throw new JsonException($"Unsupported session state '{value}'.")
            });
        }
    }

}
