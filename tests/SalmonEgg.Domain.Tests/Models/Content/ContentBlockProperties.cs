using System;
using System.Collections.Generic;
using System.Text.Json;
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

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void TextContentBlock_PropertyRoundTrip_PreservesFidelity(
            string text,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = new TextContentBlock(text)
            {
                Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified)
            };

            var roundTripped = RoundTrip(block) as TextContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Text, Is.EqualTo(block.Text));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ImageContentBlock_PropertyRoundTrip_PreservesFidelity(
            string data,
            string mimeType,
            string uri,
            bool includeUri,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = new ImageContentBlock(data, mimeType, includeUri ? uri : null)
            {
                Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified)
            };

            var roundTripped = RoundTrip(block) as ImageContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Data, Is.EqualTo(block.Data));
            Assert.That(roundTripped.MimeType, Is.EqualTo(block.MimeType));
            Assert.That(roundTripped.Uri, Is.EqualTo(block.Uri));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void AudioContentBlock_PropertyRoundTrip_PreservesFidelity(
            string data,
            string mimeType,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = new AudioContentBlock(data, mimeType)
            {
                Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified)
            };

            var roundTripped = RoundTrip(block) as AudioContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Data, Is.EqualTo(block.Data));
            Assert.That(roundTripped.MimeType, Is.EqualTo(block.MimeType));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ResourceContentBlock_TextPropertyRoundTrip_PreservesFidelity(
            string uri,
            string text,
            string mimeType,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = ResourceContentBlock.CreateText(uri, text, mimeType);
            block.Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified);

            var roundTripped = RoundTrip(block) as ResourceContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Resource.Uri, Is.EqualTo(block.Resource.Uri));
            Assert.That(roundTripped.Resource.MimeType, Is.EqualTo(block.Resource.MimeType));
            Assert.That(roundTripped.Resource.Text, Is.EqualTo(block.Resource.Text));
            Assert.That(roundTripped.Resource.Blob, Is.EqualTo(block.Resource.Blob));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ResourceContentBlock_BlobPropertyRoundTrip_PreservesFidelity(
            string uri,
            string blob,
            string mimeType,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = ResourceContentBlock.CreateBinary(uri, blob, mimeType);
            block.Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified);

            var roundTripped = RoundTrip(block) as ResourceContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Resource.Uri, Is.EqualTo(block.Resource.Uri));
            Assert.That(roundTripped.Resource.MimeType, Is.EqualTo(block.Resource.MimeType));
            Assert.That(roundTripped.Resource.Text, Is.EqualTo(block.Resource.Text));
            Assert.That(roundTripped.Resource.Blob, Is.EqualTo(block.Resource.Blob));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void ResourceLinkContentBlock_PropertyRoundTrip_PreservesFidelity(
            string uri,
            string name,
            string mimeType,
            string title,
            string description,
            long size,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = new ResourceLinkContentBlock(uri, name, mimeType, title, description, size)
            {
                Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified)
            };

            var roundTripped = RoundTrip(block) as ResourceLinkContentBlock;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Uri, Is.EqualTo(block.Uri));
            Assert.That(roundTripped.Name, Is.EqualTo(block.Name));
            Assert.That(roundTripped.MimeType, Is.EqualTo(block.MimeType));
            Assert.That(roundTripped.Title, Is.EqualTo(block.Title));
            Assert.That(roundTripped.Description, Is.EqualTo(block.Description));
            Assert.That(roundTripped.Size, Is.EqualTo(block.Size));
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        /// <summary>
        /// 属性 6：文本内容块往返一致性
        /// </summary>
        [Test]
        public void TextContentBlock_RoundTrip_PreservesEquivalence()
        {
            // Arrange
            const string text = "Detailed debug information";
            var json = $$"""
            {
              "type": "text",
              "text": {{JsonSerializer.Serialize(text, _jsonOptions)}},
              "annotations": {
                "audience": ["user"],
                "priority": 0.8,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;

            // Act
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as TextContentBlock;
            var roundTripped = JsonSerializer.Serialize<ContentBlock>(deserialized!, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("text"));
            Assert.That(deserialized.Text, Is.EqualTo(text));
            Assert.That(doc.RootElement.TryGetProperty("annotations", out var annotations), Is.True);
            Assert.That(annotations.GetProperty("audience")[0].GetString(), Is.EqualTo("user"));
            Assert.That(annotations.GetProperty("priority").GetDecimal(), Is.EqualTo(0.8m));
            Assert.That(annotations.GetProperty("lastModified").GetString(), Is.EqualTo("2026-04-20T00:00:00Z"));
        }

        /// <summary>
        /// 属性 6：图片内容块往返一致性
        /// </summary>
        [Test]
        public void ImageContentBlock_RoundTrip_PreservesOptionalUriAndAnnotations()
        {
            // Arrange
            const string data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB";
            const string mimeType = "image/png";
            const string uri = "file:///tmp/example.png";
            var json = $$"""
            {
              "type": "image",
              "data": {{JsonSerializer.Serialize(data, _jsonOptions)}},
              "mimeType": {{JsonSerializer.Serialize(mimeType, _jsonOptions)}},
              "uri": {{JsonSerializer.Serialize(uri, _jsonOptions)}},
              "annotations": {
                "audience": ["assistant"],
                "priority": 0.4,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;

            // Act
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as ImageContentBlock;
            var roundTripped = JsonSerializer.Serialize<ContentBlock>(deserialized!, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("image"));
            Assert.That(deserialized.Data, Is.EqualTo(data));
            Assert.That(deserialized.MimeType, Is.EqualTo(mimeType));
            Assert.That(doc.RootElement.GetProperty("uri").GetString(), Is.EqualTo(uri));
            Assert.That(doc.RootElement.TryGetProperty("annotations", out var annotations), Is.True);
            Assert.That(annotations.GetProperty("audience")[0].GetString(), Is.EqualTo("assistant"));
            Assert.That(annotations.GetProperty("priority").GetDecimal(), Is.EqualTo(0.4m));
        }

        /// <summary>
        /// 属性 6：音频内容块往返一致性
        /// </summary>
        [Test]
        public void AudioContentBlock_RoundTrip_PreservesEquivalence()
        {
            // Arrange
            const string data = "UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAAB";
            const string mimeType = "audio/wav";
            var json = $$"""
            {
              "type": "audio",
              "data": {{JsonSerializer.Serialize(data, _jsonOptions)}},
              "mimeType": {{JsonSerializer.Serialize(mimeType, _jsonOptions)}},
              "annotations": {
                "audience": ["user", "assistant"],
                "priority": 0.6,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;

            // Act
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as AudioContentBlock;
            var roundTripped = JsonSerializer.Serialize<ContentBlock>(deserialized!, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Type, Is.EqualTo("audio"));
            Assert.That(deserialized.Data, Is.EqualTo(data));
            Assert.That(deserialized.MimeType, Is.EqualTo(mimeType));
            Assert.That(doc.RootElement.TryGetProperty("annotations", out var annotations), Is.True);
            Assert.That(annotations.GetProperty("audience").GetArrayLength(), Is.EqualTo(2));
        }

        /// <summary>
        /// 属性 6：资源内容块往返一致性
        /// </summary>
        [Test]
        public void ResourceContentBlock_RoundTrip_PreservesTextAndBlobForms()
        {
            // Arrange
            const string uri = "file:///home/user/script.py";
            const string text = "def hello():\n    print('Hello, world!')";
            const string blob = "AAECAwQ=";
            const string mimeType = "text/x-python";
            var textJson = $$"""
            {
              "type": "resource",
              "resource": {
                "uri": {{JsonSerializer.Serialize(uri, _jsonOptions)}},
                "mimeType": {{JsonSerializer.Serialize(mimeType, _jsonOptions)}},
                "text": {{JsonSerializer.Serialize(text, _jsonOptions)}}
              },
              "annotations": {
                "audience": ["assistant"],
                "priority": 0.5,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;
            var blobJson = $$"""
            {
              "type": "resource",
              "resource": {
                "uri": {{JsonSerializer.Serialize(uri, _jsonOptions)}},
                "mimeType": {{JsonSerializer.Serialize(mimeType, _jsonOptions)}},
                "blob": {{JsonSerializer.Serialize(blob, _jsonOptions)}}
              },
              "annotations": {
                "audience": ["user"],
                "priority": 0.9,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;

            // Act
            var textBlock = JsonSerializer.Deserialize<ContentBlock>(textJson, _jsonOptions) as ResourceContentBlock;
            var blobBlock = JsonSerializer.Deserialize<ContentBlock>(blobJson, _jsonOptions) as ResourceContentBlock;
            var textRoundTripped = JsonSerializer.Serialize<ContentBlock>(textBlock!, _jsonOptions);
            var blobRoundTripped = JsonSerializer.Serialize<ContentBlock>(blobBlock!, _jsonOptions);
            using var textDoc = JsonDocument.Parse(textRoundTripped);
            using var blobDoc = JsonDocument.Parse(blobRoundTripped);

            // Assert
            Assert.That(textBlock, Is.Not.Null);
            Assert.That(textBlock!.Type, Is.EqualTo("resource"));
            Assert.That(textBlock.Resource.Uri, Is.EqualTo(uri));
            Assert.That(textBlock.Resource.Text, Is.EqualTo(text));
            Assert.That(textBlock.Resource.Blob, Is.Null);
            Assert.That(textDoc.RootElement.GetProperty("annotations").GetProperty("priority").GetDecimal(), Is.EqualTo(0.5m));

            Assert.That(blobBlock, Is.Not.Null);
            Assert.That(blobBlock!.Type, Is.EqualTo("resource"));
            Assert.That(blobBlock.Resource.Uri, Is.EqualTo(uri));
            Assert.That(blobBlock.Resource.Blob, Is.EqualTo(blob));
            Assert.That(blobBlock.Resource.Text, Is.Null);
            Assert.That(blobDoc.RootElement.GetProperty("annotations").GetProperty("priority").GetDecimal(), Is.EqualTo(0.9m));
        }

        /// <summary>
        /// 属性 6：资源内容块二进制工厂必须写入 blob 字段。
        /// </summary>
        [Test]
        public void ResourceContentBlock_CreateBinary_UsesBlobField()
        {
            // Arrange
            var block = ResourceContentBlock.CreateBinary(
                uri: "file:///home/user/archive.bin",
                blob: "AAECAwQ=",
                mimeType: "application/octet-stream");

            // Act
            var json = JsonSerializer.Serialize<ContentBlock>(block, _jsonOptions);
            using var doc = JsonDocument.Parse(json);
            var resource = doc.RootElement.GetProperty("resource");

            // Assert
            Assert.That(resource.TryGetProperty("blob", out var blob), Is.True);
            Assert.That(blob.GetString(), Is.EqualTo("AAECAwQ="));
            Assert.That(resource.TryGetProperty("text", out var text), Is.True);
            Assert.That(text.ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        /// <summary>
        /// 属性 6：资源链接内容块往返一致性。
        /// </summary>
        [Test]
        public void ResourceLinkContentBlock_RoundTrip_PreservesAnnotations()
        {
            // Arrange
            var json = """
            {
              "type": "resource_link",
              "uri": "file:///home/user/document.pdf",
              "name": "document.pdf",
              "mimeType": "application/pdf",
              "title": "Reference",
              "description": "Project document",
              "size": 1024000,
              "annotations": {
                "audience": ["user"],
                "priority": 0.2,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
            """;

            // Act
            var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions) as ResourceLinkContentBlock;
            var roundTripped = JsonSerializer.Serialize<ContentBlock>(deserialized!, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Uri, Is.EqualTo("file:///home/user/document.pdf"));
            Assert.That(deserialized.Name, Is.EqualTo("document.pdf"));
            Assert.That(doc.RootElement.TryGetProperty("annotations", out var annotations), Is.True);
            Assert.That(annotations.GetProperty("priority").GetDecimal(), Is.EqualTo(0.2m));
        }

        /// <summary>
        /// 属性 8：未知内容类型应在启用回退时保留负载。
        /// </summary>
        [Test]
        public void ContentBlock_UnknownType_RoundTrip_PreservesExtensionPayload()
        {
            // Arrange
            var json = """
            {
              "type": "experimental_content",
              "payload": {
                "kind": "custom",
                "value": 42
              }
            }
            """;

            // Act
            var block = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions);
            var roundTripped = JsonSerializer.Serialize(block, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.That(block, Is.Not.Null);
            Assert.That(block, Is.InstanceOf<ContentBlock>());
            Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("experimental_content"));
            Assert.That(doc.RootElement.GetProperty("payload").GetProperty("kind").GetString(), Is.EqualTo("custom"));
            Assert.That(doc.RootElement.GetProperty("payload").GetProperty("value").GetInt32(), Is.EqualTo(42));
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

        private ContentBlock RoundTrip(ContentBlock block)
        {
            var json = JsonSerializer.Serialize<ContentBlock>(block, _jsonOptions);
            return JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions)!;
        }

        private static Annotations CreateAnnotations(
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            return new Annotations
            {
                Audience = new List<string> { audience1, audience2 },
                Priority = prioritySeed % 101 / 100m,
                LastModified = lastModified
            };
        }

        private static void AssertAnnotations(Annotations? actual, Annotations? expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(expected, Is.Not.Null);
            Assert.That(actual!.Audience, Is.EqualTo(expected!.Audience));
            Assert.That(actual.Priority, Is.EqualTo(expected.Priority));
            Assert.That(actual.LastModified, Is.EqualTo(expected.LastModified));
        }
    }
}
