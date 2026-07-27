using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// 文本内容块。
    /// 用于表示纯文本内容。
    /// </summary>
    public sealed record TextContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "text"。
        /// 此属性被 [JsonIgnore] 忽略；wire 判别值由 ContentBlockJsonConverter 手写读写（保留未知类型 RawPayload 透传）。
        /// </summary>
        [JsonIgnore]
        public override string Type => "text";

        /// <summary>
        /// 文本内容。
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        /// <summary>
        /// 创建新的文本内容块实例。
        /// </summary>
        public TextContentBlock()
        {
        }

        /// <summary>
        /// 创建新的文本内容块实例。
        /// </summary>
        /// <param name="text">文本内容</param>
        public TextContentBlock(string text)
        {
            Text = text;
        }
    }
}
