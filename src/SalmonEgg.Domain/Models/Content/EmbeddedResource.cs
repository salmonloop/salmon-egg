using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 嵌入的资源对象。
    /// 用于 ResourceContentBlock 中包含的实际资源数据。
    /// </summary>
    public class EmbeddedResource
    {
        /// <summary>
        /// 资源的 URI 标识符。
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// 资源的 MIME 类型（例如 "text/plain", "application/json"）。
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "text/plain";

        /// <summary>
        /// 资源的文本内容（如果资源是文本类型）。
        /// 与 Blob 互斥。
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// 资源的二进制数据（Base64 编码，如果资源是二进制类型）。
        /// 与 Text 互斥。
        /// </summary>
        [JsonPropertyName("blob")]
        public string? Blob { get; set; }

        /// <summary>
        /// 创建新的 EmbeddedResource 实例。
        /// </summary>
        public EmbeddedResource()
        {
        }

        /// <summary>
        /// 创建新的文本资源实例。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="text">文本内容</param>
        /// <param name="mimeType">MIME 类型</param>
        public EmbeddedResource(string uri, string text, string mimeType = "text/plain")
        {
            Uri = uri;
            Text = text;
            MimeType = mimeType;
        }

        /// <summary>
        /// 创建新的二进制资源实例。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="blob">Base64 编码的二进制数据</param>
        /// <param name="mimeType">MIME 类型</param>
        public EmbeddedResource(string uri, string blob, string mimeType, bool isBinary)
        {
            Uri = uri;
            Blob = blob;
            MimeType = mimeType;
        }

        /// <summary>
        /// 静态辅助方法：创建文本资源。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="text">文本内容</param>
        /// <param name="mimeType">MIME 类型</param>
        /// <returns>EmbeddedResource 实例</returns>
        public static EmbeddedResource CreateText(string uri, string text, string mimeType = "text/plain")
        {
            return new EmbeddedResource(uri, text, mimeType);
        }

        /// <summary>
        /// 静态辅助方法：创建二进制资源。
        /// </summary>
        /// <param name="uri">资源 URI</param>
        /// <param name="blob">Base64 编码的二进制数据</param>
        /// <param name="mimeType">MIME 类型</param>
        /// <returns>EmbeddedResource 实例</returns>
        public static EmbeddedResource CreateBinary(string uri, string blob, string mimeType)
        {
            return new EmbeddedResource(uri, blob, mimeType, true);
        }

        /// <summary>
        /// 判断资源是否为文本类型。
        /// </summary>
        public bool IsText => !string.IsNullOrEmpty(Text);

        /// <summary>
        /// 判断资源是否为二进制类型。
        /// </summary>
        public bool IsBinary => !string.IsNullOrEmpty(Blob);
    }
}
