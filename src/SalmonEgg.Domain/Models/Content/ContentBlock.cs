using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
{
    /// <summary>
    /// 内容块的抽象基类。
    /// 用于表示会话中的各种类型的内容（文本、图片、音频、资源等）。
    /// 使用 JsonPolymorphic 特性支持多态序列化。
    /// 注意：Type 属性在此处定义为抽象属性，派生类必须实现它。
    /// 序列化时的 "type" 字段由 [JsonPolymorphic] 自动处理，无需在属性上添加 [JsonPropertyName]。
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(TextContentBlock), "text")]
    [JsonDerivedType(typeof(ImageContentBlock), "image")]
    [JsonDerivedType(typeof(AudioContentBlock), "audio")]
    [JsonDerivedType(typeof(ResourceLinkContentBlock), "resource_link")]
    [JsonDerivedType(typeof(ResourceContentBlock), "resource")]
    public abstract class ContentBlock
    {
        /// <summary>
        /// 内容块的类型标识符。
        /// 用于多态序列化和反序列化。
        /// 由 [JsonPolymorphic] 自动序列化为 JSON 中的 "type" 字段。
        /// 此属性被 [JsonIgnore] 忽略，因为类型信息已由 JsonPolymorphic 自动处理。
        /// </summary>
        [JsonIgnore]
        public abstract string Type { get; }
    }
}
