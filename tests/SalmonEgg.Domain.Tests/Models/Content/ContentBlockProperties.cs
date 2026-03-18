using System;
using System.Collections.Generic;
using System.Text.Json;
using FsCheck.NUnit;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Content;

namespace SalmonEgg.Domain.Tests.Models.Content
{
    /// <summary>
    /// 内容块属性测试。
    /// 使用 FsCheck 验证内容块的往返一致性和多态性。
    /// </summary>
    [TestFixture]
    public class ContentBlockProperties
    {
       private readonly JsonSerializerOptions _jsonOptions;

       public ContentBlockProperties()
       {
           // 配置序列化选项：使用小写命名策略，以匹配 "type" 字段
           _jsonOptions = new JsonSerializerOptions
           {
               PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
               PropertyNameCaseInsensitive = true,
               WriteIndented = false
           };

           // 添加 JsonPolymorphic 转换器支持
           _jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
       }

        /// <summary>
        /// 属性 6：文本内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void TextContentBlock_RoundTrip_PreservesEquivalence(string text)
        {
            // Arrange
            var block = new TextContentBlock(text);
            ContentBlock baseRef = block;  // 使用基类引用以触发 JsonPolymorphic 类型标识符写入

            // Act
            var json = JsonSerializer.Serialize(baseRef, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as TextContentBlock;

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("text"));
            Assert.That(deserialized.Text, Is.EqualTo(block.Text));
        }

        /// <summary>
        /// 属性 6：图片内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ImageContentBlock_RoundTrip_PreservesEquivalence(string data, string mimeType)
        {
            // Arrange
            var block = new ImageContentBlock(data, mimeType);
            ContentBlock baseRef = block;  // 使用基类引用以触发 JsonPolymorphic 类型标识符写入

            // Act
            var json = JsonSerializer.Serialize(baseRef, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as ImageContentBlock;

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("image"));
            Assert.That(deserialized.Data, Is.EqualTo(block.Data));
            Assert.That(deserialized.MimeType, Is.EqualTo(block.MimeType));
        }

        /// <summary>
        /// 属性 6：音频内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void AudioContentBlock_RoundTrip_PreservesEquivalence(string data, string mimeType)
        {
            // Arrange
            var block = new AudioContentBlock(data, mimeType);
            ContentBlock baseRef = block;  // 使用基类引用以触发 JsonPolymorphic 类型标识符写入

            // Act
            var json = JsonSerializer.Serialize(baseRef, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as AudioContentBlock;

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("audio"));
            Assert.That(deserialized.Data, Is.EqualTo(block.Data));
            Assert.That(deserialized.MimeType, Is.EqualTo(block.MimeType));
        }

        /// <summary>
        /// 属性 6：资源内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ResourceContentBlock_RoundTrip_PreservesEquivalence(string uri, string text, string mimeType)
        {
            // Arrange
            var resource = new EmbeddedResource(uri, text, mimeType);
            var block = new ResourceContentBlock(resource);
            ContentBlock baseRef = block;  // 使用基类引用以触发 JsonPolymorphic 类型标识符写入

            // Act
            var json = JsonSerializer.Serialize(baseRef, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as ResourceContentBlock;

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("resource"));
            Assert.That(deserialized.Resource.Uri, Is.EqualTo(block.Resource.Uri));
            Assert.That(deserialized.Resource.Text, Is.EqualTo(block.Resource.Text));
            Assert.That(deserialized.Resource.MimeType, Is.EqualTo(block.Resource.MimeType));
        }

        /// <summary>
        /// 属性 7：内容块多态序列化和反序列化
        /// </summary>
        [Test]
        public void ContentBlock_Array_Polymorphic_Serialization()
        {
            // Arrange
            var blocks = new List<ContentBlock>
            {
                new TextContentBlock("Hello"),
                new ImageContentBlock("base64data", "image/png"),
                new AudioContentBlock("base64audio", "audio/wav")
            };

            // Act
            var json = JsonSerializer.Serialize(blocks, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ContentBlock[]>(json, _jsonOptions);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Length, Is.EqualTo(blocks.Count));
            Assert.That(deserialized[0], Is.InstanceOf<TextContentBlock>());
            Assert.That(deserialized[1], Is.InstanceOf<ImageContentBlock>());
            Assert.That(deserialized[2], Is.InstanceOf<AudioContentBlock>());
        }

        /// <summary>
        /// 属性 7：ContentBlock 数组序列化时 type 字段存在
        /// </summary>
        [Test]
        public void ContentBlock_Array_TypeDiscriminator_Present()
        {
            // Arrange
            var blocks = new List<ContentBlock>
            {
                new TextContentBlock("Test"),
                new ImageContentBlock("data", "image/jpeg")
            };

            // Act
            var json = JsonSerializer.Serialize(blocks, _jsonOptions);
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThan(0));

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                Assert.That(element.TryGetProperty("type", out _), Is.True, "每个 ContentBlock 必须包含 type 字段");
            }
        }
    }
}
