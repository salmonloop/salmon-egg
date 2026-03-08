using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 图片内容块。
    /// 用于表示 Base64 编码的图片数据。
    /// </summary>
    public class ImageContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "image"。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public override string Type => "image";

        /// <summary>
        /// Base64 编码的图片数据。
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;

        /// <summary>
        /// 图片的 MIME 类型（例如 "image/png", "image/jpeg"）。
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "image/png";

        /// <summary>
        /// 创建新的图片内容块实例。
        /// </summary>
        public ImageContentBlock()
        {
        }

        /// <summary>
        /// 创建新的图片内容块实例。
        /// </summary>
        /// <param name="data">Base64 编码的图片数据</param>
        /// <param name="mimeType">图片的 MIME 类型</param>
        public ImageContentBlock(string data, string mimeType = "image/png")
        {
            Data = data;
            MimeType = mimeType;
        }
    }
}
