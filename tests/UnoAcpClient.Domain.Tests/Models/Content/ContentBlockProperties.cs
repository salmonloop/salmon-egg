using System;
using System.Collections.Generic;
using System.Text.Json;
using FsCheck;
using FsCheck.NUnit;
using NUnit.Framework;
using UnoAcpClient.Domain.Models.Content;

namespace UnoAcpClient.Domain.Tests.Models.Content
{
    /// <summary>
    /// 内容块属性测试。
    /// 使用 FsCheck 验证内容块的往返一致性和多态性。
    /// </summary>
    [TestFixture]
    public class ContentBlockProperties
    {
        /// <summary>
        /// 属性 6：文本内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property]
        public void TextContentBlock_RoundTrip_PreservesEquivalence(string text)
        {
            // Arrange
            var block = new TextContentBlock(text);

            // Act
            var json = JsonSerializer.Serialize(block);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json) as TextContentBlock;

            // Assert
            NUnit.Framework.NUnit.Framework.Assert.IsNotNull(deserialized);
            NUnit.Framework.Assert.AreEqual("text", deserialized.Type);
            NUnit.Framework.Assert.AreEqual(block.Text, deserialized.Text);
        }

        /// <summary>
        /// 属性 6：图片内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property]
        public void ImageContentBlock_RoundTrip_PreservesEquivalence(string data, string mimeType)
        {
            // Arrange
            var block = new ImageContentBlock(data, mimeType);

            // Act
            var json = JsonSerializer.Serialize(block);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json) as ImageContentBlock;

            // Assert
            NUnit.Framework.NUnit.Framework.Assert.IsNotNull(deserialized);
            NUnit.Framework.Assert.AreEqual("image", deserialized.Type);
            NUnit.Framework.Assert.AreEqual(block.Data, deserialized.Data);
            NUnit.Framework.Assert.AreEqual(block.MimeType, deserialized.MimeType);
        }

        /// <summary>
        /// 属性 6：音频内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property]
        public void AudioContentBlock_RoundTrip_PreservesEquivalence(string data, string mimeType)
        {
            // Arrange
            var block = new AudioContentBlock(data, mimeType);

            // Act
            var json = JsonSerializer.Serialize(block);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json) as AudioContentBlock;

            // Assert
            NUnit.Framework.NUnit.Framework.Assert.IsNotNull(deserialized);
            NUnit.Framework.Assert.AreEqual("audio", deserialized.Type);
            NUnit.Framework.Assert.AreEqual(block.Data, deserialized.Data);
            NUnit.Framework.Assert.AreEqual(block.MimeType, deserialized.MimeType);
        }

        /// <summary>
        /// 属性 6：资源内容块往返一致性
        /// </summary>
        [FsCheck.NUnit.Property]
        public void ResourceContentBlock_RoundTrip_PreservesEquivalence(string uri, string text, string mimeType)
        {
            // Arrange
            var resource = new EmbeddedResource(uri, text, mimeType);
            var block = new ResourceContentBlock(resource);

            // Act
            var json = JsonSerializer.Serialize(block);
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json) as ResourceContentBlock;

            // Assert
            NUnit.Framework.NUnit.Framework.Assert.IsNotNull(deserialized);
            NUnit.Framework.Assert.AreEqual("resource", deserialized.Type);
            NUnit.Framework.Assert.AreEqual(block.Resource.Uri, deserialized.Resource.Uri);
            NUnit.Framework.Assert.AreEqual(block.Resource.Text, deserialized.Resource.Text);
            NUnit.Framework.Assert.AreEqual(block.Resource.MimeType, deserialized.Resource.MimeType);
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
            var json = JsonSerializer.Serialize(blocks);
            var deserialized = JsonSerializer.Deserialize<ContentBlock[]>(json);

            // Assert
            NUnit.Framework.NUnit.Framework.Assert.IsNotNull(deserialized);
            NUnit.Framework.Assert.AreEqual(blocks.Count, deserialized.Length);
            NUnit.Framework.Assert.IsInstanceOf<TextContentBlock>(deserialized[0]);
            NUnit.Framework.Assert.IsInstanceOf<ImageContentBlock>(deserialized[1]);
            NUnit.Framework.Assert.IsInstanceOf<AudioContentBlock>(deserialized[2]);
        }
    }
}
