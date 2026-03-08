using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 资源链接内容块。
    /// 用于表示对外部资源的引用（URI 链接）。
    /// </summary>
    public class ResourceLinkContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "resource_link"。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public override string Type => "resource_link";

        /// <summary>
        /// 资源的 URI 标识符。
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// 资源的名称（可选）。
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// 资源的 MIME 类型（可选）。
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        /// <summary>
        /// 资源的标题（可选）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// 资源的描述（可选）。
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 资源的大小（字节，可选）。
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; set; }

        /// <summary>
        /// 创建新的资源链接内容块实例。
        /// </summary>
        public ResourceLinkContentBlock()
        {
        }

        /// <summary>
        /// 创建新的资源链接内容块实例。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="name">资源名称</param>
        /// <param name="mimeType">MIME 类型</param>
        /// <param name="title">标题</param>
        /// <param name="description">描述</param>
        /// <param name="size">大小（字节）</param>
        public ResourceLinkContentBlock(
            string uri,
            string? name = null,
            string? mimeType = null,
            string? title = null,
            string? description = null,
            long? size = null)
        {
            Uri = uri;
            Name = name;
            MimeType = mimeType;
            Title = title;
            Description = description;
            Size = size;
        }
    }
}
