using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConversationStoreTests : IDisposable
{
    private readonly string _root;

    public ConversationStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggConversationStoreTests", Guid.NewGuid().ToString("N"));
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
    public async Task SaveAsync_WhenSessionInfoHasPresenceMetadata_RoundTripsPresenceFlags()
    {
        var sut = CreateStore();
        var document = new ConversationDocument
        {
            Conversations =
            {
                new ConversationRecord
                {
                    ConversationId = "conversation-1",
                    SessionInfo = new ConversationSessionInfoSnapshot
                    {
                        Title = null,
                        HasTitle = true,
                        AdditionalDirectories = [@"C:\shared\one", @"D:\shared\two"],
                        UpdatedAtUtc = null,
                        HasUpdatedAt = true
                    }
                }
            }
        };

        await sut.SaveAsync(document, TestContext.Current.CancellationToken);
        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        var conversation = Assert.Single(loaded.Conversations);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(conversation.SessionInfo);
        Assert.Null(sessionInfo.Title);
        Assert.True(sessionInfo.HasTitle);
        Assert.Equal([@"C:\shared\one", @"D:\shared\two"], sessionInfo.AdditionalDirectories);
        Assert.Null(sessionInfo.UpdatedAtUtc);
        Assert.True(sessionInfo.HasUpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_WhenSessionInfoMetaContainsJsonElements_PreservesRawTokensAcrossReload()
    {
        using var metaDocument = JsonDocument.Parse(
            """{"number":1.2300e+02,"escaped":"\u4f60\u597d","nested":{"items":[1,true,null]}}""");
        var meta = metaDocument.RootElement.EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
        var document = new ConversationDocument
        {
            Conversations =
            {
                new ConversationRecord
                {
                    ConversationId = "conversation-jsonelement",
                    SessionInfo = new ConversationSessionInfoSnapshot
                    {
                        Meta = meta
                    }
                }
            }
        };
        var sut = CreateStore();

        await sut.SaveAsync(document, TestContext.Current.CancellationToken);
        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);
        var loadedMeta = Assert.IsType<Dictionary<string, object?>>(
            loaded.Conversations[0].SessionInfo!.Meta,
            exactMatch: false);

        Assert.Equal("1.2300e+02", Assert.IsType<JsonElement>(loadedMeta["number"]).GetRawText());
        Assert.Equal("\"\\u4f60\\u597d\"", Assert.IsType<JsonElement>(loadedMeta["escaped"]).GetRawText());
        Assert.Equal(
            "{\"items\":[1,true,null]}",
            Assert.IsType<JsonElement>(loadedMeta["nested"]).GetRawText());

        await sut.SaveAsync(loaded, TestContext.Current.CancellationToken);
        var reloaded = await sut.LoadAsync(TestContext.Current.CancellationToken);
        var reloadedMeta = reloaded.Conversations[0].SessionInfo!.Meta!;

        Assert.Equal("1.2300e+02", Assert.IsType<JsonElement>(reloadedMeta["number"]).GetRawText());
        Assert.Equal("\"\\u4f60\\u597d\"", Assert.IsType<JsonElement>(reloadedMeta["escaped"]).GetRawText());
    }

    private ConversationStore CreateStore()
        => new(new TestAppDataService(_root));

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
