using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models.ConversationPreview;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConversationPreviewStoreTests : IDisposable
{
    private readonly string _root;

    public ConversationPreviewStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggConversationPreviewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task SaveAsync_WhenEquivalentTranscriptIsSavedAgain_DoesNotRewriteStoredPreview()
    {
        var sut = CreateStore();
        var original = CreateSnapshot(
            "conversation-1",
            generatedAt: new DateTimeOffset(2026, 4, 18, 1, 0, 0, TimeSpan.Zero),
            entries:
            [
                new PreviewEntry("user", "hello", new DateTimeOffset(2026, 4, 18, 1, 0, 1, TimeSpan.Zero)),
                new PreviewEntry("assistant", "world", new DateTimeOffset(2026, 4, 18, 1, 0, 2, TimeSpan.Zero))
            ]);
        var duplicateWithNewTimestamp = CreateSnapshot(
            "conversation-1",
            generatedAt: new DateTimeOffset(2026, 4, 18, 1, 5, 0, TimeSpan.Zero),
            entries:
            [
                new PreviewEntry("user", "hello", new DateTimeOffset(2026, 4, 18, 1, 0, 1, TimeSpan.Zero)),
                new PreviewEntry("assistant", "world", new DateTimeOffset(2026, 4, 18, 1, 0, 2, TimeSpan.Zero))
            ]);

        await sut.SaveAsync(original);
        var previewPath = GetStoredPreviewPath();
        var firstJson = await File.ReadAllTextAsync(previewPath);

        await sut.SaveAsync(duplicateWithNewTimestamp);
        var secondJson = await File.ReadAllTextAsync(previewPath);

        Assert.Equal(firstJson, secondJson);

        var loaded = await sut.LoadAsync("conversation-1");
        Assert.NotNull(loaded);
        Assert.Equal(original.GeneratedAt, loaded.GeneratedAt);
        Assert.Equal(original.Entries, loaded.Entries);
    }

    [Fact]
    public async Task SaveAsync_WhenNewTranscriptArrives_PersistsLatestSnapshot()
    {
        var sut = CreateStore();
        var first = CreateSnapshot(
            "conversation-1",
            generatedAt: new DateTimeOffset(2026, 4, 18, 1, 0, 0, TimeSpan.Zero),
            entries:
            [
                new PreviewEntry("user", "hello", new DateTimeOffset(2026, 4, 18, 1, 0, 1, TimeSpan.Zero))
            ]);
        var second = CreateSnapshot(
            "conversation-1",
            generatedAt: new DateTimeOffset(2026, 4, 18, 1, 1, 0, TimeSpan.Zero),
            entries:
            [
                new PreviewEntry("user", "hello", new DateTimeOffset(2026, 4, 18, 1, 0, 1, TimeSpan.Zero)),
                new PreviewEntry("assistant", "updated", new DateTimeOffset(2026, 4, 18, 1, 1, 1, TimeSpan.Zero))
            ]);

        await Task.WhenAll(
            sut.SaveAsync(first),
            sut.SaveAsync(second));

        var loaded = await sut.LoadAsync("conversation-1");
        Assert.NotNull(loaded);
        Assert.Equal(second.GeneratedAt, loaded.GeneratedAt);
        Assert.Equal(second.Entries, loaded.Entries);
    }

    [Fact]
    public void Constructor_DoesNotTouchPreviewDirectory()
    {
        _ = CreateStore();

        Assert.False(Directory.Exists(Path.Combine(_root, "conversation-previews")));
    }

    private ConversationPreviewStore CreateStore()
        => new(new TestAppDataService(_root), NullLogger<ConversationPreviewStore>.Instance);

    private string GetStoredPreviewPath()
        => Assert.Single(Directory.GetFiles(Path.Combine(_root, "conversation-previews"), "*.json"));

    private static ConversationPreviewSnapshot CreateSnapshot(
        string conversationId,
        DateTimeOffset generatedAt,
        IReadOnlyList<PreviewEntry> entries)
        => new(conversationId, entries, generatedAt);

    private sealed class TestAppDataService : IAppDataService
    {
        public TestAppDataService(string root)
        {
            AppDataRootPath = root;
        }

        public string AppDataRootPath { get; }
        public string ConfigRootPath => Path.Combine(AppDataRootPath, "config");
        public string LogsDirectoryPath => Path.Combine(AppDataRootPath, "logs");
        public string CacheRootPath => Path.Combine(AppDataRootPath, "cache");
        public string ExportsDirectoryPath => Path.Combine(AppDataRootPath, "exports");
    }
}
