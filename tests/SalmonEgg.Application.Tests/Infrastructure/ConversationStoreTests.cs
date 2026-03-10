using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Application.Tests.Infrastructure;

public sealed class ConversationStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "salmon-egg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", root);

        IAppDataService appData = new AppDataService();
        IConversationStore store = new ConversationStore(appData);

        var doc = new ConversationDocument
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
                        },
                        new ConversationMessageSnapshot
                        {
                            Id = "m2",
                            IsOutgoing = false,
                            ContentType = "tool_call",
                            ToolCallId = "tc1",
                            ToolCallKind = ToolCallKind.Execute,
                            ToolCallStatus = ToolCallStatus.InProgress,
                            ToolCallJson = "{ }"
                        }
                    }
                }
            }
        };

        await store.SaveAsync(doc);

        var loaded = await store.LoadAsync();
        Assert.Equal("c1", loaded.LastActiveConversationId);
        Assert.Single(loaded.Conversations);
        Assert.Equal("My Session", loaded.Conversations[0].DisplayName);
        Assert.Equal(2, loaded.Conversations[0].Messages.Count);
        Assert.Equal("hi", loaded.Conversations[0].Messages[0].TextContent);
        Assert.Equal("tc1", loaded.Conversations[0].Messages[1].ToolCallId);
    }
}
