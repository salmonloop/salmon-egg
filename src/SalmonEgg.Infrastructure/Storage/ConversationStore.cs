using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConversationStore : IConversationStore
{
    private static readonly JsonTypeInfo<ConversationDocument> ConversationDocumentJsonType =
        ConversationJsonContext.Default.ConversationDocument;

    private readonly IAppDataService _appData;

    public ConversationStore(IAppDataService appData)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
    }

    public async Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetDocumentPath();
            if (!File.Exists(path))
            {
                return new ConversationDocument();
            }

            await using var stream = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync(stream, ConversationDocumentJsonType, cancellationToken).ConfigureAwait(false);
            return doc ?? new ConversationDocument();
        }
        catch
        {
            // Corrupted file or schema changes: do not crash the app, just start fresh.
            return new ConversationDocument();
        }
    }

    public async Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var json = JsonSerializer.Serialize(document, ConversationDocumentJsonType);
        await AtomicFile.WriteUtf8AtomicAsync(GetDocumentPath(), json, cancellationToken).ConfigureAwait(false);
    }

    private string GetDocumentPath()
    {
        return Path.Combine(_appData.AppDataRootPath, "conversations", "conversations.v1.json");
    }
}
