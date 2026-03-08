using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Content;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 资源内容块。
    /// 用于表示嵌入的实际资源数据（文本或二进制）。
    /// </summary>
    public class ResourceContentBlock : ContentBlock
    {
        /// <summary>
        /// 内容块类型标识符，固定为 "resource"。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public override string Type => "resource";

        /// <summary>
        /// 嵌入的资源对象。
        /// 包含资源的实际数据（uri, mimeType, text 或 blob）。
        /// </summary>
        [JsonPropertyName("resource")]
        public EmbeddedResource Resource { get; set; } = null!;

        /// <summary>
        /// 创建新的资源内容块实例。
        /// </summary>
        public ResourceContentBlock()
        {
        }

        /// <summary>
        /// 创建新的资源内容块实例。
        /// </summary>
        /// <param name="resource">嵌入的资源对象</param>
        public ResourceContentBlock(EmbeddedResource resource)
        {
            Resource = resource;
        }

        /// <summary>
        /// 创建新的文本资源内容块实例。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="text">文本内容</param>
        /// <param name="mimeType">MIME 类型</param>
        public static ResourceContentBlock CreateText(string uri, string text, string mimeType = "text/plain")
        {
            return new ResourceContentBlock(new EmbeddedResource(uri, text, mimeType));
        }

        /// <summary>
        /// 创建新的二进制资源内容块实例。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="blob">Base64 编码的二进制数据</param>
        /// <param name="mimeType">MIME 类型</param>
        public static ResourceContentBlock CreateBinary(string uri, string blob, string mimeType)
        {
            return new ResourceContentBlock(new EmbeddedResource(uri, blob, mimeType));
        }
    }
}
