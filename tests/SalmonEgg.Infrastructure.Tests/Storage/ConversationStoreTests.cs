using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SalmonEgg.Acp.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Tool;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task SaveAsync_WithToolCallAndPlanWireValues_RoundTripsThroughDomainConverters()
    {
        var sut = CreateStore();
        var document = new ConversationDocument
        {
            Conversations =
            {
                new ConversationRecord
                {
                    ConversationId = "conversation-tool",
                    Messages =
                    {
                        new ConversationMessageSnapshot
                        {
                            Id = "tool-1",
                            ContentType = "tool_call",
                            ToolCallId = "call-1",
                            ToolCallKind = ToolCallKind.Execute.ToString(),
                            ToolCallStatus = ToolCallStatus.Completed.ToString(),
                            ToolCallContent = JsonSerializer.SerializeToElement(
                                new List<ToolCallContent> { new ContentToolCallContent(new TextContentBlock("ran ls")) },
                                AcpJsonContext.Default.ListToolCallContent),
                            ToolCallLocations = JsonSerializer.SerializeToElement(
                                new List<ToolCallLocation> { new ToolCallLocation("/tmp/a.txt", 12u) },
                                AcpJsonContext.Default.ListToolCallLocation),
                            PlanEntry = new ConversationPlanEntrySnapshot
                            {
                                Content = "inspect workspace",
                                Status = PlanEntryStatus.Completed.ToString(),
                                Priority = PlanEntryPriority.High.ToString()
                            }
                        }
                    },
                    Plan =
                    {
                        new ConversationPlanEntrySnapshot
                        {
                            Content = "inspect workspace",
                            Status = PlanEntryStatus.InProgress.ToString(),
                            Priority = PlanEntryPriority.Medium.ToString()
                        }
                    }
                }
            }
        };

        await sut.SaveAsync(document, TestContext.Current.CancellationToken);
        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        var conversation = Assert.Single(loaded.Conversations);
        var message = Assert.Single(conversation.Messages);
        Assert.Equal(ToolCallKind.Execute.ToString(), message.ToolCallKind);
        Assert.Equal(ToolCallStatus.Completed.ToString(), message.ToolCallStatus);
        var contentList = JsonSerializer.Deserialize(message.ToolCallContent!.Value, AcpJsonContext.Default.ListToolCallContent);
        var content = Assert.IsType<ContentToolCallContent>(Assert.Single(contentList!));
        Assert.Equal("ran ls", Assert.IsType<TextContentBlock>(content.Content).Text);
        var locations = JsonSerializer.Deserialize(message.ToolCallLocations!.Value, AcpJsonContext.Default.ListToolCallLocation);
        var location = Assert.Single(locations!);
        Assert.Equal("/tmp/a.txt", location.Path);
        Assert.Equal(12u, location.Line);
        Assert.Equal(PlanEntryStatus.Completed.ToString(), message.PlanEntry!.Status);
        Assert.Equal(PlanEntryPriority.High.ToString(), message.PlanEntry.Priority);
        var plan = Assert.Single(conversation.Plan);
        Assert.Equal(PlanEntryStatus.InProgress.ToString(), plan.Status);
        Assert.Equal(PlanEntryPriority.Medium.ToString(), plan.Priority);
    }

    [Fact]
    public async Task SaveAsync_WithMessages_RoundTripsDocument()
    {
        var sut = CreateStore();
        var document = new ConversationDocument
        {
            Version = 1,
            LastActiveConversationId = "c1",
            Conversations =
            {
                new ConversationRecord
                {
                    ConversationId = "c1",
                    DisplayName = "My Session",
                    Messages =
                    {
                        new ConversationMessageSnapshot
                        {
                            Id = "m1",
                            IsOutgoing = true,
                            ContentType = "text",
                            TextContent = "hi"
                        }
                    }
                }
            }
        };

        await sut.SaveAsync(document, TestContext.Current.CancellationToken);
        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("c1", loaded.LastActiveConversationId);
        var conversation = Assert.Single(loaded.Conversations);
        Assert.Equal("My Session", conversation.DisplayName);
        Assert.Equal("hi", Assert.Single(conversation.Messages).TextContent);
    }

    [Fact]
    public async Task LoadAsync_WhenDocumentMissing_ReturnsEmptyDocument()
    {
        var sut = CreateStore();

        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Conversations);
    }

    [Fact]
    public async Task LoadAsync_WhenDocumentCorrupted_QuarantinesFileAndReturnsEmptyDocument()
    {
        var sut = CreateStore();
        var path = GetDocumentPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json !!", TestContext.Current.CancellationToken);

        var loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Conversations);
        Assert.False(File.Exists(path));
        var quarantinePath = path + ".corrupt";
        Assert.True(File.Exists(quarantinePath));
        Assert.Equal("{ not json !!", await File.ReadAllTextAsync(quarantinePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_WhenDocumentLocked_PropagatesIoFailure()
    {
        var sut = CreateStore();
        var path = GetDocumentPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);
        using var exclusiveLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAsync<IOException>(
            () => sut.LoadAsync(TestContext.Current.CancellationToken));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_PropagatesCancellation()
    {
        var sut = CreateStore();
        var path = GetDocumentPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.LoadAsync(cancelled.Token));

        Assert.True(File.Exists(path));
    }

    private string GetDocumentPath()
        => Path.Combine(_root, "conversations", "conversations.v1.json");

    private ConversationStore CreateStore()
        => new(new TestAppDataService(_root), NullLogger<ConversationStore>.Instance);

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
