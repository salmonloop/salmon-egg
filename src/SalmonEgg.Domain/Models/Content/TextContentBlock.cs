using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.JsonRpc;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 文本内容块。
    /// 用于表示纯文本内容。
    /// </summary>
    public class TextContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "text"。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public override string Type => "text";

        /// <summary>
        /// 文本内容。
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

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
