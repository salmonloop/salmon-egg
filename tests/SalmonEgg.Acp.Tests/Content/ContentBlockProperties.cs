using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Acp.Tests.Content
{
    /// <summary>
    /// 内容块属性测试。
    /// 使用 FsCheck 验证内容块的往返一致性和多态性。
    /// </summary>
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

        [Fact]
        public void TextContentBlock_PropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(TextContentBlock_PropertyRoundTrip_PreservesFidelityProperty));
        }

        private void TextContentBlock_PropertyRoundTrip_PreservesFidelityProperty(
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

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Text, roundTripped!.Text);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [Fact]
        public void ImageContentBlock_PropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(ImageContentBlock_PropertyRoundTrip_PreservesFidelityProperty));
        }

        private void ImageContentBlock_PropertyRoundTrip_PreservesFidelityProperty(
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

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Data, roundTripped!.Data);
            Assert.Equal(block.MimeType, roundTripped.MimeType);
            Assert.Equal(block.Uri, roundTripped.Uri);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [Fact]
        public void AudioContentBlock_PropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(AudioContentBlock_PropertyRoundTrip_PreservesFidelityProperty));
        }

        private void AudioContentBlock_PropertyRoundTrip_PreservesFidelityProperty(
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

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Data, roundTripped!.Data);
            Assert.Equal(block.MimeType, roundTripped.MimeType);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [Fact]
        public void ResourceContentBlock_TextPropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(ResourceContentBlock_TextPropertyRoundTrip_PreservesFidelityProperty));
        }

        private void ResourceContentBlock_TextPropertyRoundTrip_PreservesFidelityProperty(
            string uri,
            string text,
            string mimeType,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = ResourceContentBlock.CreateText(uri, text, mimeType);
            block = block with { Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified) };

            var roundTripped = RoundTrip(block) as ResourceContentBlock;

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Resource.Uri, roundTripped!.Resource.Uri);
            Assert.Equal(block.Resource.MimeType, roundTripped.Resource.MimeType);
            Assert.Equal(block.Resource.Text, roundTripped.Resource.Text);
            Assert.Equal(block.Resource.Blob, roundTripped.Resource.Blob);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [Fact]
        public void ResourceContentBlock_BlobPropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(ResourceContentBlock_BlobPropertyRoundTrip_PreservesFidelityProperty));
        }

        private void ResourceContentBlock_BlobPropertyRoundTrip_PreservesFidelityProperty(
            string uri,
            string blob,
            string mimeType,
            string audience1,
            string audience2,
            byte prioritySeed,
            string lastModified)
        {
            var block = ResourceContentBlock.CreateBinary(uri, blob, mimeType);
            block = block with { Annotations = CreateAnnotations(audience1, audience2, prioritySeed, lastModified) };

            var roundTripped = RoundTrip(block) as ResourceContentBlock;

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Resource.Uri, roundTripped!.Resource.Uri);
            Assert.Equal(block.Resource.MimeType, roundTripped.Resource.MimeType);
            Assert.Equal(block.Resource.Text, roundTripped.Resource.Text);
            Assert.Equal(block.Resource.Blob, roundTripped.Resource.Blob);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        [Fact]
        public void ResourceLinkContentBlock_PropertyRoundTrip_PreservesFidelity()
        {
            FsCheckPropertyRunner.Run(this, nameof(ResourceLinkContentBlock_PropertyRoundTrip_PreservesFidelityProperty));
        }

        private void ResourceLinkContentBlock_PropertyRoundTrip_PreservesFidelityProperty(
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

            Assert.NotNull(roundTripped);
            Assert.Equal(block.Uri, roundTripped!.Uri);
            Assert.Equal(block.Name, roundTripped.Name);
            Assert.Equal(block.MimeType, roundTripped.MimeType);
            Assert.Equal(block.Title, roundTripped.Title);
            Assert.Equal(block.Description, roundTripped.Description);
            Assert.Equal(block.Size, roundTripped.Size);
            AssertAnnotations(roundTripped.Annotations, block.Annotations);
        }

        /// <summary>
        /// 属性 6：文本内容块往返一致性
        /// </summary>
        [Fact]
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
            Assert.NotNull(deserialized);
            Assert.Equal("text", deserialized!.Type);
            Assert.Equal(text, deserialized.Text);
            Assert.True(doc.RootElement.TryGetProperty("annotations", out var annotations));
            Assert.Equal("user", annotations.GetProperty("audience")[0].GetString());
            Assert.Equal(0.8m, annotations.GetProperty("priority").GetDecimal());
            Assert.Equal("2026-04-20T00:00:00Z", annotations.GetProperty("lastModified").GetString());
        }

        /// <summary>
        /// 属性 6：图片内容块往返一致性
        /// </summary>
        [Fact]
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
            Assert.NotNull(deserialized);
            Assert.Equal("image", deserialized!.Type);
            Assert.Equal(data, deserialized.Data);
            Assert.Equal(mimeType, deserialized.MimeType);
            Assert.Equal(uri, doc.RootElement.GetProperty("uri").GetString());
            Assert.True(doc.RootElement.TryGetProperty("annotations", out var annotations));
            Assert.Equal("assistant", annotations.GetProperty("audience")[0].GetString());
            Assert.Equal(0.4m, annotations.GetProperty("priority").GetDecimal());
        }

        /// <summary>
        /// 属性 6：音频内容块往返一致性
        /// </summary>
        [Fact]
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
            Assert.NotNull(deserialized);
            Assert.Equal("audio", deserialized!.Type);
            Assert.Equal(data, deserialized.Data);
            Assert.Equal(mimeType, deserialized.MimeType);
            Assert.True(doc.RootElement.TryGetProperty("annotations", out var annotations));
            Assert.Equal(2, annotations.GetProperty("audience").GetArrayLength());
        }

        /// <summary>
        /// 属性 6：资源内容块往返一致性
        /// </summary>
        [Fact]
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
            Assert.NotNull(textBlock);
            Assert.Equal("resource", textBlock!.Type);
            Assert.Equal(uri, textBlock.Resource.Uri);
            Assert.Equal(text, textBlock.Resource.Text);
            Assert.Null(textBlock.Resource.Blob);
            Assert.Equal(0.5m, textDoc.RootElement.GetProperty("annotations").GetProperty("priority").GetDecimal());

            Assert.NotNull(blobBlock);
            Assert.Equal("resource", blobBlock!.Type);
            Assert.Equal(uri, blobBlock.Resource.Uri);
            Assert.Equal(blob, blobBlock.Resource.Blob);
            Assert.Null(blobBlock.Resource.Text);
            Assert.Equal(0.9m, blobDoc.RootElement.GetProperty("annotations").GetProperty("priority").GetDecimal());
        }

        /// <summary>
        /// 属性 6：资源内容块二进制工厂必须写入 blob 字段。
        /// </summary>
        [Fact]
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
            Assert.True(resource.TryGetProperty("blob", out var blob));
            Assert.Equal("AAECAwQ=", blob.GetString());
            Assert.True(resource.TryGetProperty("text", out var text));
            Assert.Equal(JsonValueKind.Null, text.ValueKind);
        }

        /// <summary>
        /// 属性 6：资源链接内容块往返一致性。
        /// </summary>
        [Fact]
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
            Assert.NotNull(deserialized);
            Assert.Equal("file:///home/user/document.pdf", deserialized!.Uri);
            Assert.Equal("document.pdf", deserialized.Name);
            Assert.True(doc.RootElement.TryGetProperty("annotations", out var annotations));
            Assert.Equal(0.2d, annotations.GetProperty("priority").GetDouble());
        }

        /// <summary>
        /// 属性 8：未知内容类型走 passthrough，无损保留整个原始 payload。
        /// spec 要求 receiver 对不认识的 content 类型保留 raw payload，由 Agent 而非 client
        /// 决定接受或拒绝；丢弃未知字段属反向收紧，为 AGENTS.md §11 明令禁止
        /// （对照 McpServerJsonConverter 的 RawPayload 范式）。
        /// </summary>
        [Fact]
        public void ContentBlock_UnknownType_RoundTrip_PreservesEntireRawPayload()
        {
            // Arrange
            var json = """
            {
              "type": "experimental_content",
              "payload": {
                "kind": "custom",
                "value": 42
              },
              "annotations": {
                "audience": ["assistant"],
                "priority": 0.75
              },
              "_meta": {
                "vendor": "example"
              }
            }
            """;

            // Act
            var block = JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions);
            var roundTripped = JsonSerializer.Serialize(block, _jsonOptions);
            using var doc = JsonDocument.Parse(roundTripped);

            // Assert
            Assert.NotNull(block);
            Assert.IsAssignableFrom<ContentBlock>(block);
            Assert.Equal("experimental_content", doc.RootElement.GetProperty("type").GetString());
            // 未知的非协议字段必须原样透传，不得丢弃。
            Assert.True(doc.RootElement.TryGetProperty("payload", out var payload));
            Assert.Equal("custom", payload.GetProperty("kind").GetString());
            Assert.Equal(42, payload.GetProperty("value").GetInt32());
            Assert.Equal(
                "assistant",
                doc.RootElement.GetProperty("annotations").GetProperty("audience")[0].GetString());
            Assert.Equal(0.75d, doc.RootElement.GetProperty("annotations").GetProperty("priority").GetDouble());
            Assert.Equal("example", doc.RootElement.GetProperty("_meta").GetProperty("vendor").GetString());
        }

        /// <summary>
        /// 协议宽松度:annotations.priority 在上游 schema 上标了 x-deserialize-default-on-error,
        /// 因此提供了却非 JSON number 时必须回落为「未提供」,不得抛错把整个 content block 判死。
        /// 见 src/SalmonEgg.Acp/SchemaTolerance.Fields.txt 与 AGENTS.md「协议宽松度不得反向收紧」。
        /// </summary>
        [Fact]
        public void ContentBlock_AnnotationsPriorityWrongType_DegradesToUnset()
        {
            var json = """
            {
              "type": "text",
              "text": "hello",
              "annotations": {
                "priority": "high"
              }
            }
            """;

            var block = Assert.IsType<TextContentBlock>(
                JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions));

            Assert.NotNull(block.Annotations);
            Assert.Null(block.Annotations!.Priority);
            // 同一 block 里其他字段不受牵连。
            Assert.Equal("hello", block.Text);
        }

        /// <summary>
        /// 协议宽松度:resource_link.size 同样标了 x-deserialize-default-on-error,
        /// 类型错误回落 null 而不是拒绝整块内容。
        /// </summary>
        [Fact]
        public void ContentBlock_ResourceLinkSizeWrongType_DegradesToUnset()
        {
            var json = """
            {
              "type": "resource_link",
              "uri": "file:///tmp/a.bin",
              "size": "1024"
            }
            """;

            var block = Assert.IsType<ResourceLinkContentBlock>(
                JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions));

            Assert.Null(block.Size);
            Assert.Equal("file:///tmp/a.bin", block.Uri);
        }

        /// <summary>
        /// 类型契约:resource content 类型缺失必填 resource 字段时读取即拒绝(fail-fast),
        /// 不得存 null 延迟到序列化才 NRE。官方 content schema 中 resource 变体的
        /// resource 字段为 required,且**没有**容忍标注,所以这里仍须抛。
        /// </summary>
        [Fact]
        public void ContentBlock_ResourceMissingRequiredResource_Throws()
        {
            var json = """
            {
              "type": "resource",
              "annotations": {
                "priority": 0.5
              }
            }
            """;

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions));
        }

        /// <summary>
        /// 类型契约:必填字符串字段(如 text.text)提供了却非 JSON string 时,
        /// 必须抛 JsonException——与本层其余读取器的异常类型保持一致,
        /// 不得外泄裸 InvalidOperationException(嵌套反序列化时会污染调用方)。
        /// TextContent.text 在上游 schema 上**没有**容忍标注,与 priority/size 的处置正好相反,
        /// 这一对用例即是「只在无标注处收紧」的正反样本。
        /// </summary>
        [Fact]
        public void ContentBlock_StringFieldWrongType_ThrowsJsonException()
        {
            var json = """
            {
              "type": "text",
              "text": 42
            }
            """;

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions));
        }

        /// <summary>
        /// 协议宽松度:annotations.audience 标了 x-deserialize-skip-invalid-items,
        /// 因此单个非字符串元素必须被跳过、其余元素保留,不得因一个坏元素丢掉整条消息。
        /// </summary>
        [Fact]
        public void ContentBlock_StringListWrongElementType_SkipsInvalidItem()
        {
            var json = """
            {
              "type": "text",
              "text": "hello",
              "annotations": {
                "audience": ["user", 7, "assistant"]
              }
            }
            """;

            var block = Assert.IsType<TextContentBlock>(
                JsonSerializer.Deserialize<ContentBlock>(json, _jsonOptions));

            Assert.NotNull(block.Annotations);
            Assert.Equal(new[] { "user", "assistant" }, block.Annotations!.Audience);
        }

        /// <summary>
        /// 属性 7：内容块多态序列化和反序列化
        /// </summary>
        [Fact]
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
            Assert.NotNull(deserialized);
            Assert.Equal(blocks.Count, deserialized!.Length);
            Assert.IsAssignableFrom<TextContentBlock>(deserialized[0]);
            Assert.IsAssignableFrom<ImageContentBlock>(deserialized[1]);
            Assert.IsAssignableFrom<AudioContentBlock>(deserialized[2]);
        }

        /// <summary>
        /// 属性 7：ContentBlock 数组序列化时 type 字段存在
        /// </summary>
        [Fact]
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
            Assert.True(doc.RootElement.GetArrayLength() > 0);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                Assert.True(element.TryGetProperty("type", out _), "每个 ContentBlock 必须包含 type 字段");
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
                Priority = prioritySeed % 101 / 100d,
                LastModified = lastModified
            };
        }

        private static void AssertAnnotations(Annotations? actual, Annotations? expected)
        {
            Assert.NotNull(actual);
            Assert.NotNull(expected);
            Assert.Equal(expected!.Audience, actual!.Audience);
            Assert.Equal(expected.Priority, actual.Priority);
            Assert.Equal(expected.LastModified, actual.LastModified);
        }
    }
}
