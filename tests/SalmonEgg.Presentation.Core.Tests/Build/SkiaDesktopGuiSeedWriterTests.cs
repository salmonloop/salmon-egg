using System;
using System.IO;
using System.Text.Json;
using SalmonEgg.TestSupport;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class SkiaDesktopGuiSeedWriterTests
{
    [Fact]
    public void WriteMixedTranscriptSeed_WritesRealProductionAppDataLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "salmonegg-skia-seed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var paths = SkiaDesktopGuiSeedWriter.WriteMixedTranscriptSeed(root);

            Assert.True(File.Exists(paths.AppYamlPath));
            Assert.True(File.Exists(paths.ConversationsPath));
            Assert.True(Directory.Exists(paths.ProjectRootPath));
            Assert.Contains(
                "theme: Dark",
                File.ReadAllText(paths.AppYamlPath),
                StringComparison.Ordinal);

            using var document = JsonDocument.Parse(File.ReadAllText(paths.ConversationsPath));
            var rootElement = document.RootElement;
            Assert.Equal(
                SkiaDesktopGuiSeedWriter.ConversationId,
                rootElement.GetProperty("lastActiveConversationId").GetString());

            var conversations = rootElement.GetProperty("conversations").EnumerateArray().ToArray();
            Assert.Equal(2, conversations.Length);

            var conversation = Assert.Single(
                conversations,
                item => item.GetProperty("conversationId").GetString()
                    == SkiaDesktopGuiSeedWriter.ConversationId);
            var plainConversation = Assert.Single(
                conversations,
                item => item.GetProperty("conversationId").GetString()
                    == SkiaDesktopGuiSeedWriter.PlainConversationId);
            var plainMessage = Assert.Single(plainConversation.GetProperty("messages").EnumerateArray());
            Assert.Equal(
                "text",
                plainMessage.GetProperty("contentType").GetString());

            var contentTypes = conversation.GetProperty("messages")
                .EnumerateArray()
                .Select(message => message.GetProperty("contentType").GetString())
                .ToArray();

            Assert.Contains("text", contentTypes);
            Assert.Contains("tool_call", contentTypes);
            Assert.Contains("mode_change", contentTypes);
            Assert.Contains("image", contentTypes);
            Assert.Contains(
                SkiaDesktopGuiSeedWriter.MarkdownMarker,
                File.ReadAllText(paths.ConversationsPath),
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp seed roots.
            }
        }
    }
}
