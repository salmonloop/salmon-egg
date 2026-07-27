using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// 图片内容块。
    /// 用于表示 Base64 编码的图片数据。
    /// </summary>
    public sealed record ImageContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "image"。
        /// 此属性被 [JsonIgnore] 忽略；wire 判别值由 ContentBlockJsonConverter 手写读写（保留未知类型 RawPayload 透传）。
        /// </summary>
        [JsonIgnore]
        public override string Type => "image";

        /// <summary>
        /// Base64 编码的图片数据。
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;

        /// <summary>
        /// Optional URI reference for the image source.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }

        /// <summary>
        /// 图片的 MIME 类型（例如 "image/png", "image/jpeg"）。
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; } = "image/png";

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
        /// <param name="uri">Optional URI reference for the image source</param>
        public ImageContentBlock(string data, string mimeType = "image/png", string? uri = null)
        {
            Data = data;
            MimeType = mimeType;
            Uri = uri;
        }
    }
}
