using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 音频内容块。
    /// 用于表示 Base64 编码的音频数据。
    /// </summary>
    public class AudioContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "audio"。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public override string Type => "audio";

        /// <summary>
        /// Base64 编码的音频数据。
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;

        /// <summary>
        /// 音频的 MIME 类型（例如 "audio/wav", "audio/mp3"）。
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "audio/wav";

        /// <summary>
        /// 创建新的音频内容块实例。
        /// </summary>
        public AudioContentBlock()
        {
        }

        /// <summary>
        /// 创建新的音频内容块实例。
        /// </summary>
        /// <param name="data">Base64 编码的音频数据</param>
        /// <param name="mimeType">音频的 MIME 类型</param>
        public AudioContentBlock(string data, string mimeType = "audio/wav")
        {
            Data = data;
            MimeType = mimeType;
        }
    }
}
