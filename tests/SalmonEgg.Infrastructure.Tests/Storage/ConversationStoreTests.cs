using System;
using System.IO;
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
                        UpdatedAtUtc = null,
                        HasUpdatedAt = true
                    }
                }
            }
        };

        await sut.SaveAsync(document);
        var loaded = await sut.LoadAsync();

        var conversation = Assert.Single(loaded.Conversations);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(conversation.SessionInfo);
        Assert.Null(sessionInfo.Title);
        Assert.True(sessionInfo.HasTitle);
        Assert.Null(sessionInfo.UpdatedAtUtc);
        Assert.True(sessionInfo.HasUpdatedAt);
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
